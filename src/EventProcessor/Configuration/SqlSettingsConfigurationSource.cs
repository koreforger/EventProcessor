using EventProcessor.HealthChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventProcessor.Configuration;

/// <summary>
/// Options for the SQL-backed settings configuration provider.
/// All properties are required — no C# defaults. Missing values cause startup failure.
/// Place defaults for local development in appsettings.Development.json.
/// </summary>
public sealed class SqlSettingsOptions
{
    /// <summary>Connection string to the settings database. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Application identifier used to filter settings rows. Required.</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Instance identifier used for instance-specific overrides. Required.</summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary>
    /// How frequently the provider polls for setting changes at runtime.
    /// Defaults to 30 s — override in appsettings for other environments.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Configuration source that loads settings from SQL Server (KoreForge.Settings schema).
/// </summary>
public sealed class SqlSettingsConfigurationSource : IConfigurationSource
{
    private readonly SqlSettingsOptions _options;

    // Exposed so that AddSqlSettingsServices can wire a real ILoggerFactory into the provider
    // after the DI container is available (the provider starts its polling background task
    // lazily, so it can use a proper logger once the host has started).
    internal SqlSettingsConfigurationProvider? Provider { get; private set; }

    public SqlSettingsConfigurationSource(SqlSettingsOptions options)
    {
        _options = options;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new SqlSettingsConfigurationProvider(_options);
        return Provider;
    }
}

/// <summary>
/// Loads settings from SQL Server at startup and polls for changes at runtime.
/// </summary>
/// <remarks>
/// Bootstrap phase (Load): fails hard on any error — missing connection string, DB
/// connectivity failure, or query failure all terminate startup immediately.
///
/// Runtime refresh phase (poll loop): logs errors via the structured logger attached by
/// the DI-aware SqlSettingsPollingWirer hosted service, and surfaces degraded health
/// via the SqlSettingsHealthCheck.
/// </remarks>
public sealed class SqlSettingsConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly SqlSettingsOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollingTask;
    private bool _disposed;

    // Filled in once the DI container is ready (see SqlSettingsPollingWirer).
    private ILogger? _logger;

    /// <summary>
    /// Runtime health of the polling loop.
    /// Healthy = last poll succeeded; Degraded = last poll failed; Starting = not yet polled.
    /// </summary>
    public SqlSettingsHealthStatus HealthStatus { get; internal set; } = SqlSettingsHealthStatus.Starting;

    public SqlSettingsConfigurationProvider(SqlSettingsOptions options)
    {
        _options = options;
    }

    /// <summary>Called by SqlSettingsPollingWirer once ILoggerFactory is available from DI.</summary>
    public void AttachLogger(ILoggerFactory loggerFactory) =>
        _logger = loggerFactory.CreateLogger<SqlSettingsConfigurationProvider>();

    /// <summary>Returns a snapshot of the currently loaded settings (safe for concurrent reads).</summary>
    public IReadOnlyDictionary<string, string?> GetLoadedData() =>
        new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    public override void Load()
    {
        // FAIL-HARD: any exception propagates and aborts application startup.
        // This enforces the principle that bad configuration must prevent start-up,
        // not be discovered at runtime.
        LoadFromDatabase();
        HealthStatus = SqlSettingsHealthStatus.Healthy;

        // Start the runtime polling loop.
        if (_pollingTask == null && _options.PollingInterval > TimeSpan.Zero)
            _pollingTask = PollForChangesAsync(_cts.Token);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void LoadFromDatabase()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var connection = new SqlConnection(_options.ConnectionString);
        connection.Open();

        // Query matches the KoreForge.Settings schema.
        // Instance-specific rows win over wildcard ('*') rows, which win over global rows.
        const string sql = """
            SELECT [Key], [Value]
            FROM dbo.Settings
            WHERE ApplicationId = @AppId
              AND (InstanceId IS NULL OR InstanceId = @InstanceId OR InstanceId = '*')
            ORDER BY
                CASE WHEN InstanceId IS NULL THEN 0 WHEN InstanceId = '*' THEN 1 ELSE 2 END
            """;

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@AppId", _options.Application);
        cmd.Parameters.AddWithValue("@InstanceId", _options.Instance);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(1) ? null : reader.GetString(1);
            data[key] = value;
        }

        Data = data;
    }

    // ── Runtime polling ───────────────────────────────────────────────────────

    private async Task PollForChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, cancellationToken);

                var oldData = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
                LoadFromDatabase();
                HealthStatus = SqlSettingsHealthStatus.Healthy;

                var hasChanges = Data.Count != oldData.Count ||
                    Data.Any(kv => !oldData.TryGetValue(kv.Key, out var oldVal) || oldVal != kv.Value);

                if (hasChanges)
                {
                    _logger?.LogInformation(
                        "SQL settings changed ({Count} keys loaded) — triggering configuration reload",
                        Data.Count);
                    OnReload();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                HealthStatus = SqlSettingsHealthStatus.Degraded;
                _logger?.LogError(ex,
                    "SQL settings polling failed; configuration not refreshed this cycle. " +
                    "App continues with last known values");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>Runtime health of the SQL settings polling loop.</summary>
public enum SqlSettingsHealthStatus
{
    Starting,
    Healthy,
    Degraded
}

/// <summary>
/// Extension methods for adding SQL settings to the configuration pipeline.
/// </summary>
public static class SqlSettingsExtensions
{
    /// <summary>
    /// Reads bootstrap SQL settings options from the <c>KoreForge:Settings</c> config section,
    /// validates them, and adds the SQL-backed configuration source.
    /// Throws <see cref="InvalidOperationException"/> if any required value is absent.
    /// </summary>
    public static IConfigurationBuilder AddSqlSettings(
        this IConfigurationBuilder builder,
        Action<SqlSettingsOptions>? configure = null)
    {
        var tempConfig = builder.Build();

        // All three fields are REQUIRED. Missing values must abort startup.
        var connectionString = tempConfig["KoreForge:Settings:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "KoreForge:Settings:ConnectionString is required but was not found in configuration. " +
                "Add it to appsettings.json or set the environment variable " +
                "KoreForge__Settings__ConnectionString.");

        var application = tempConfig["KoreForge:Settings:Application"];
        if (string.IsNullOrWhiteSpace(application))
            throw new InvalidOperationException(
                "KoreForge:Settings:Application is required but was not found in configuration.");

        var instance = tempConfig["KoreForge:Settings:Instance"];
        if (string.IsNullOrWhiteSpace(instance))
            throw new InvalidOperationException(
                "KoreForge:Settings:Instance is required but was not found in configuration.");

        var options = new SqlSettingsOptions
        {
            ConnectionString = connectionString,
            Application = application,
            Instance = instance
        };

        // Allow callers to override PollingInterval or other non-required fields.
        configure?.Invoke(options);

        if (options.PollingInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                "KoreForge:Settings:PollingInterval must be a positive duration.");

        builder.Add(new SqlSettingsConfigurationSource(options));
        return builder;
    }

    /// <summary>
    /// Registers DI services needed by the SQL settings infrastructure:
    /// <list type="bullet">
    /// <item><see cref="SqlSettingsOptions"/> singleton (read from IConfiguration)</item>
    /// <item><see cref="SqlSettingsPollingWirer"/> hosted service that attaches the logger</item>
    /// </list>
    /// Call this from <c>ServiceCollectionExtensions</c> after <c>AddSqlSettings</c>.
    /// </summary>
    public static IServiceCollection AddSqlSettingsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind SqlSettingsOptions from IConfiguration so SettingsMonitor can inject them.
        services.AddSingleton(_ =>
        {
            var section = configuration.GetSection("KoreForge:Settings");
            var opts = new SqlSettingsOptions();
            section.Bind(opts);
            return opts;
        });

        // Wire the ILoggerFactory into the config provider once the host starts.
        services.AddHostedService<SqlSettingsPollingWirer>();

        // SQL settings polling health check.
        services.AddHealthChecks()
            .AddSqlSettingsHealthCheck();

        return services;
    }
}

/// <summary>
/// Hosted service that attaches a real <see cref="ILoggerFactory"/> to the
/// <see cref="SqlSettingsConfigurationProvider"/> once the DI container is ready.
/// This enables structured logging in the provider's runtime polling loop
/// without coupling the config provider to DI.
/// </summary>
internal sealed class SqlSettingsPollingWirer : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public SqlSettingsPollingWirer(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Locate the provider in the running configuration pipeline and attach the logger.
        var provider = ((IConfigurationRoot)_configuration).Providers
            .OfType<SqlSettingsConfigurationProvider>()
            .SingleOrDefault();

        provider?.AttachLogger(_loggerFactory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}


