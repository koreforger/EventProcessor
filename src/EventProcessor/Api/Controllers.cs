using EventProcessor.Monitoring;
using EventProcessor.Session;
using Event.Streaming.Processing.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace EventProcessor.Api;

[ApiController]
[Route("api/monitoring")]
public sealed class MonitoringController : ControllerBase
{
    private readonly EventProcessorMonitoringSnapshot _snapshot;
    private readonly IIncidentStore _incidents;

    public MonitoringController(EventProcessorMonitoringSnapshot snapshot, IIncidentStore incidents)
    {
        _snapshot = snapshot;
        _incidents = incidents;
    }

    [HttpGet("snapshot")]
    public IActionResult GetSnapshot() => Ok(_snapshot);

    [HttpGet("incidents")]
    public IActionResult GetIncidents([FromQuery] int count = 50) =>
        Ok(_incidents.GetRecent(count));

    [HttpGet("incidents/unresolved")]
    public IActionResult GetUnresolved() =>
        Ok(_incidents.GetUnresolved());
}

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeController : ControllerBase
{
    private readonly FraudSessionStore _sessions;

    public RuntimeController(FraudSessionStore sessions) => _sessions = sessions;

    [HttpGet("state")]
    public IActionResult GetState() => Ok(new
    {
        Application = "EventProcessor",
        Environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
        MachineName = System.Environment.MachineName,
        ProcessId = System.Environment.ProcessId,
        StartedAt = DateTimeOffset.UtcNow,
        ActiveSessions = _sessions.ActiveCount,
    });

    [HttpGet("sessions")]
    public IActionResult GetSessions() => Ok(_sessions.Snapshot());
}

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IConfiguration _config;

    public SettingsController(IConfiguration config) => _config = config;

    [HttpGet]
    public IActionResult GetSettings() => Ok(new
    {
        Application = "EventProcessor",
        SeekMode = _config["EventProcessor:Seek:Mode"] ?? "None",
    });
}
