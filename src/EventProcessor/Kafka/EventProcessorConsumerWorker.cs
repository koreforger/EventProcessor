using EventProcessor.Logging;
using KF.Kafka.Configuration.Factory;
using KF.Kafka.Consumer.Hosting;
using KF.Metrics;

namespace EventProcessor.Kafka;

public sealed class EventProcessorConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaClientConfigFactory _configFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOperationMonitor _monitor;
    private IServiceScope? _scope;
    private EventProcessorLogger<EventProcessorConsumerWorker>? _log;
    private KafkaConsumerHost? _host;

    public EventProcessorConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IKafkaClientConfigFactory configFactory,
        ILoggerFactory loggerFactory,
        IOperationMonitor monitor)
    {
        _scopeFactory = scopeFactory;
        _configFactory = configFactory;
        _loggerFactory = loggerFactory;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _scope = _scopeFactory.CreateScope();
        var processor = _scope.ServiceProvider.GetRequiredService<EventProcessorKafkaBatchProcessor>();
        _log = _scope.ServiceProvider.GetRequiredService<EventProcessorLogger<EventProcessorConsumerWorker>>();

        _log.Kafka.Consumer.Starting.LogInformation("Building KafkaConsumerHost for EventProcessor profile 'Default'");

        _host = KafkaConsumerHost.Create()
            .UseKafkaConfigurationProfile("Default", _configFactory)
            .UseOperationMonitor(_monitor)
            .UseLoggerFactory(_loggerFactory)
            .UseProcessor(() => processor)
            .Build();

        await _host.StartAsync(stoppingToken);
        _log.Kafka.Consumer.Started.LogInformation("EventProcessor Kafka consumer host started");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log?.Kafka.Consumer.Stopping.LogInformation("Stopping EventProcessor Kafka consumer host");

        if (_host is not null)
        {
            await _host.StopAsync(cancellationToken);
            await _host.DisposeAsync();
        }

        _scope?.Dispose();
        _log?.Kafka.Consumer.Stopped.LogInformation("EventProcessor Kafka consumer host stopped");
        await base.StopAsync(cancellationToken);
    }
}