using KF.Logging;

namespace EventProcessor.Logging;

/// <summary>
/// Structured log event IDs for EventProcessor.
/// Naming: AREA_SubArea_EventName = numericId
///
/// IMPORTANT: Each underscore-separated token must be a single word (no PascalCase compounds)
/// because the source generator lowercases all characters after the first in each token.
///
/// ID ranges:
///   1000–1999  APP     — Application lifecycle (startup, shutdown, config)
///   2000–2999  KAFKA   — Kafka consumer/producer operations
///   3000–3999  JEX     — JEX script compilation and field extraction
///   4000–4999  RULES   — Fraud rule evaluation and scoring
///   5000–5999  SESSION — Session state management (FASTER, flush)
///   6000–6999  SQL     — SQL sink and persistence
///   9000–9999  INFRA   — Health checks, diagnostics, infrastructure
///
/// To extend: add new enum members in the appropriate range.
/// The source generator will automatically create/update the logger hierarchy.
/// </summary>
[LogEventSource(LoggerRootTypeName = "EventProcessorLog", BasePath = "EventProcessor")]
public enum EventProcessorLogEvents
{
    // ── APP (1000–1999) ─────────────────────────────────────────────
    APP_Starting              = 1000,
    APP_Started               = 1001,
    APP_Stopping              = 1002,
    APP_Stopped               = 1003,
    APP_Config_Loaded         = 1010,
    APP_Config_Changed        = 1011,
    APP_Config_Error          = 1012,

    // ── KAFKA (2000–2999) ───────────────────────────────────────────
    KAFKA_Consumer_Starting   = 2000,
    KAFKA_Consumer_Started    = 2001,
    KAFKA_Consumer_Stopping   = 2002,
    KAFKA_Consumer_Stopped    = 2003,
    KAFKA_Consumer_Assigned   = 2010,
    KAFKA_Consumer_Revoked    = 2011,
    KAFKA_Consumer_Error      = 2012,
    KAFKA_Consumer_Received   = 2020,
    KAFKA_Consumer_Batched    = 2021,
    KAFKA_Consumer_Stats      = 2030,
    KAFKA_Consumer_Eof        = 2031,
    KAFKA_Producer_Sent       = 2050,
    KAFKA_Producer_Error      = 2051,

    // ── JEX (3000–3999) ─────────────────────────────────────────────
    JEX_Script_Compiled       = 3000,
    JEX_Script_Failed         = 3001,
    JEX_Script_Reloaded       = 3002,
    JEX_Extract_Completed     = 3010,
    JEX_Extract_Failed        = 3011,
    JEX_Extract_Missing       = 3012,

    // ── RULES (4000–4999) ───────────────────────────────────────────
    RULES_Loaded              = 4000,
    RULES_Reloaded            = 4001,
    RULES_Evaluated           = 4010,
    RULES_Matched             = 4011,
    RULES_Computed            = 4020,
    RULES_Decided             = 4021,
    RULES_Error_Load          = 4002,
    RULES_Error_Eval          = 4012,

    // ── SESSION (5000–5999) ─────────────────────────────────────────
    SESSION_Created           = 5000,
    SESSION_Updated           = 5001,
    SESSION_Closed            = 5002,
    SESSION_Flagged           = 5003,
    SESSION_Blocked           = 5004,
    SESSION_Flush_Started     = 5010,
    SESSION_Flush_Completed   = 5011,
    SESSION_Flush_Error       = 5012,

    // ── SQL (6000–6999) ─────────────────────────────────────────────
    SQL_Settings_Loaded       = 6000,
    SQL_Settings_Changed      = 6001,
    SQL_Settings_Error        = 6002,
    SQL_Sink_Persisted        = 6010,
    SQL_Sink_Error            = 6011,

    // ── INFRA (9000–9999) ───────────────────────────────────────────
    INFRA_Health_Checked      = 9000,
    INFRA_Health_Degraded     = 9001,
    INFRA_Diagnostics         = 9010,
}
