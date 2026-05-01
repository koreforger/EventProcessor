using KF.Logging;

namespace EventProcessor.Logging;

[LogEventSource(LoggerRootTypeName = "EventProcessorLogger", BasePath = "EventProcessor")]
public enum EventProcessorLogEvents
{
    // 1000–1099 APP — startup, shutdown, configuration
    APP_Startup_Begin = 1000,
    APP_Startup_Complete = 1001,
    APP_Shutdown_Begin = 1002,
    APP_Shutdown_Complete = 1003,
    APP_Config_Reloaded = 1010,

    // 2000–2099 KAFKA — consumer lifecycle
    KAFKA_Consumer_Starting = 2000,
    KAFKA_Consumer_Started = 2001,
    KAFKA_Consumer_Stopping = 2002,
    KAFKA_Consumer_Stopped = 2003,
    KAFKA_Consumer_Assigned = 2010,
    KAFKA_Consumer_Revoked = 2011,
    KAFKA_Consumer_Rebalance = 2012,
    KAFKA_Consumer_Error = 2020,
    KAFKA_Batch_Received = 2030,
    KAFKA_Batch_Committed = 2031,
    KAFKA_Batch_CommitFailed = 2032,

    // 3000–3099 SESSION — session lifecycle
    SESSION_Created = 3000,
    SESSION_Updated = 3001,
    SESSION_Expired = 3002,
    SESSION_Evicted = 3003,
    SESSION_Snapshot_Taken = 3010,

    // 4000–4099 RULES — rule evaluation
    RULES_Loaded = 4000,
    RULES_LoadFailed = 4001,
    RULES_Refreshed = 4002,
    RULES_Evaluated = 4010,
    RULES_EvaluateFailed = 4011,
    RULES_Matched = 4020,
    RULES_NotMatched = 4021,

    // 5000–5099 DECISION — output decisions
    DECISION_Approved = 5000,
    DECISION_Flagged = 5001,
    DECISION_Blocked = 5002,
    DECISION_Deferred = 5003,
    DECISION_Failed = 5010,

    // 6000–6099 SEEK — offset/datetime seek feature
    SEEK_Mode_None = 6000,
    SEEK_From_Offset = 6001,
    SEEK_From_Timestamp = 6002,
    SEEK_Range_Configured = 6003,
    SEEK_Applied = 6010,
    SEEK_Failed = 6011,
    SEEK_Stop_Reached = 6020,
    SEEK_Timestamp_Resolved = 6030,

    // 8000–8099 WORKER — background workers, outbox
    WORKER_Started = 8000,
    WORKER_Stopped = 8001,
    WORKER_Error = 8010,
    WORKER_Outbox_Sent = 8020,
    WORKER_Outbox_Failed = 8021,

    // 9000–9099 INFRA — health, SQL, HTTP
    INFRA_Health_Started = 9000,
    INFRA_Health_Failed = 9001,
    INFRA_Sql_Error = 9010,
    INFRA_Http_Error = 9020,
}
