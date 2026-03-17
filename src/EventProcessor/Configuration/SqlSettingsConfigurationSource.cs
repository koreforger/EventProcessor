using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EventProcessor.Configuration;

/// <summary>
/// Options for SQL-based settings configuration.
/// </summary>
public sealed class SqlSettingsOptions
{
    /// <summary>
    /// Connection string to the settings database.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Application identifier for filtering settings.
    /// </summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>
    /// Instance identifier for filtering settings.
    /// </summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary>
    /// Polling interval for settings refresh.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Configuration source that loads settings from SQL Server (KoreForge.Settings schema).
/// </summary>
public sealed class SqlSettingsConfigurationSource : IConfigurationSource
{
    private readonly SqlSettingsOptions _options;

    public SqlSettingsConfigurationSource(SqlSettingsOptions options)
    {
        _options = options;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SqlSettingsConfigurationProvider(_options);
    }
}

/// <summary>
/// Configuration provider that loads and reloads settings from SQL Server.
/// </summary>
public sealed class SqlSettingsConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly SqlSettingsOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollingTask;
    private bool _disposed;

    public SqlSettingsConfigurationProvider(SqlSettingsOptions options)
    {
        _options = options;
    }

    public override void Load()
    {
        try
        {
            LoadFromDatabase();

            // Start polling for changes
            if (_pollingTask == null && _options.PollingInterval > TimeSpan.Zero)
            {
                _pollingTask = PollForChangesAsync(_cts.Token);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail - app can start without DB settings
            Console.WriteLine($"[SqlSettings] Failed to load settings: {ex.Message}");
        }
    }

    private void LoadFromDatabase()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return;
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var connection = new SqlConnection(_options.ConnectionString);
        connection.Open();

        // Query matches KoreForge.Settings schema
        const string sql = """
            SELECT [Key], [Value]
            FROM dbo.Settings
            WHERE ApplicationId = @AppId
              AND (InstanceId IS NULL OR InstanceId = @InstanceId OR InstanceId = '*')
            ORDER BY
                CASE WHEN InstanceId IS NULL THEN 0 WHEN InstanceId = '*' THEN 1 ELSE 2 END -- Instance-specific wins
            """;

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@AppId", _options.Application);
        cmd.Parameters.AddWithValue("@InstanceId", _options.Instance);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(1) ? null : reader.GetString(1);
            data[key] = value; // Later rows (instance-specific) overwrite earlier (* wildcard)
        }

        Data = data;
        Console.WriteLine($"[SqlSettings] Loaded {data.Count} settings for {_options.Application}/{_options.Instance}");
    }

    private async Task PollForChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, cancellationToken);

                var oldData = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
                LoadFromDatabase();

                // Check if anything changed
                var hasChanges = Data.Count != oldData.Count ||
                    Data.Any(kv => !oldData.TryGetValue(kv.Key, out var oldVal) || oldVal != kv.Value);

                if (hasChanges)
                {
                    Console.WriteLine($"[SqlSettings] Settings changed, triggering reload");
                    OnReload();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SqlSettings] Polling error: {ex.Message}");
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

/// <summary>
/// Extension methods for adding SQL settings configuration.
/// </summary>
public static class SqlSettingsExtensions
{
    /// <summary>
    /// Adds SQL-based settings from KoreForge.Settings schema.
    /// </summary>
    public static IConfigurationBuilder AddSqlSettings(
        this IConfigurationBuilder builder,
        Action<SqlSettingsOptions>? configure = null)
    {
        // Build intermediate config to read bootstrap settings
        var tempConfig = builder.Build();

        var options = new SqlSettingsOptions
        {
            ConnectionString = tempConfig["KoreForge:Settings:ConnectionString"] ?? string.Empty,
            Application = tempConfig["KoreForge:Settings:Application"] ?? "EventProcessor",
            Instance = tempConfig["KoreForge:Settings:Instance"] ?? Environment.MachineName
        };

        configure?.Invoke(options);

        // Stash the options instance so it can be registered in DI later
        SqlSettingsOptionsHolder.Instance = options;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Console.WriteLine("[SqlSettings] No connection string configured, skipping SQL settings");
            return builder;
        }

        builder.Add(new SqlSettingsConfigurationSource(options));
        return builder;
    }
}

/// <summary>
/// Holds the SqlSettingsOptions instance created during configuration building
/// so it can be registered as a singleton in the DI container.
/// </summary>
public static class SqlSettingsOptionsHolder
{
    public static SqlSettingsOptions Instance { get; set; } = new();
}
