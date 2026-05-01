using Event.Streaming.In.Seek;
using Microsoft.Extensions.Configuration;

namespace EventProcessor.Seek;

/// <summary>
/// Reads seek configuration from IConfiguration (KFSettings SQL-backed provider).
/// Setting keys: EventProcessor:Seek:Mode, EventProcessor:Seek:StartOffsetOrTimestamp, EventProcessor:Seek:StopOffsetOrTimestamp
/// </summary>
public sealed class SqlConsumerSeekOptionsProvider : IConsumerSeekOptionsProvider
{
    private readonly IConfiguration _config;

    public SqlConsumerSeekOptionsProvider(IConfiguration config) => _config = config;

    public ConsumerSeekOptions GetOptions()
    {
        var modeStr = _config["EventProcessor:Seek:Mode"] ?? "None";
        if (!Enum.TryParse<SeekMode>(modeStr, ignoreCase: true, out var mode))
            mode = SeekMode.None;

        return new ConsumerSeekOptions
        {
            Mode = mode,
            StartOffsetOrTimestamp = _config["EventProcessor:Seek:StartOffsetOrTimestamp"],
            StopOffsetOrTimestamp = _config["EventProcessor:Seek:StopOffsetOrTimestamp"],
        };
    }
}
