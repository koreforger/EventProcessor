using EventProcessor.Seek;
using Event.Streaming.In.Seek;
using Microsoft.Extensions.Configuration;

namespace EventProcessor.Tests;

public sealed class SqlConsumerSeekOptionsProviderTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void GetOptions_WhenModeIsNone_ReturnsModeNone()
    {
        var config = BuildConfig(new() { ["EventProcessor:Seek:Mode"] = "None" });
        var provider = new SqlConsumerSeekOptionsProvider(config);
        var opts = provider.GetOptions();
        Assert.Equal(SeekMode.None, opts.Mode);
    }

    [Fact]
    public void GetOptions_WhenModeAbsent_DefaultsToNone()
    {
        var config = BuildConfig(new());
        var provider = new SqlConsumerSeekOptionsProvider(config);
        var opts = provider.GetOptions();
        Assert.Equal(SeekMode.None, opts.Mode);
    }

    [Fact]
    public void GetOptions_WhenModeIsFromOffset_ReturnsFromOffset()
    {
        var config = BuildConfig(new()
        {
            ["EventProcessor:Seek:Mode"] = "FromOffset",
            ["EventProcessor:Seek:StartOffsetOrTimestamp"] = "42"
        });
        var provider = new SqlConsumerSeekOptionsProvider(config);
        var opts = provider.GetOptions();
        Assert.Equal(SeekMode.FromOffset, opts.Mode);
        Assert.Equal("42", opts.StartOffsetOrTimestamp);
    }

    [Fact]
    public void GetOptions_WhenModeIsRange_SetsStopValue()
    {
        var config = BuildConfig(new()
        {
            ["EventProcessor:Seek:Mode"] = "Range",
            ["EventProcessor:Seek:StartOffsetOrTimestamp"] = "100",
            ["EventProcessor:Seek:StopOffsetOrTimestamp"] = "200"
        });
        var provider = new SqlConsumerSeekOptionsProvider(config);
        var opts = provider.GetOptions();
        Assert.Equal(SeekMode.Range, opts.Mode);
        Assert.Equal("100", opts.StartOffsetOrTimestamp);
        Assert.Equal("200", opts.StopOffsetOrTimestamp);
    }

    [Fact]
    public void GetOptions_WhenModeIsInvalid_DefaultsToNone()
    {
        var config = BuildConfig(new() { ["EventProcessor:Seek:Mode"] = "BOGUS_VALUE" });
        var provider = new SqlConsumerSeekOptionsProvider(config);
        var opts = provider.GetOptions();
        Assert.Equal(SeekMode.None, opts.Mode);
    }
}
