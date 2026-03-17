using EventProcessor.Logging;
using KoreForge.Jex;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Services;

/// <summary>
/// Extracts fields from incoming JSON messages using a compiled JEX script.
/// The script maps raw Kafka message fields to a canonical shape (nid, timestamp, amount, etc.).
/// </summary>
public sealed class JexFieldExtractorService
{
    private const string DefaultScript = """
        %set $.nid = coalescePath($in, "$.nid", "$.NID", "$.customerId", "$.customer_id");
        %set $.transactionId = coalescePath($in, "$.transactionId", "$.transaction_id", "$.txnId");
        %set $.timestamp = coalescePath($in, "$.timestamp", "$.eventTime", "$.event_time");
        %set $.amount = coalescePath($in, "$.amount", "$.Amount", "$.transactionAmount");
        %set $.currency = coalescePath($in, "$.currency", "$.Currency");
        %set $.transactionType = coalescePath($in, "$.transactionType", "$.transaction_type", "$.type");
        %set $.merchantId = coalescePath($in, "$.merchantId", "$.merchant_id");
        %set $.merchantCategory = coalescePath($in, "$.merchantCategory", "$.merchant_category", "$.mcc");
        %set $.countryCode = coalescePath($in, "$.countryCode", "$.country_code", "$.country");
        %set $.channel = coalescePath($in, "$.channel", "$.Channel");
        %set $.deviceId = coalescePath($in, "$.deviceId", "$.device_id");
        %set $.ipAddress = coalescePath($in, "$.ipAddress", "$.ip_address", "$.ip");
        """;

    private readonly EventProcessorLog<JexFieldExtractorService> _log;
    private readonly IJexProgram _program;

    public JexFieldExtractorService(
        EventProcessorLog<JexFieldExtractorService> log,
        IJexCompiler compiler)
        : this(log, compiler, DefaultScript)
    {
    }

    public JexFieldExtractorService(
        EventProcessorLog<JexFieldExtractorService> log,
        IJexCompiler compiler,
        string script)
    {
        _log = log;

        try
        {
            _program = compiler.Compile(script);
            _log.Jex.Script.Compiled.LogInformation("Field extraction JEX script compiled successfully");
        }
        catch (Exception ex)
        {
            _log.Jex.Script.Failed.LogError(ex, "Failed to compile field extraction JEX script");
            throw;
        }
    }

    /// <summary>
    /// Runs the JEX extraction script against a raw JSON string.
    /// Returns a JObject with the canonical fields, or null on failure.
    /// </summary>
    public JObject? Extract(string rawJson)
    {
        try
        {
            var input = JToken.Parse(rawJson);
            var result = _program.Execute(input);

            if (result is JObject obj)
            {
                // Warn if key routing field is missing
                if (obj.Value<string>("nid") is null)
                {
                    _log.Jex.Extract.Missing.LogWarning("Extracted result has no 'nid' field");
                }

                return obj;
            }

            _log.Jex.Extract.Failed.LogWarning(
                "JEX script returned {TokenType} instead of object", result?.Type);
            return null;
        }
        catch (Exception ex)
        {
            _log.Jex.Extract.Failed.LogError(ex, "JEX extraction failed for message");
            return null;
        }
    }
}
