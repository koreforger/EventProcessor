using EventProcessor.HealthChecks;
using EventProcessor.Logging;
using EventProcessor.Models;
using EventProcessor.Services;
using EventProcessor.Workers.Pipeline;
using KF.Kafka.Configuration.Factory;
using KF.Kafka.Consumer.Hosting;
using KF.Kafka.Consumer.Pipelines;
using KF.Metrics;
using KoreForge.Processing.Pipelines;
using Newtonsoft.Json.Linq;
using KorePipeline = KoreForge.Processing.Pipelines.Pipeline;

namespace EventProcessor.Workers;

/// <summary>
/// Background worker that hosts a KoreForge KafkaConsumerHost for transaction processing.
/// </summary>
/// <remarks>
/// DIAGNOSTIC: Change <see cref="DiagnosticStage"/> to isolate pipeline bottlenecks.
/// 0 = null  — pure Kafka read, no pipeline overhead at all
/// 1 = DecodeBytesStep only
/// 2 = DecodeBytesStep + JexExtractStep
/// 3 = full pipeline (Decode + JEX + FraudEval)  &lt;-- normal production
/// After each change: rebuild, restart the API, run catchup-bench.ps1.
/// </remarks>
public sealed class TransactionConsumerWorker : BackgroundService
{
    // ── Diagnostic switch ──────────────────────────────────────────────────────
    private const int DiagnosticStage = 0;
    // ──────────────────────────────────────────────────────────────────────────

    private readonly EventProcessorLog<TransactionConsumerWorker> _log;
    private readonly IKafkaClientConfigFactory _configFactory;
    private readonly JexFieldExtractorService _extractor;
    private readonly IFraudRuleEngine _ruleEngine;
    private readonly ISessionStore _sessionStore;
    private readonly IFraudDecisionProducer _producer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly KafkaConsumerState _consumerState;
    private readonly IOperationMonitor _monitor;
    private KafkaConsumerHost? _host;

    public TransactionConsumerWorker(
        EventProcessorLog<TransactionConsumerWorker> log,
        IKafkaClientConfigFactory configFactory,
        JexFieldExtractorService extractor,
        IFraudRuleEngine ruleEngine,
        ISessionStore sessionStore,
        IFraudDecisionProducer producer,
        ILoggerFactory loggerFactory,
        KafkaConsumerState consumerState,
        IOperationMonitor monitor)
    {
        _log = log;
        _configFactory = configFactory;
        _extractor = extractor;
        _ruleEngine = ruleEngine;
        _sessionStore = sessionStore;
        _producer = producer;
        _loggerFactory = loggerFactory;
        _consumerState = consumerState;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.Kafka.Consumer.Starting.LogInformation(
            "Building KafkaConsumerHost from profile 'Default' [DiagnosticStage={Stage}]", DiagnosticStage);

        _host = BuildHost();

        _log.Kafka.Consumer.Started.LogInformation(
            "KafkaConsumerHost built [stage={Stage}], starting consumption loop", DiagnosticStage);

        try
        {
            await _host.StartAsync(stoppingToken);
            _consumerState.ReportRunning();
        }
        catch (Exception ex)
        {
            _consumerState.ReportFaulted(ex.Message);
            _log.Kafka.Consumer.Error.LogError(ex, "Kafka consumer host failed to start");
            throw;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // ── BuildHost ─────────────────────────────────────────────────────────────

    private KafkaConsumerHost BuildHost()
    {
        var base_ = KafkaConsumerHost.Create()
            .UseKafkaConfigurationProfile("Default", _configFactory)
            .UseOperationMonitor(_monitor)
            .UseLoggerFactory(_loggerFactory);

        // Stage 0: null — no pipeline, no processing; measures raw Kafka throughput.
#pragma warning disable CS0162 // Unreachable code — DiagnosticStage is a compile-time const switch
        if (DiagnosticStage == 0)
        {
            return base_
                .UseProcessor(() => new NullBatchProcessor(_monitor))
                .Build();
        }

        // Stage 1: UTF-8 decode only.
        if (DiagnosticStage == 1)
        {
            return base_
                .UseProcessingPipeline<string>(() =>
                    KorePipeline.Start<KafkaPipelineRecord>()
                        .UseStep(new DecodeBytesStep())
                        .Build())
                .Build();
        }

        // Stage 2: Decode + JEX field extraction.
        if (DiagnosticStage == 2)
        {
            return base_
                .UseProcessingPipeline<JObject>(() =>
                    KorePipeline.Start<KafkaPipelineRecord>()
                        .UseStep(new DecodeBytesStep())
                        .UseStep(new JexExtractStep(_extractor))
                        .Build())
                .Build();
        }

        // Stage 3 (production): full pipeline — Decode + JEX + FraudEval.
        var fraudEvalLogger = _loggerFactory.CreateLogger<FraudEvalStep>();
        return base_
            .UseProcessingPipeline<FraudDecision>(() =>
                KorePipeline.Start<KafkaPipelineRecord>()
                    .UseStep(new DecodeBytesStep())
                    .UseStep(new JexExtractStep(_extractor))
                    .UseStep(new FraudEvalStep(_ruleEngine, _sessionStore, _producer, fraudEvalLogger))
                    .Build())
            .Build();
#pragma warning restore CS0162
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.Kafka.Consumer.Stopping.LogInformation("Stopping consumer host");

        if (_host != null)
        {
            await _host.StopAsync(cancellationToken);
            await _host.DisposeAsync();
        }

        _consumerState.ReportStopped();
        _log.Kafka.Consumer.Stopped.LogInformation("Consumer host stopped");
        await base.StopAsync(cancellationToken);
    }
}
