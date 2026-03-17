using KF.Time;
using KF.Logging;
using KF.Logging.Serilog;
using KoreForge.Jex;
using KoreForge.AppLifecycle.Options;
using KF.Metrics;
using KoreForge.Processing.Pipelines;
using KF.Settings.Options;
using KF.Kafka.Configuration.Options;
using KF.RestApi.Common.Abstractions.Options;
using KF.RestApi.Common.Observability.Tracing;
using KF.RestApi.Common.Persistence.Options;
using KF.Web.Authorization.Dynamic;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║         KoreForge NuGet Package Verification                ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. KoreForge.Time
var clock = new VirtualSystemClock();
Print("KoreForge.Time", clock);

// 2. KoreForge.Logging
var attr = new LogEventSourceAttribute();
Print("KoreForge.Logging", attr);

// 3. KoreForge.Logging.Serilog
var logStash = new LogStashOptions();
Print("KoreForge.Logging.Serilog", logStash);

// 4. KoreForge.Jex
var jex = new Jex();
Print("KoreForge.Jex", jex);

// 5. KoreForge.AppLifecycle
var lifecycle = new ApplicationLifecycleOptions();
Print("KoreForge.AppLifecycle", lifecycle);

// 6. KoreForge.Metrics
var monitoring = new MonitoringOptions();
Print("KoreForge.Metrics", monitoring);

// 7. KoreForge.Metrics.AspNet — abstract/static only, reference the assembly
var metricsAspNetAssembly = typeof(KF.Metrics.AspNet.MonitoringEndpointRouteBuilderExtensions).Assembly;
Print("KoreForge.Metrics.AspNet", metricsAspNetAssembly);

// 8. KoreForge.Processing
var pipelineCtx = new PipelineContext();
Print("KoreForge.Processing", pipelineCtx);

// 9. KoreForge.Settings
var settings = new KFSettingsOptions();
Print("KoreForge.Settings", settings);

// 10. KoreForge.Kafka
var kafkaOpts = new KafkaConfigurationRootOptions();
Print("KoreForge.Kafka", kafkaOpts);

// 11. KoreForge.Web.RestApi.Abstractions
var externalApi = new ExternalApiOptions();
Print("KoreForge.Web.RestApi.Abstractions", externalApi);

// 12. KoreForge.Web.RestApi.Observability
var tracer = new ActivityTracer();
Print("KoreForge.Web.RestApi.Observability", tracer);

// 13. KoreForge.Web.RestApi.Persistence
var redaction = new AuditRedactionOptions();
Print("KoreForge.Web.RestApi.Persistence", redaction);

// 14. KoreForge.Web.Authorization
var methodKey = new MethodKey("EventProcessor.Controllers.HomeController", "Index");
Print("KoreForge.Web.Authorization", methodKey);

Console.WriteLine();
Console.WriteLine("All 14 KoreForge NuGet packages verified successfully.");

static void Print(string package, object instance)
{
    var type = instance.GetType();
    var assembly = type.Assembly.GetName();
    Console.WriteLine($"  [OK] {package,-42} → {type.FullName} (from {assembly.Name} v{assembly.Version})");
}
