using System.Diagnostics;
using Confluent.Kafka;
using Newtonsoft.Json;

namespace DevProducer;

internal static class BulkProducer
{
    public static void Run(ProducerArgs args)
    {
        Console.WriteLine($"DevProducer [BULK] -> {args.Broker}  topic={args.Topic}  count={args.Bulk:N0}");
        Console.WriteLine("Producing as fast as possible -- no rate limiting.\n");

        var config = new ProducerConfig
        {
            BootstrapServers          = args.Broker,
            Acks                      = Acks.Leader,
            LingerMs                  = 5,
            BatchSize                 = 1_000_000,
            MessageTimeoutMs          = 30_000,
            QueueBufferingMaxMessages = 2_000_000,
        };

        using var producer = new ProducerBuilder<string, string>(config).Build();

        var  rng    = new Random();
        var  sw     = Stopwatch.StartNew();
        var  cts    = new CancellationTokenSource();
        long sent   = 0;
        long errors = 0;

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        for (int i = 0; i < args.Bulk && !cts.IsCancellationRequested; i++)
        {
            var msg  = TransactionFactory.Generate(rng);
            var json = JsonConvert.SerializeObject(msg);

            producer.Produce(args.Topic,
                new Message<string, string> { Key = msg.NID, Value = json },
                report =>
                {
                    if (report.Error.IsError) Interlocked.Increment(ref errors);
                    else                      Interlocked.Increment(ref sent);
                });

            if ((i + 1) % 10_000 == 0)
            {
                producer.Flush(TimeSpan.FromSeconds(10));
                double elapsed = sw.Elapsed.TotalSeconds;
                Console.WriteLine($"  [{i + 1,7:N0} / {args.Bulk:N0}]  confirmed={sent:N0}  errors={errors}  " +
                                  $"rate={sent / elapsed:N0} msg/s  elapsed={elapsed:F1}s");
            }
        }

        Console.Write("\nFlushing...");
        producer.Flush(TimeSpan.FromSeconds(30));
        sw.Stop();

        double total = sw.Elapsed.TotalSeconds;
        Console.WriteLine(" done.\n");
        Console.WriteLine("=== BULK COMPLETE ===");
        Console.WriteLine($"  Produced : {sent:N0}");
        Console.WriteLine($"  Errors   : {errors}");
        Console.WriteLine($"  Time     : {total:F2}s");
        Console.WriteLine($"  Rate     : {sent / total:N0} msg/s");
    }
}
