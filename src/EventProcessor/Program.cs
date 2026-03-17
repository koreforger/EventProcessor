using EventProcessor;
using EventProcessor.Configuration;
using EventProcessor.Hubs;
using EventProcessor.Logging;
using EventProcessor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add SQL-based settings from database (loads from KoreForge.Settings schema)
builder.Configuration.AddSqlSettings();

// KoreForge structured logging (source-generated from EventProcessorLogEvents enum)
builder.Services.AddGeneratedLogging();

// Configure services
builder.Services.AddKafkaProcessor(builder.Configuration);

// Dashboard services (SignalR, metrics, settings monitor)
builder.Services.AddDashboard();

var app = builder.Build();

// CORS for Vite dev server
app.UseCors("Dashboard");

// Configure the HTTP pipeline
app.MapHealthChecks("/health");
app.MapGet("/", () => "EventProcessor is running");

// SignalR hubs
app.MapHub<MetricsHub>("/hub/metrics");
app.MapHub<SettingsHub>("/hub/settings");

// REST API for initial page loads
app.MapGet("/api/settings", (SettingsMonitor monitor) => monitor.GetSettingsSnapshot());
app.MapGet("/api/metrics", (MetricsCollector collector) => collector.GetSnapshot());

// Start the application
app.Run();
