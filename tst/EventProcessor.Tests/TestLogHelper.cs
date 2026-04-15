using EventProcessor.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventProcessor.Tests;

/// <summary>
/// Creates <see cref="EventProcessorLog{T}"/> instances backed by a no-op logger
/// for use in unit tests.
/// </summary>
internal static class TestLogHelper
{
    private static readonly IServiceProvider Provider = BuildProvider();

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddGeneratedLogging();
        return services.BuildServiceProvider();
    }

    public static EventProcessorLog<T> CreateLog<T>() =>
        Provider.GetRequiredService<EventProcessorLog<T>>();
}
