using EventProcessor.Hubs;
using EventProcessor.Kafka;
using EventProcessor.Logging;
using EventProcessor.Monitoring;
using EventProcessor.Rules;
using EventProcessor.Session;
using KoreForge.Kafka.Configuration.Extensions;
using KoreForge.Metrics;
using KoreForge.Metrics.AspNet;
using KoreForge.Settings.Extensions;
using Event.Streaming.Processing.Monitoring;
using KoreForge.Web.HealthChecks;
using KoreForge.AppLifecycle;

var builder = WebApplication.CreateBuilder(args);

// -- Settings (SQL-backed, live-reload) --
builder.Configuration.AddKoreForgeSettings(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("KoreForgeSettings")!;
    opts.ApplicationId = builder.Configuration["KoreForgeSettings:ApplicationId"] ?? "EventProcessor";
    opts.InstanceId = builder.Configuration["KoreForgeSettings:InstanceId"] ?? "1";
    opts.PollingInterval = TimeSpan.FromMinutes(1);
});
builder.Services.AddKoreForgeSettingsServices(builder.Configuration);

// -- Application lifecycle --
builder.Services.AddApplicationLifecycleManager(_ => { });

// -- Logging --
builder.Services.AddGeneratedLogging();

// -- Kafka configuration --
builder.Services.AddKafkaConfiguration(builder.Configuration);

// -- Clock --
builder.Services.AddSingleton<KoreForge.Time.ISystemClock>(_ => KoreForge.Time.UtcSystemClock.Instance);

// -- Session store --
builder.Services.AddSingleton<FraudSessionStore>();

// -- Rules --
builder.Services.AddSingleton<RuleEvaluator>(sp =>
{
    var evaluator = new RuleEvaluator();
    evaluator.RefreshRules([new HighFrequencyRule(), new HighAmountRule()]);
    return evaluator;
});

// -- Monitoring singletons --
builder.Services.AddSingleton<EventProcessorMonitoringSnapshot>();
builder.Services.AddSingleton<EventProcessorMetricsAccumulator>();
builder.Services.AddSingleton<IIncidentStore, InMemoryIncidentStore>();
builder.Services.AddScoped<EventProcessorKafkaBatchProcessor>();
builder.Services.AddHostedService<EventProcessorConsumerWorker>();

// -- Seek --
builder.Services.AddSingleton<EventProcessor.Seek.SeekBoundaryState>();
builder.Services.AddScoped<EventProcessor.Seek.SeekStartupService>();
builder.Services.AddScoped<Event.Streaming.In.Seek.IConsumerSeekOptionsProvider,
                            EventProcessor.Seek.SqlConsumerSeekOptionsProvider>();

// -- Metrics --
builder.Services.AddKoreForgeMetrics();

// -- Health checks --
builder.Services.AddHealthChecks();

// -- Web + SignalR --
builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

app.MapControllers();
app.MapHub<SettingsHub>("/hubs/settings");
app.MapHub<MonitoringHub>("/hubs/monitoring");
app.MapKfHealthEndpoints();
app.MapMonitoringEndpoints();

app.Run();
