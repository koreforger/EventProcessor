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

// ── JexFieldExtractorService direct tests ─────────────────────────────────

public sealed class JexFieldExtractorServiceTests
{
    private readonly JexFieldExtractorService _extractor;

    public JexFieldExtractorServiceTests()
    {
        var compiler = new KoreForge.Jex.Jex();
        _extractor = new JexFieldExtractorService(TestLogHelper.CreateLog<JexFieldExtractorService>(), compiler);
    }

    [Fact]
    public void Extract_maps_standard_field_names()
    {
        var result = _extractor.Extract("""{"nid":"12345","amount":500,"countryCode":"NO"}""");

        result.Should().NotBeNull();
        result!.Value<string>("nid").Should().Be("12345");
        result.Value<decimal>("amount").Should().Be(500);
        result.Value<string>("countryCode").Should().Be("NO");
    }

    [Fact]
    public void Extract_maps_alternative_field_name_NID_uppercase()
    {
        var result = _extractor.Extract("""{"NID":"12345","amount":100}""");

        result.Should().NotBeNull();
        result!.Value<string>("nid").Should().Be("12345");
    }

    [Fact]
    public void Extract_maps_alternative_field_name_customerId()
    {
        var result = _extractor.Extract("""{"customerId":"12345","amount":100}""");

        result.Should().NotBeNull();
        result!.Value<string>("nid").Should().Be("12345");
    }

    [Fact]
    public void Extract_maps_alternative_field_name_customer_id()
    {
        var result = _extractor.Extract("""{"customer_id":"12345","amount":100}""");

        result.Should().NotBeNull();
        result!.Value<string>("nid").Should().Be("12345");
    }

    [Fact]
    public void Extract_maps_alternative_field_name_transaction_id()
    {
        var result = _extractor.Extract("""{"nid":"X","transaction_id":"TX-99"}""");

        result.Should().NotBeNull();
        result!.Value<string>("transactionId").Should().Be("TX-99");
    }

    [Fact]
    public void Extract_valid_json_missing_nid_returns_object_with_null_nid()
    {
        var result = _extractor.Extract("""{"amount":500,"countryCode":"NO"}""");

        result.Should().NotBeNull();
        result!.Value<string>("nid").Should().BeNull();
    }

    [Fact]
    public void Extract_malformed_json_returns_null()
    {
        var result = _extractor.Extract("{not valid json}}}");

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_maps_alternative_amount_field()
    {
        var result = _extractor.Extract("""{"nid":"X","transactionAmount":750}""");

        result.Should().NotBeNull();
        result!.Value<decimal>("amount").Should().Be(750);
    }

    [Fact]
    public void Extract_maps_alternative_country_field()
    {
        var result = _extractor.Extract("""{"nid":"X","country":"SE"}""");

        result.Should().NotBeNull();
        result!.Value<string>("countryCode").Should().Be("SE");
    }
}
