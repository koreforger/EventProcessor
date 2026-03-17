namespace DevProducer;

internal sealed class ProducerArgs
{
    public string Broker { get; private init; } = "localhost:29092";
    public string Topic  { get; private init; } = "fraud.transactions";
    public int    Rate   { get; private init; } = 10;
    public int    Burst  { get; private init; } = 0;
    public int    Bulk   { get; private init; } = 0;

    public static ProducerArgs Parse(string[] args) => new()
    {
        Broker = Get(args, "--broker", "localhost:29092"),
        Topic  = Get(args, "--topic",  "fraud.transactions"),
        Rate   = int.Parse(Get(args,   "--rate",  "10")),
        Burst  = int.Parse(Get(args,   "--burst", "0")),
        Bulk   = int.Parse(Get(args,   "--bulk",  "0")),
    };

    private static string Get(string[] args, string flag, string fallback)
    {
        int i = Array.IndexOf(args, flag);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : fallback;
    }
}
