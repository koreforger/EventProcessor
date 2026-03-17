using EventProcessor;
using EventProcessor.Configuration;
using EventProcessor.HealthChecks;
using EventProcessor.Hubs;
using EventProcessor.Logging;
using EventProcessor.Services;
using KF.Metrics.AspNet;
using KF.Web.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add SQL-based settings from database (loads from KoreForge.Settings schema)
builder.Configuration.AddSqlSettings();

// KoreForge structured logging (source-generated from EventProcessorLogEvents enum)
builder.Services.AddGeneratedLogging();

// Configure services
builder.Services.AddKafkaProcessor(builder.Configuration);

// Dashboard services (SignalR, metrics, settings monitor)
builder.Services.AddDashboard();

// Wire ILoggerFactory into the SQL settings config provider once the host starts
builder.Services.AddSqlSettingsServices(builder.Configuration);

var app = builder.Build();

// CORS for Vite dev server
app.UseCors("Dashboard");

// Configure the HTTP pipeline
// /health        — all registered checks (full ops visibility)
// /health/ready  — "ready" tagged checks (Kubernetes readiness probe)
// /health/live   — "live" tagged checks (Kubernetes liveness probe)
app.MapKfHealthEndpoints();
app.MapGet("/", () => "EventProcessor is running");

// KoreForge metrics snapshot endpoint (/monitoring/snapshot)
app.MapMonitoringEndpoints();

// SignalR hubs
app.MapHub<MetricsHub>("/hub/metrics");
app.MapHub<SettingsHub>("/hub/settings");

// REST API for initial page loads
app.MapGet("/api/settings", (SettingsMonitor monitor) => monitor.GetSettingsSnapshot());
app.MapGet("/api/metrics", (MetricsCollector collector) => collector.GetSnapshot());

// Start the application
app.Run();
