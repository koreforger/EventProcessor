using EventProcessor.Services;
using EventProcessor.Workers.Pipeline;
using FluentAssertions;
using KoreForge.Processing.Pipelines;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Tests.Pipeline;

public sealed class JexExtractStepTests
{
    private readonly JexFieldExtractorService _extractor;
    private readonly JexExtractStep _step;

    public JexExtractStepTests()
    {
        var compiler = new KoreForge.Jex.Jex();
        _extractor = new JexFieldExtractorService(TestLogHelper.CreateLog<JexFieldExtractorService>(), compiler);
        _step = new JexExtractStep(_extractor);
    }

    [Fact]
    public async Task Valid_json_extracts_fields()
    {
        var json = """{"nid":"ABC123","amount":500,"countryCode":"US"}""";
        var outcome = await _step.InvokeAsync(json, new PipelineContext(), default);

        outcome.Kind.Should().Be(StepOutcomeKind.Continue);
        outcome.Value.Should().BeOfType<JObject>();
        outcome.Value.Value<string>("nid").Should().Be("ABC123");
    }

    [Fact]
    public async Task Null_extraction_aborts()
    {
        // Empty string should produce null extraction
        var outcome = await _step.InvokeAsync("", new PipelineContext(), default);

        outcome.Kind.Should().Be(StepOutcomeKind.Abort);
    }
}
