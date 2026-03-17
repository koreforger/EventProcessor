using Confluent.Kafka;
using Newtonsoft.Json;

namespace DevProducer;

internal static class StreamingProducer
{
    public static async Task RunAsync(ProducerArgs args)
    {
        Console.WriteLine($"DevProducer -> {args.Broker}  topic={args.Topic}  rate={args.Rate}/s  burst={args.Burst}");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        var config = new ProducerConfig
        {
            BootstrapServers = args.Broker,
            Acks             = Acks.Leader,
            MessageTimeoutMs = 5000,
        };

        using var producer = new ProducerBuilder<string, string>(config).Build();

        var  rng          = new Random();
        var  cts          = new CancellationTokenSource();
        long sent         = 0;
        long errors       = 0;
        int  intervalMs   = 1000 / Math.Max(1, args.Rate);
        int  burstEvery   = args.Rate * 10;
        int  burstCounter = 0;

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                bool isBurst = args.Burst > 0 && burstCounter++ % burstEvery == 0 && sent > 0;
                int  count   = isBurst ? args.Burst : 1;

                for (int i = 0; i < count; i++)
                {
                    var evt  = TransactionFactory.Generate(rng);
                    var json = JsonConvert.SerializeObject(evt);
                    try
                    {
                        await producer.ProduceAsync(args.Topic,
                            new Message<string, string> { Key = evt.NID, Value = json },
                            cts.Token);
                        sent++;
                    }
                    catch (ProduceException<string, string> ex)
                    {
                        errors++;
                        Console.WriteLine($"[ERROR] {ex.Error.Reason}");
                    }
                }

                if (isBurst)
                    Console.WriteLine($"[BURST] {count} msgs  total={sent}  errors={errors}");

                if (sent % 100 == 0 && sent > 0)
                    Console.WriteLine($"[INFO]  Sent {sent} messages  errors={errors}");

                await Task.Delay(intervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            producer.Flush(TimeSpan.FromSeconds(5));
            Console.WriteLine($"\nStopped. Total sent={sent}  errors={errors}");
        }
    }
}
