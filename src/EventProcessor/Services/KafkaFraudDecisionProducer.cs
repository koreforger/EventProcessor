using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using EventProcessor.Logging;
using EventProcessor.Models;
using Microsoft.Extensions.Options;

namespace EventProcessor.Services;

/// <summary>
/// Publishes fraud decisions to a Kafka topic as JSON using Confluent.Kafka.
/// Registered when <see cref="ProducerOptions.Enabled"/> is <c>true</c>.
/// </summary>
internal sealed class KafkaFraudDecisionProducer : IFraudDecisionProducer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly EventProcessorLog<KafkaFraudDecisionProducer> _log;

    public KafkaFraudDecisionProducer(
        IOptions<FraudEngineOptions> options,
        EventProcessorLog<KafkaFraudDecisionProducer> log)
    {
        _log = log;
        var producerOpts = options.Value.Kafka.Producer;
        _topic = producerOpts.Topic;

        var config = new ProducerConfig
        {
            BootstrapServers = producerOpts.BootstrapServers,
            Acks = Enum.Parse<Acks>(producerOpts.Acks, ignoreCase: true),
        };

        _producer = new ProducerBuilder<string, string>(config).Build();

        _log.Kafka.Producer.Sent.LogInformation(
            "Kafka decision producer initialized — topic={Topic}, servers={Servers}",
            _topic, producerOpts.BootstrapServers);
    }

    public async Task ProduceAsync(string nid, FraudDecision decision, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(decision, s_jsonOptions);
        try
        {
            await _producer.ProduceAsync(
                _topic,
                new Message<string, string> { Key = nid, Value = value },
                ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _log.Kafka.Producer.Error.LogError(ex,
                "Failed to produce decision for NID {NID} to {Topic}", nid, _topic);
            throw;
        }
    }

    public void Dispose() => _producer.Dispose();
}
