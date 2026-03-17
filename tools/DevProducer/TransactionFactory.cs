namespace DevProducer;

internal static class TransactionFactory
{
    private static readonly string[] Nids = Enumerable.Range(1, 200)
        .Select(i => $"NID{i:D6}").ToArray();

    private static readonly string[] Merchants  = ["M001", "M002", "M003", "M004", "M005", "M006"];
    private static readonly string[] Categories = ["grocery", "travel", "restaurant", "fuel", "online", "atm"];
    private static readonly string[] Channels   = ["pos", "online", "mobile", "atm"];
    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "ZAR"];
    private static readonly string[] Countries  = ["US", "GB", "DE", "ZA", "FR", "AE"];

    public static TransactionEvent Generate(Random rng)
    {
        bool highValue = rng.NextDouble() < 0.05;
        bool foreign   = rng.NextDouble() < 0.10;

        return new TransactionEvent
        {
            TransactionId    = Guid.NewGuid().ToString("N"),
            NID              = Nids[rng.Next(Nids.Length)],
            Timestamp        = DateTimeOffset.UtcNow,
            Amount           = highValue
                                   ? Math.Round((decimal)(rng.NextDouble() * 9000 + 1000), 2)
                                   : Math.Round((decimal)(rng.NextDouble() * 500 + 1), 2),
            Currency         = foreign ? Currencies[rng.Next(1, Currencies.Length)] : "USD",
            TransactionType  = rng.Next(5) == 0 ? "withdrawal" : "purchase",
            MerchantId       = Merchants[rng.Next(Merchants.Length)],
            MerchantCategory = Categories[rng.Next(Categories.Length)],
            CountryCode      = foreign ? Countries[rng.Next(1, Countries.Length)] : "US",
            Channel          = Channels[rng.Next(Channels.Length)],
            DeviceId         = $"DEV-{rng.Next(1000, 9999)}",
            IpAddress        = $"{rng.Next(1, 254)}.{rng.Next(0, 255)}.{rng.Next(0, 255)}.{rng.Next(1, 254)}",
            SessionId        = $"SES-{rng.Next(100000, 999999)}",
        };
    }
}
