using EventProcessor.Hubs;
using EventProcessor.Kafka;
using EventProcessor.Logging;
using EventProcessor.Monitoring;
using EventProcessor.Rules;
using EventProcessor.Session;
using KF.Kafka.Configuration.Extensions;
using KF.Metrics;
using KF.Metrics.AspNet;
using KF.Settings.Extensions;
using Event.Streaming.Processing.Monitoring;
using KF.Web.HealthChecks;
using KoreForge.AppLifecycle;

var builder = WebApplication.CreateBuilder(args);

// -- Settings (SQL-backed, live-reload) --
builder.Configuration.AddKFSettings(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("KFSettings")!;
    opts.ApplicationId = builder.Configuration["KFSettings:ApplicationId"] ?? "EventProcessor";
    opts.InstanceId = builder.Configuration["KFSettings:InstanceId"] ?? "1";
    opts.PollingInterval = TimeSpan.FromMinutes(1);
});
builder.Services.AddKFSettingsServices(builder.Configuration);

// -- Application lifecycle --
builder.Services.AddApplicationLifecycleManager(_ => { });

// -- Logging --
builder.Services.AddGeneratedLogging();

// -- Kafka configuration --
builder.Services.AddKafkaConfiguration(builder.Configuration);

// -- Clock --
builder.Services.AddSingleton<KF.Time.ISystemClock>(_ => KF.Time.UtcSystemClock.Instance);

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
