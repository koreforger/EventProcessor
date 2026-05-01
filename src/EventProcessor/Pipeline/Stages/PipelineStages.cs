using Event.Streaming.Processing.Envelopes;
using Event.Streaming.Processing.Pipeline;

namespace EventProcessor.Pipeline.Stages;

/// <summary>
/// Stage 1 — Receive: wraps a Kafka record into an OperationalEnvelope.
/// </summary>
public sealed class ReceiveStage : IPipelineStage<byte[], OperationalEnvelope>
{
    private readonly string _sourceTopic;

    public ReceiveStage(string sourceTopic) => _sourceTopic = sourceTopic;

    public string StageName => "Receive";

    public Task<StageExecutionResult<OperationalEnvelope>> ExecuteAsync(byte[] input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var envelope = new OperationalEnvelope
        {
            SourceTopic = _sourceTopic,
            IngestedTimestamp = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(StageExecutionResult<OperationalEnvelope>.Ok(envelope, sw.Elapsed));
    }
}

/// <summary>
/// Stage 2 — Decode: validates the envelope has a usable payload.
/// </summary>
public sealed class DecodeStage : IPipelineStage<OperationalEnvelope, OperationalEnvelope>
{
    public string StageName => "Decode";

    public Task<StageExecutionResult<OperationalEnvelope>> ExecuteAsync(OperationalEnvelope input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (input.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            return Task.FromResult(StageExecutionResult<OperationalEnvelope>.Fail(
                "Decode", "Payload is undefined", sw.Elapsed));
        }
        return Task.FromResult(StageExecutionResult<OperationalEnvelope>.Ok(input, sw.Elapsed));
    }
}

/// <summary>
/// Stage 3 — Classify: tags the envelope with the rule profile name.
/// </summary>
public sealed class ClassifyStage : IPipelineStage<OperationalEnvelope, OperationalEnvelope>
{
    private readonly string _profileName;

    public ClassifyStage(string profileName) => _profileName = profileName;

    public string StageName => "Classify";

    public Task<StageExecutionResult<OperationalEnvelope>> ExecuteAsync(OperationalEnvelope input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(input.ClassificationProfile))
            input.ClassificationProfile = _profileName;
        return Task.FromResult(StageExecutionResult<OperationalEnvelope>.Ok(input, sw.Elapsed));
    }
}

/// <summary>
/// Stage 4 — Enrich: applies tag overrides from the rule profile.
/// </summary>
public sealed class EnrichStage : IPipelineStage<OperationalEnvelope, OperationalEnvelope>
{
    private readonly IReadOnlyDictionary<string, string> _tagOverrides;

    public EnrichStage(IReadOnlyDictionary<string, string> tagOverrides) => _tagOverrides = tagOverrides;

    public string StageName => "Enrich";

    public Task<StageExecutionResult<OperationalEnvelope>> ExecuteAsync(OperationalEnvelope input, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (k, v) in _tagOverrides)
            input.Tags[k] = v;
        return Task.FromResult(StageExecutionResult<OperationalEnvelope>.Ok(input, sw.Elapsed));
    }
}
