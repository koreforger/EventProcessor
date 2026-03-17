using KF.Kafka.Consumer.Abstractions;
using KF.Kafka.Consumer.Batch;
using KF.Metrics;

namespace EventProcessor.Workers;

/// <summary>
/// Diagnostic batch processor that does zero work.
/// Use this to measure the maximum Kafka consumption throughput without any
/// pipeline overhead. Compare against staged pipeline builds to locate bottlenecks.
/// </summary>
internal sealed class NullBatchProcessor : IKafkaBatchProcessor
{
    private readonly IOperationMonitor _monitor;

    public NullBatchProcessor(IOperationMonitor monitor) => _monitor = monitor;

    public Task ProcessAsync(KafkaRecordBatch batch, CancellationToken cancellationToken)
    {
        // Fire one scope per MESSAGE (not per batch) so kafka.consumer.pipeline.batch.total
        // accumulates individual message counts and the benchmark can compare it directly
        // against the topic fill count without needing to know the batch size.
        for (int i = 0; i < batch.Count; i++)
        {
            using (_monitor.Begin("kafka.consumer.pipeline.batch")) { }
        }
        return Task.CompletedTask;
    }
}
