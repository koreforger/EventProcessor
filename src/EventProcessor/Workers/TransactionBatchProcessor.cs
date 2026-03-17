using System.Diagnostics.Metrics;
using System.Text;
using EventProcessor.Logging;
using EventProcessor.Services;
using KF.Kafka.Consumer.Abstractions;
using KF.Kafka.Consumer.Batch;

namespace EventProcessor.Workers;

/// <summary>
/// Kafka batch processor that extracts fields from each message using JEX
/// and routes them for fraud rule evaluation.
/// </summary>
public sealed class TransactionBatchProcessor : IKafkaBatchProcessor
{
    private static readonly Meter s_meter = new("EventProcessor");
    private static readonly Counter<long> s_messagesConsumed = s_meter.CreateCounter<long>("messages.consumed");
    private static readonly Counter<long> s_batchesProcessed = s_meter.CreateCounter<long>("batches.processed");
    private static readonly Counter<long> s_extractionsDone = s_meter.CreateCounter<long>("jex.extractions");
    private static readonly Counter<long> s_errorsTotal = s_meter.CreateCounter<long>("errors.total");

    private readonly EventProcessorLog<TransactionBatchProcessor> _log;
    private readonly JexFieldExtractorService _extractor;
    private long _messageCount;
    private long _errorCount;

    public TransactionBatchProcessor(
        EventProcessorLog<TransactionBatchProcessor> log,
        JexFieldExtractorService extractor)
    {
        _log = log;
        _extractor = extractor;
    }

    public Task ProcessAsync(KafkaRecordBatch batch, CancellationToken cancellationToken)
    {
        s_batchesProcessed.Add(1);
        _log.Kafka.Consumer.Batched.LogInformation(
            "Batch received: {Count} records", batch.Count);

        foreach (var record in batch.Records)
        {
            var count = Interlocked.Increment(ref _messageCount);
            s_messagesConsumed.Add(1);

            try
            {
                var json = Encoding.UTF8.GetString(record.Message.Value);
                var extracted = _extractor.Extract(json);

                if (extracted == null)
                {
                    _log.Jex.Extract.Failed.LogWarning(
                        "Field extraction returned null for offset {Offset}", record.Offset.Value);
                    continue;
                }

                s_extractionsDone.Add(1);

                // Log every 100th message or first 10 for visibility
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
                Interlocked.Increment(ref _errorCount);
                s_errorsTotal.Add(1);
                _log.Kafka.Consumer.Error.LogError(ex,
                    "Failed to process record at offset {Offset}", record.Offset.Value);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Total messages processed since startup.</summary>
    public long MessageCount => Interlocked.Read(ref _messageCount);

    /// <summary>Total processing errors since startup.</summary>
    public long ErrorCount => Interlocked.Read(ref _errorCount);
}
