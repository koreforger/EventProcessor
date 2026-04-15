# EventProcessor — Capabilities & Design

## What Is This?

The EventProcessor is a **real-time fraud detection service**. It watches a stream of financial transactions as they happen, decides whether each one looks suspicious, and publishes that decision so downstream systems can act on it (block the transaction, flag it for review, or let it through).

It processes transactions **one at a time, very fast** — the goal is sub-millisecond decisions so legitimate customers are not slowed down.

---

## How It Works — The Big Picture

```mermaid
flowchart LR
    subgraph "Message Broker (Redpanda / Kafka)"
        IN["transactions topic"]
        OUT["fraud-decisions topic\n(JSON)"]
    end

    subgraph "EventProcessor Service"
        direction TB
        DECODE["Step 1 · Decode"]
        JEX["🔧 Step 2 · JEX Extract\n(your script)"]
        EVAL["🔧 Step 3 · Fraud Evaluation\n(your rules)"]
        CUSTOM["🔧 ... additional steps\n(your code)"]
        FLUSH["Flush Coordinator\n(background timer)"]
    end

    FASTER["FASTER\nIn-Memory Session Store"]
    SQL["SQL Server\n(permanent storage)"]

    IN --> DECODE --> JEX --> EVAL --> CUSTOM

    FASTER -- "① read session" --> EVAL
    EVAL -- "③ write session back" --> FASTER

    EVAL -- "④ publish JSON\ndecision" --> OUT
    FASTER -. "periodic\nbatch save" .-> FLUSH
    FLUSH --> SQL

    style JEX fill:#e8f5e9,stroke:#388e3c
    style EVAL fill:#e8f5e9,stroke:#388e3c
    style CUSTOM fill:#e8f5e9,stroke:#388e3c,stroke-dasharray: 5 5
```

> **Green boxes (🔧) are extensibility points** — they are where you plug in your own behaviour without modifying the framework code. The dashed box represents optional additional pipeline steps you can insert.

**Why FASTER exists:** It is a shared memory cache that accumulates transaction history per customer. When a new transaction arrives, Step 3 **reads** that customer's session **from FASTER** first — that's how it knows the customer already spent 45,000 NOK in 3 previous transactions. After scoring, it **writes** the updated session (now 100,000 NOK across 4 transactions) **back to FASTER** so the *next* transaction for that customer will see the full picture. Without FASTER, every transaction would be evaluated in isolation with no knowledge of what came before.

The SQL flush is secondary — it runs on a background timer to copy sessions to permanent storage for reporting and durability. It is **not** part of the real-time processing loop.

---

## Walking Through a Single Transaction

Imagine a transaction arrives from Kafka: *"NID 12345678 spent 55,000 NOK at a merchant in Russia."*

### Step 1 — Decode

The raw message arrives as a stream of bytes. This step converts those bytes into a readable JSON text string. If the message is empty or corrupted, processing stops here — the record is discarded and an error is logged.

### Step 2 — JEX Field Extraction

Transaction messages can come from different source systems with different field names (e.g., one system calls it `"customerId"`, another calls it `"nid"`, another calls it `"nationalId"`). The JEX extractor is a small script that normalizes all of these into a single consistent format:

| Output field | Meaning |
|-------------|---------|
| `nid` | The customer's national ID number |
| `amount` | Transaction amount |
| `countryCode` | Where the transaction happened |
| `transactionId` | Unique transaction reference |
| `timestamp` | When it happened |

This means the fraud rules don't need to know about source system differences — they always see the same clean fields.

### Step 3 — Fraud Evaluation

This is where the actual fraud detection happens. Several things occur in order:

**3a. Find the customer's session**

Every customer (identified by NID) has a *session* — a running record of their recent activity. The system looks up this session in the in-memory store. If this is the first transaction we've seen for this NID, a new session is created.

> **Why is the session important?** A single transaction of 500 NOK is fine. But if the same person has made 15 transactions in the last minute totaling 120,000 NOK, that pattern is suspicious. The session gives us that history.

**3b. Update the session with this transaction**

- Increment the transaction count
- Add the amount to the running total
- Record the country (if this is the first transaction, it becomes the customer's "base country")
- Add the transaction to the recent transaction list (capped at 100 entries)

**3c. Run the fraud rules**

Five rules are evaluated against the transaction *and* the session:

| Rule | What it checks | Why it matters |
|------|---------------|----------------|
| **High Amount** | Is this single transaction over 10,000? | Large individual transactions are higher risk |
| **Very High Amount** | Is this single transaction over 50,000? | Even higher risk — weighted more heavily |
| **Rapid Transactions** | Has this NID made more than 10 transactions in this session? | Burst activity is a common fraud pattern |
| **Unusual Location** | Is this transaction from a different country than the customer's base country? | Unexpected geography is suspicious |
| **Large Session Total** | Has this NID's session total exceeded 100,000? | High cumulative spending is a risk indicator |

Each rule that matches adds a **score modifier** (a number between 0 and 1, configurable). The modifiers are added together to get a total fraud score.

**3d. Make a decision**

| Total score | Decision | Meaning |
|-------------|----------|---------|
| Below 0.5 | **Allow** | Transaction looks normal |
| 0.5 to 0.99 | **Flagged** | Suspicious — send for human review |
| 1.0 or above | **Blocked** | Very likely fraud — reject automatically |

For our example: the 55,000 NOK transaction from Russia would trigger **VeryHighAmount** (score: 0.6) + **HighAmount** (score: 0.3) + **UnusualLocation** (score: 0.5) = total 1.4 → **Blocked**.

**3e. Save and publish**

- The updated session (with new score and triggered rules) is written back to the in-memory store
- The fraud decision is published as **JSON** to the `fraud-decisions` Kafka topic so any downstream consumer can read it with standard tooling — no special deserializer required

Example output message:
```json
{
  "score": 1.4,
  "decision": "Blocked",
  "decidedAt": "2026-04-15T10:30:00+00:00",
  "triggeredRuleNames": ["VeryHighAmount", "HighAmount", "UnusualLocation"]
}
```

---

## The In-Memory Session Store (FASTER)

### What is it?

FASTER is a high-performance key-value store from Microsoft Research. We use it as an **in-memory dictionary** — think of it as a giant lookup table where the key is the customer's NID and the value is their fraud session.

### What exactly is stored per session?

| Field | What it is |
|-------|-----------|
| NID | The customer identifier (the lookup key) |
| Status | Active, Closed, Flagged, or Blocked |
| Created at | When we first saw this NID |
| Last activity | Timestamp of most recent transaction |
| Transaction count | How many transactions in this session |
| Total amount | Sum of all transaction amounts |
| Recent transactions | List of the last 100 transactions (ID, amount, time, country) |
| Base country | The country from the customer's first transaction |
| Current fraud score | Latest calculated score |
| Triggered rules | Which rules matched on the last evaluation |
| Decision | The most recent Allow / Flagged / Blocked decision |

### Why in-memory? Why not just use the database?

**Speed.** The fraud check must happen while the transaction is in progress. A database round-trip typically takes 5–20 milliseconds. An in-memory lookup in FASTER takes **under 1 millisecond**. When you're processing thousands of transactions per second, that difference matters enormously.

### How is it organized? (Bucket routing)

Imagine 64 filing cabinets (this number is configurable). When a transaction arrives for NID "12345678", the system hashes that NID to determine which cabinet to use — say, cabinet #23. It opens that cabinet, finds the folder (or creates a new one), and does the lookup.

The key benefit: **different cabinets can be accessed simultaneously**. If two transactions arrive at the same time for two different NIDs that map to different cabinets, they don't wait for each other.

### What happens when the service shuts down?

**The in-memory data in FASTER is lost.** This is by design — FASTER is a speed-optimized cache, not permanent storage.

However, the **Flush Coordinator** continuously saves changed sessions to SQL Server in the background (every few seconds). So in practice:

- Sessions that were updated **more than a few seconds before shutdown** are safely in SQL
- Sessions that were updated **in the last few seconds** may be lost

For a fraud detection system, this trade-off is acceptable: the worst case is that a few customers get a "fresh start" on their session when the service restarts, which biases toward *more permissive* (Allow) rather than blocking legitimate activity.

### What happens at startup?

**Currently:** The in-memory store starts empty. Sessions are created fresh as new transactions arrive. This means the system needs a brief warm-up period — during the first few minutes, session-based rules (like "rapid transactions" or "large session total") may not fire because the system hasn't accumulated enough history yet.

**Planned improvement (see Roadmap):** On startup, load existing sessions from SQL back into FASTER so the system resumes with full history. This is a high-priority item for production readiness.

---

## The Flush Coordinator — Saving to SQL

The Flush Coordinator runs on a timer in the background, separate from transaction processing. Every few seconds (configurable), it:

1. Checks if any sessions have changed since the last flush
2. Gathers a batch of changed sessions (up to a configurable limit)
3. Writes them to SQL Server using upsert logic (insert if new, update if existing)

This means SQL is **eventually consistent** — it reflects the state of sessions from a few seconds ago, not the exact current instant. For dashboards and reporting this is perfectly fine; for real-time fraud decisions the service uses the in-memory store.

---

## Observability — How We Know It's Working

### Health Checks

The service exposes standard health endpoints that monitoring tools can poll:

| Endpoint | What it tells you |
|----------|-------------------|
| `/health/live` | Is the process alive? |
| `/health/ready` | Is it connected to Kafka and SQL and able to process? |

### Logging

Every significant event is logged with a structured ID, making it easy to search and filter:

| ID range | Area | Examples |
|----------|------|---------|
| 1000s | Application | Startup, shutdown, configuration changes |
| 2000s | Kafka | Messages consumed, batches processed, errors |
| 3000s | Field extraction | Script loaded, extraction failures |
| 4000s | Fraud rules | Rules loaded, rules matched, decisions made |
| 5000s | Session store | Sessions created, flushed to SQL |
| 6000s | SQL | Settings loaded, persistence errors |

### Metrics

Timing and throughput metrics are collected for all key operations — how fast batches are processed, how long field extraction takes, how long flush cycles take. These can be consumed by a real-time dashboard (planned).

---

## Live Configuration

Rules and thresholds can be changed **without restarting the service**. Settings are stored in SQL Server and the service polls for changes. When an operator updates a rule's score modifier or enables/disables a rule, the change takes effect within seconds.

This means: if fraud patterns shift, the team can adjust thresholds immediately rather than waiting for a code deployment.

---

## Testing

The system has **54 automated tests** covering:

- **Session store** (9 tests): Creating, reading, updating, and flushing sessions; verifying bucket routing distributes evenly
- **Fraud rules** (12 tests): Each of the 5 rules tested individually; disabled rules are skipped; rules reload when configuration changes
- **Full pipeline** (8 tests): End-to-end processing of transactions through all three steps; verifying session accumulation, country detection, and decision output
- **Configuration validation** (10 tests): All required settings verified at startup; the service refuses to start with invalid configuration
- **Health checks** (8 tests): Health endpoints correctly reflect the state of each dependency
- **Field extraction** (4 tests): Valid and invalid input handling
- **Decision producer** (2 tests): The "off switch" (no-op producer) works correctly

**Performance benchmarks** are also available, measuring session store throughput at 1,000 and 10,000 session scales, and rule engine evaluation speed.

---

## Customization & Extensibility

The EventProcessor is designed as a **framework you install once, then customize without touching its internals**. The processing flow (Kafka → decode → extract → evaluate → publish) is fixed infrastructure. What changes between deployments is *your* business logic — your field mapping, your rules, and optionally your own pipeline steps.

### What's Fixed (the framework)

| Component | What it does | Why you don't touch it |
|-----------|-------------|------------------------|
| Kafka consumer loop | Reads batches from the topic, dispatches to the pipeline | Handles offsets, retries, health reporting |
| Decode step | Converts raw bytes → UTF-8 string | Universal — there's nothing to customize |
| FASTER session store | Reads/writes per-NID sessions at sub-millisecond speed | Internal performance plumbing |
| Flush coordinator | Periodically saves changed sessions to SQL | Background durability — runs on a timer |
| Health checks, logging, metrics | Standard observability | Consistent structure across all deployments |

### What You Customize

#### 1. JEX Field Extraction Script

**What:** The JEX script that maps your source system's JSON fields to the canonical format the rules expect.

**How:** Provide your own JEX script. The built-in default handles common field name variations (`customerId`, `nid`, `nationalId`, etc.), but if your source data uses different names or structures, you write a script that maps them. This is a small text — typically 10–15 lines.

**Impact:** Changes which fields are available to the rules. No code changes needed — just a different script.

#### 2. Fraud Rules

**What:** The rules that determine whether a transaction is suspicious.

**How — without code:** Each rule has a name, a score modifier, and an enabled flag. You can change these values in SQL settings and they take effect within seconds (live reload). For example, you can:
- Disable a rule that produces too many false positives
- Increase the score modifier for a rule you want weighted more heavily
- Adjust the decision thresholds (what score counts as Flagged vs Blocked)

**How — with code:** Implement `IFraudRuleEngine` with your own rule logic and register it in DI. Your implementation completely replaces the built-in rule engine. The framework calls your code at the right point in the pipeline — you never modify the pipeline itself.

**Planned:** Write rules as JEX expressions directly in configuration, so analysts can add entirely new rules without any code (see Roadmap 3a).

#### 3. Additional Pipeline Steps

**What:** Insert your own processing steps before or after evaluation.

**How:** Implement `IPipelineStep<TIn, TOut>` and chain it with `.UseStep()` in the pipeline builder. Examples of what you might add:
- An enrichment step that calls an external API to add customer risk data before evaluation
- A notification step that sends an alert to a Slack channel after evaluation
- A logging step that writes to a separate audit trail

Each step receives the output of the previous step and passes its output to the next. If a step returns `Abort()`, the record is dropped without processing further.

#### 4. Swappable Components via Dependency Injection

Every major component is registered behind an interface. You can replace any of them by registering your own implementation:

| Interface | Default | When you'd swap it |
|-----------|---------|--------------------|
| `IFraudRuleEngine` | `SimpleFraudRuleEngine` | You want completely different rule logic |
| `ISessionStore` | `FasterSessionStore` | You want a different storage backend (Redis, etc.) |
| `IFraudDecisionProducer` | `KafkaFraudDecisionProducer` | You want to publish decisions somewhere other than Kafka |

The built-in `NoOpFraudDecisionProducer` is an example — it's a drop-in replacement that discards all decisions, useful for testing or dry-run deployments.

#### 5. Configuration

All operational parameters are externalized in the `FraudEngine` config section — bucket counts, flush intervals, session timeouts, scoring thresholds, Kafka connection details, SQL connection strings. You tune these per environment without rebuilding.

### The key principle

> **You extend the system by providing your own implementations of well-defined interfaces and scripts. You never edit the framework's pipeline, consumer loop, or session management code.** If something breaks, you look at your code — the framework is unchanged.

---

## Current Status — What's Done, What's Not

The core processing pipeline is **functionally complete**: transactions flow in, sessions accumulate, rules fire, decisions are published. The system works end-to-end in a development environment with Docker Compose (Redpanda for Kafka, Azure SQL Edge for the database).

However, there are important gaps before this is production-ready. The next section lays these out.

---

## Roadmap — Prioritized Plan

### Priority 1 — Required for Production

#### 1a. Session Eviction

**What:** Automatically remove old sessions from the in-memory store when they are no longer needed.

**Why this is critical:** Without eviction, the in-memory store grows without bound. Every unique NID that transacts creates a session that stays in memory forever. In a production environment with millions of customers, this will eventually exhaust the 256 MB memory budget and crash the service.

**What needs to happen:**
- Sessions idle for longer than the configured timeout (e.g., 30 minutes of no activity) should be flushed to SQL and then removed from memory
- Sessions that exceed a maximum duration (e.g., 24 hours) should be forcibly closed
- Sessions that exceed a maximum transaction count should be closed and a new one started

The configuration settings for these thresholds already exist (`Sessions.IdleTimeoutMinutes`, `Sessions.MaxTransactionsPerSession`, `Sessions.MaxSessionDurationMinutes`) — they just aren't enforced yet.

#### 1b. Cold-Start Session Recovery

**What:** When the service starts up, load recent sessions from SQL back into the in-memory store.

**Why this is critical:** Without this, every restart means losing all session history. The service needs a warm-up period of minutes or hours before session-based rules (rapid transactions, large totals) become effective. During that window, certain types of fraud will not be detected.

**What needs to happen:**
- On startup, query SQL for sessions that were active within the last N minutes
- Load them into FASTER before the Kafka consumer starts processing
- Only then begin consuming transactions

### Priority 2 — Important for Operations

#### 2a. Dashboard Integration

**What:** A web-based dashboard showing live metrics, active sessions, recent decisions, and configuration controls.

**Why:** Operations teams need visibility into what the system is doing — how many transactions per second it's processing, what percentage are being flagged, whether rule changes had the expected effect. The SignalR infrastructure (real-time data push) and metric collection are already built; the dashboard frontend (Vue.js) needs to be connected to them.

#### 2b. Partitioned Consumers

**What:** Running multiple instances of the EventProcessor that divide the Kafka topic among themselves, so each instance handles a portion of the traffic.

**Why:** A single instance has a throughput ceiling. For very high transaction volumes we need horizontal scaling — adding more instances to share the load.

**How it works:** Kafka supports *consumer groups*. When multiple instances join the same group, Kafka automatically assigns different partitions of the topic to different instances. The key design point is that all transactions for a given NID must go to the **same instance** (because that's where the NID's session lives in memory). Kafka's default partitioning by message key (which is the NID) guarantees this.

**What needs to happen:**
- Validate that the NID is consistently used as the Kafka message key by all producers
- Test multi-instance deployment with partition rebalancing
- Handle the case where partitions are reassigned (session handoff)

### Priority 3 — Enhancements

#### 3a. User-Defined Rules via JEX Expressions

**What:** Allow analysts to write custom fraud rules as JEX expressions (our JSON expression language) instead of requiring code changes for new rules.

**Why:** Currently, the 5 fraud rules are hardcoded. Adding a new rule means a code change and redeployment. With JEX, a new rule could be added via a SQL settings change and picked up within seconds.

#### 3b. Extended Benchmarking

**What:** Comprehensive performance testing under realistic production loads.

**Why:** We have unit-level benchmarks, but we need end-to-end throughput testing with realistic data volumes to validate capacity planning and identify bottlenecks before production deployment.

---

## Technical Reference

<details>
<summary>Configuration reference (click to expand)</summary>

All settings live under the `FraudEngine` section:

```json
{
  "FraudEngine": {
    "Processing": {
      "ConsumerThreads": 8,
      "BucketCount": 64,
      "MaxBatchSize": 500,
      "BatchTimeoutMs": 100
    },
    "Flush": {
      "TimeBasedIntervalMs": 5000,
      "CountThreshold": 1000,
      "DirtyRatioThreshold": 0.3,
      "MemoryPressureThreshold": 0.85
    },
    "Sessions": {
      "IdleTimeoutMinutes": 30,
      "MaxTransactionsPerSession": 10000,
      "MaxSessionDurationMinutes": 1440
    },
    "Scoring": {
      "BaseScore": 0.0,
      "DecisionThreshold": 0.75,
      "HighScoreAlertThreshold": 0.5
    },
    "Kafka": {
      "Consumer": {
        "BootstrapServers": "localhost:29092",
        "GroupId": "fraud-engine",
        "Topics": ["transactions"]
      },
      "Producer": {
        "Enabled": true,
        "BootstrapServers": "localhost:29092",
        "Topic": "fraud-decisions"
      }
    },
    "SqlSink": {
      "ConnectionString": "Server=localhost,14333;...",
      "BatchSize": 500,
      "TimeoutSeconds": 30
    },
    "Rules": [
      { "Name": "HighAmount", "ScoreModifier": 0.3, "Enabled": true },
      { "Name": "VeryHighAmount", "ScoreModifier": 0.6, "Enabled": true },
      { "Name": "RapidTransactions", "ScoreModifier": 0.4, "Enabled": true },
      { "Name": "UnusualLocation", "ScoreModifier": 0.5, "Enabled": true },
      { "Name": "LargeSessionTotal", "ScoreModifier": 0.7, "Enabled": true }
    ]
  }
}
```

</details>

<details>
<summary>Docker Compose services (click to expand)</summary>

| Service | Port | Purpose |
|---------|------|---------|
| redpanda | 29092 | Kafka-compatible message broker |
| sqledge | 14333 | SQL Server (settings + session persistence) |
| console | 8080 | Redpanda web management UI |

</details>

<details>
<summary>Dev tooling (click to expand)</summary>

| Tool | Purpose |
|------|---------|
| `thr-bench.ps1` | Measures raw Kafka throughput (no processing) |
| `catchup-bench.ps1` | Measures end-to-end processing speed |
| `DevProducer/` | Generates synthetic transactions for testing |

</details>
