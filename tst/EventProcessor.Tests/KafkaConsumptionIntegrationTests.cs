using EventProcessor.Kafka;
using EventProcessor.Logging;
using EventProcessor.Monitoring;
using EventProcessor.Rules;
using EventProcessor.Session;
using EventProcessor.Tests.Infrastructure;
using KF.Kafka.Configuration.Extensions;
using KF.Kafka.Configuration.Factory;
using KF.Kafka.Consumer.Hosting;
using Event.Streaming.Processing.Monitoring;
using KF.Time;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventProcessor.Tests;

[Collection(KafkaClusterCollection.CollectionName)]
public sealed class KafkaConsumptionIntegrationTests
{
    private readonly KafkaTestClusterFixture _fixture;

    public KafkaConsumptionIntegrationTests(KafkaTestClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventProcessor_consumes_real_kafka_messages()
    {
        var topic = $"event-processor-{Guid.NewGuid():N}";
        await _fixture.CreateTopicAsync(topic);

        var payloads = Enumerable.Range(0, 10)
            .Select(i => JsonSerializer.Serialize(new
            {
                entityId = $"acct-{i % 2}",
                amount = 600 + (i * 25),
                country = "GB",
                merchant = new { category = "electronics", name = $"merchant-{i}" },
                signals = new[] { "velocity", "amount" },
            }))
            .ToArray();

        await _fixture.ProduceJsonAsync(topic, payloads);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGeneratedLogging();
        services.AddSingleton<ISystemClock>(_ => UtcSystemClock.Instance);
        services.AddSingleton<FraudSessionStore>();
        services.AddSingleton<RuleEvaluator>(sp =>
        {
            var evaluator = new RuleEvaluator();
            evaluator.RefreshRules([new HighFrequencyRule(3), new HighAmountRule(1000m)]);
            return evaluator;
        });
        services.AddSingleton<EventProcessorMonitoringSnapshot>();
        services.AddSingleton<EventProcessorMetricsAccumulator>();
        services.AddSingleton<IIncidentStore, InMemoryIncidentStore>();
        services.AddScoped<EventProcessorKafkaBatchProcessor>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:Clusters:Local:BootstrapServers"] = _fixture.BootstrapServers,
                ["Kafka:Profiles:Default:Type"] = "Consumer",
                ["Kafka:Profiles:Default:Cluster"] = "Local",
                ["Kafka:Profiles:Default:ExplicitGroupId"] = $"processor-it-{Guid.NewGuid():N}",
                ["Kafka:Profiles:Default:Topics:0"] = topic,
                ["Kafka:Profiles:Default:ConfluentOptions:auto.offset.reset"] = "earliest",
                ["Kafka:Profiles:Default:ConfluentOptions:enable.auto.commit"] = "false",
                ["Kafka:Profiles:Default:ExtendedConsumer:ConsumerCount"] = "1",
                ["Kafka:Profiles:Default:ExtendedConsumer:MaxBatchSize"] = "10",
                ["Kafka:Profiles:Default:ExtendedConsumer:MaxBatchWaitMs"] = "200",
                ["Kafka:Profiles:Default:ExtendedConsumer:StartMode"] = "Earliest",
            })
            .Build();

        services.AddKafkaConfiguration(config);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<EventProcessorKafkaBatchProcessor>();
        var factory = scope.ServiceProvider.GetRequiredService<IKafkaClientConfigFactory>();
        var metrics = scope.ServiceProvider.GetRequiredService<EventProcessorMetricsAccumulator>();
        var snapshot = scope.ServiceProvider.GetRequiredService<EventProcessorMonitoringSnapshot>();
        var sessions = scope.ServiceProvider.GetRequiredService<FraudSessionStore>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        await using var host = KafkaConsumerHost.Create()
            .UseKafkaConfigurationProfile("Default", factory)
            .UseLoggerFactory(loggerFactory)
            .UseProcessor(() => processor)
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await host.StartAsync(cts.Token);
        await WaitUntilAsync(() => metrics.TotalProcessed == payloads.Length, TimeSpan.FromSeconds(30), cts.Token);
        await host.StopAsync(cts.Token);

        Assert.Equal(payloads.Length, metrics.TotalProcessed);
        Assert.Equal(payloads.Length, metrics.TotalDecisions);
        Assert.Equal(2, sessions.ActiveCount);
        Assert.Equal(Event.Streaming.Processing.Monitoring.HealthStatus.Healthy, snapshot.Health);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not satisfied before timeout.");
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }
}