using EventProcessor.Configuration;
using EventProcessor.HealthChecks;
using EventProcessor.Services;
using EventProcessor.Workers;
using KF.Kafka.Configuration.Extensions;
using KF.Metrics;
using KF.Time;
using KoreForge.Jex;
using Microsoft.Extensions.Options;

namespace EventProcessor;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaProcessor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Health checks — each integration registers its own check via IHealthChecksBuilder
        // extension methods defined in HealthCheckExtensions.cs.
        services.AddHealthChecks()
            .AddKafkaConsumerHealthCheck();

        // KoreForge observability: IOperationMonitor + IMonitoringSnapshotProvider
        services.AddKoreForgeMetrics();

        // Deterministic clock abstraction
        services.AddSingleton<ISystemClock>(UtcSystemClock.Instance);

        // KoreForge Kafka configuration (reads from "Kafka" config section by default)
        services.AddKafkaConfiguration(configuration);

        // KoreForge JEX compiler
        services.AddSingleton<IJexCompiler>(_ => new Jex());

        // JEX-based field extractor
        services.AddSingleton<JexFieldExtractorService>();

        // SQL-backed session repository (slab storage)
        services.AddSingleton<ISessionRepository, SqlSessionRepository>();

        // FASTER-backed session store (durable, cache-through from SQL)
        services.AddSingleton<ISessionStore, FasterSessionStore>();

        // Fraud decision Kafka producer (interface-based, on/off toggle)
        services.AddSingleton<IFraudDecisionProducer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FraudEngineOptions>>();
            if (options.Value.Kafka.Producer.Enabled)
                return ActivatorUtilities.CreateInstance<KafkaFraudDecisionProducer>(sp);
            return new NoOpFraudDecisionProducer();
        });

        // Kafka consumer hosted service + processing pipeline
        services.AddHostedService<TransactionConsumerWorker>();

        // Flush coordinator — drains dirty sessions to SQL on a timer
        services.AddHostedService<FlushCoordinator>();

        // Fraud rule engine
        services.AddSingleton<IFraudRuleEngine, SimpleFraudRuleEngine>();

        // Bind and validate FraudEngine options at startup
        services.AddOptions<FraudEngineOptions>()
            .BindConfiguration("FraudEngine")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FraudEngineOptions>, FraudEngineOptionsValidator>();

        return services;
    }

    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        // CORS for Vite dev server
        services.AddCors(options =>
        {
            options.AddPolicy("Dashboard", policy =>
            {
                policy.WithOrigins("http://localhost:5173")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        // SignalR
        services.AddSignalR();

        // Metrics collection + broadcasting
        services.AddSingleton<MetricsCollector>();
        services.AddHostedService<MetricsBroadcaster>();

        // Settings monitoring (subscribes to IConfiguration reload tokens)
        services.AddSingleton<SettingsMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<SettingsMonitor>());

        return services;
    }
}
