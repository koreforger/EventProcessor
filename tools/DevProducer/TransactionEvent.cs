using Newtonsoft.Json;

namespace DevProducer;

public sealed class TransactionEvent
{
    [JsonProperty("transactionId")]    public string          TransactionId    { get; set; } = "";
    [JsonProperty("nid")]              public string          NID              { get; set; } = "";
    [JsonProperty("timestamp")]        public DateTimeOffset  Timestamp        { get; set; }
    [JsonProperty("amount")]           public decimal         Amount           { get; set; }
    [JsonProperty("currency")]         public string          Currency         { get; set; } = "USD";
    [JsonProperty("transactionType")]  public string          TransactionType  { get; set; } = "";
    [JsonProperty("merchantId")]       public string?         MerchantId       { get; set; }
    [JsonProperty("merchantCategory")] public string?         MerchantCategory { get; set; }
    [JsonProperty("countryCode")]      public string?         CountryCode      { get; set; }
    [JsonProperty("channel")]          public string?         Channel          { get; set; }
    [JsonProperty("deviceId")]         public string?         DeviceId         { get; set; }
    [JsonProperty("ipAddress")]        public string?         IpAddress        { get; set; }
    [JsonProperty("sessionId")]        public string?         SessionId        { get; set; }
}
