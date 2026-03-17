using Newtonsoft.Json;

namespace EventProcessor.Models;

/// <summary>
/// Incoming transaction event from Kafka.
/// </summary>
public sealed class TransactionEvent
{
    [JsonProperty("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonProperty("nid")]
    public string NID { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; } = "USD";

    [JsonProperty("transactionType")]
    public string TransactionType { get; set; } = string.Empty;

    [JsonProperty("merchantId")]
    public string? MerchantId { get; set; }

    [JsonProperty("merchantCategory")]
    public string? MerchantCategory { get; set; }

    [JsonProperty("countryCode")]
    public string? CountryCode { get; set; }

    [JsonProperty("channel")]
    public string? Channel { get; set; }

    [JsonProperty("deviceId")]
    public string? DeviceId { get; set; }

    [JsonProperty("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonProperty("sessionId")]
    public string? SessionId { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Generic fraud event that can carry different payloads.
/// </summary>
public abstract class FraudEvent
{
    public string EventType { get; set; } = string.Empty;
    public string NID { get; set; } = string.Empty;
    public DateTimeOffset EventTime { get; set; }
}

/// <summary>
/// Close session command event.
/// </summary>
public sealed class CloseSessionEvent : FraudEvent
{
    public string Reason { get; set; } = "idle";
}

/// <summary>
/// Recalculate score event.
/// </summary>
public sealed class RecalculateScoreEvent : FraudEvent
{
    public bool ForceFlush { get; set; }
}
