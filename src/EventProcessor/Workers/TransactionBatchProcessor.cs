using System.Text;
using EventProcessor.Logging;
using EventProcessor.Services;
using KF.Kafka.Consumer.Abstractions;
using KF.Kafka.Consumer.Batch;
using KF.Metrics;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Workers;

/// <summary>
/// Kafka batch processor that extracts fields from each message using JEX
/// and routes them for fraud rule evaluation.
/// Observability is provided through KoreForge.Metrics IOperationMonitor scopes.
/// </summary>
public sealed class TransactionBatchProcessor : IKafkaBatchProcessor
{
    // Operation name constants — never use magic strings at call sites.
    private const string OpBatch = "kafka.batch.process";
    private const string OpExtract = "jex.extract";

    private readonly EventProcessorLog<TransactionBatchProcessor> _log;
    private readonly JexFieldExtractorService _extractor;
    private readonly IOperationMonitor _monitor;
    private long _messageCount;

    public TransactionBatchProcessor(
        EventProcessorLog<TransactionBatchProcessor> log,
        JexFieldExtractorService extractor,
        IOperationMonitor monitor)
    {
        _log = log;
        _extractor = extractor;
        _monitor = monitor;
    }

    public Task ProcessAsync(KafkaRecordBatch batch, CancellationToken cancellationToken)
    {
        using var batchScope = _monitor.Begin(OpBatch);

        _log.Kafka.Consumer.Batched.LogInformation(
            "Batch received: {Count} records", batch.Count);

        try
        {
            foreach (var record in batch.Records)
            {
                var count = Interlocked.Increment(ref _messageCount);

                try
                {
                    var json = Encoding.UTF8.GetString(record.Message.Value);

                    using var extractScope = _monitor.Begin(OpExtract);
                    JObject? extracted;
                    try
                    {
                        extracted = _extractor.Extract(json);
                    }
                    catch
                    {
                        extractScope.MarkFailed();
                        throw;
                    }

                    if (extracted == null)
                    {
                        extractScope.MarkFailed();
                        _log.Jex.Extract.Failed.LogWarning(
                            "Field extraction returned null for offset {Offset}", record.Offset.Value);
                        continue;
                    }

                    // Log every 100th message or first 10 for visibility.
                    if (count <= 10 || count % 100 == 0)
                    {
                        _log.Kafka.Consumer.Received.LogInformation(
                            "[{Count}] Extracted: NID={NID}, Amount={Amount}, Timestamp={Timestamp}",
                            count,
                            extracted.Value<string>("nid"),
                            extracted.Value<decimal?>("amount"),
                            extracted.Value<string>("timestamp"));
                    }

                    // TODO: Route to bucket based on NID hash
                    // TODO: Evaluate fraud rules via JEX rule engine
                    // TODO: Update FASTER state
                    // TODO: Produce decisions to output topic
                }
                catch (Exception ex)
                {
                    _log.Kafka.Consumer.Error.LogError(ex,
                        "Failed to process record at offset {Offset}", record.Offset.Value);
                }
            }
        }
        catch
        {
            batchScope.MarkFailed();
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>Total messages processed since startup.</summary>
    public long MessageCount => Interlocked.Read(ref _messageCount);
}

