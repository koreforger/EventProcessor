using EventProcessor.Logging;
using KF.Kafka.Configuration.Factory;
using KF.Kafka.Consumer.Hosting;

namespace EventProcessor.Workers;

/// <summary>
/// Background worker that hosts a KoreForge KafkaConsumerHost for transaction processing.
/// Uses IKafkaClientConfigFactory to build consumer config from the "Default" Kafka profile,
/// and delegates per-batch work to TransactionBatchProcessor.
/// </summary>
public sealed class TransactionConsumerWorker : BackgroundService
{
    private readonly EventProcessorLog<TransactionConsumerWorker> _log;
    private readonly IKafkaClientConfigFactory _configFactory;
    private readonly TransactionBatchProcessor _processor;
    private readonly ILoggerFactory _loggerFactory;
    private KafkaConsumerHost? _host;

    public TransactionConsumerWorker(
        EventProcessorLog<TransactionConsumerWorker> log,
        IKafkaClientConfigFactory configFactory,
        TransactionBatchProcessor processor,
        ILoggerFactory loggerFactory)
    {
        _log = log;
        _configFactory = configFactory;
        _processor = processor;
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.Kafka.Consumer.Starting.LogInformation("Building KafkaConsumerHost from profile 'Default'");

        _host = KafkaConsumerHost.Create()
            .UseKafkaConfigurationProfile("Default", _configFactory)
            .UseProcessor(() => _processor)
            .UseLoggerFactory(_loggerFactory)
            .Build();

        _log.Kafka.Consumer.Started.LogInformation("KafkaConsumerHost built, starting consumption loop");

        await _host.StartAsync(stoppingToken);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.Kafka.Consumer.Stopping.LogInformation(
            "Stopping consumer host. Messages={Messages}, Errors={Errors}",
            _processor.MessageCount, _processor.ErrorCount);

        if (_host != null)
        {
            await _host.StopAsync(cancellationToken);
            await _host.DisposeAsync();
        }

        _log.Kafka.Consumer.Stopped.LogInformation("Consumer host stopped");
        await base.StopAsync(cancellationToken);
    }
}
