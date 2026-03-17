using EventProcessor.Configuration;
using EventProcessor.Services;
using EventProcessor.Workers;
using KF.Kafka.Configuration.Extensions;
using KoreForge.Jex;

namespace EventProcessor;

/// <summary>
/// Extension methods for configuring EventProcessor services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EventProcessor services to the DI container.
    /// </summary>
    public static IServiceCollection AddKafkaProcessor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Health checks
        services.AddHealthChecks();

        // KoreForge Kafka configuration (reads from "Kafka" config section by default)
        services.AddKafkaConfiguration(configuration);

        // KoreForge JEX compiler
        services.AddSingleton<IJexCompiler>(_ => new Jex());

        // JEX-based field extractor
        services.AddSingleton<JexFieldExtractorService>();

        // Kafka batch processor (processes each batch through JEX extraction)
        services.AddSingleton<TransactionBatchProcessor>();

        // Kafka consumer hosted service (wraps KafkaConsumerHost)
        services.AddHostedService<TransactionConsumerWorker>();

        // Fraud rule engine
        services.AddSingleton<IFraudRuleEngine, SimpleFraudRuleEngine>();

        // Bind FraudEngine options from config
        services.Configure<FraudEngineOptions>(configuration.GetSection("FraudEngine"));

        return services;
    }

    /// <summary>
    /// Adds dashboard services: SignalR, CORS, metrics collection, settings monitoring.
    /// </summary>
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

        // Settings monitoring (SqlSettingsOptions singleton from config bootstrap)
        services.AddSingleton(SqlSettingsOptionsHolder.Instance);
        services.AddSingleton<SettingsMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<SettingsMonitor>());

        return services;
    }
}
