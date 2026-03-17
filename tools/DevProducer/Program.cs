/// DevProducer — synthetic Kafka producer for local development and benchmarking.
///
/// Usage:
///   dotnet run                          -- stream at 10 msg/s to localhost:29092
///   dotnet run -- --rate 50             -- stream at 50 msg/s
///   dotnet run -- --broker my:9092      -- custom broker
///   dotnet run -- --rate 5 --burst 20   -- 5 msg/s with occasional 20-msg bursts
///   dotnet run -- --bulk 100000         -- dump 100 000 msgs as fast as possible, then exit
using DevProducer;

var options = ProducerArgs.Parse(Environment.GetCommandLineArgs());

if (options.Bulk > 0)
    BulkProducer.Run(options);
else
    await StreamingProducer.RunAsync(options);
