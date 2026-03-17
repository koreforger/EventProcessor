using KF.Web.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventProcessor.HealthChecks;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for plugging in
/// component-level health checks.
///
/// Each integration area owns one extension method here. The method:
///   1. Registers any shared state singleton the check needs.
///   2. Registers the <see cref="IHealthCheck"/> implementation under a stable name.
///   3. Applies the appropriate <see cref="HealthTags"/> for endpoint filtering.
///
/// ── How to add a new check ──────────────────────────────────────────────────
///   1. Create a state holder (e.g. <c>KafkaProducerState</c>) if the check needs
///      runtime state reported by a background service or hosted service.
///   2. Create an <see cref="IHealthCheck"/> implementation that reads that state.
///   3. Add an extension method here following the pattern below.
///   4. Chain the method from the correct <c>Add*()</c> call in
///      <see cref="EventProcessor.ServiceCollectionExtensions"/>.
/// ────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that reflects the SQL settings polling loop state.
    /// The check reads <see cref="SqlSettingsHealthCheck"/> — no extra DB round-trip.
    /// Tags: <see cref="HealthTags.Sql"/>, <see cref="HealthTags.Ready"/>.
    /// </summary>
    public static IHealthChecksBuilder AddSqlSettingsHealthCheck(
        this IHealthChecksBuilder builder) =>
        builder.AddCheck<SqlSettingsHealthCheck>(
            name: "sql-settings",
            failureStatus: HealthStatus.Degraded,
            tags: [HealthTags.Sql, HealthTags.Ready]);

    /// <summary>
    /// Registers a health check that reflects the Kafka consumer host running state.
    /// Also registers the shared <see cref="KafkaConsumerState"/> singleton that
    /// <c>TransactionConsumerWorker</c> writes into.
    /// Tags: <see cref="HealthTags.Kafka"/>, <see cref="HealthTags.Ready"/>.
    /// </summary>
    public static IHealthChecksBuilder AddKafkaConsumerHealthCheck(
        this IHealthChecksBuilder builder)
    {
        builder.Services.AddSingleton<KafkaConsumerState>();
        return builder.AddCheck<KafkaConsumerHealthCheck>(
            name: "kafka-consumer",
            failureStatus: HealthStatus.Unhealthy,
            tags: [HealthTags.Kafka, HealthTags.Ready]);
    }

    // ── Future checks (follow the same pattern) ──────────────────────────────
    //
    // public static IHealthChecksBuilder AddKafkaProducerHealthCheck(...)
    // {
    //     builder.Services.AddSingleton<KafkaProducerState>();
    //     return builder.AddCheck<KafkaProducerHealthCheck>(
    //         name: "kafka-producer",
    //         failureStatus: HealthStatus.Unhealthy,
    //         tags: [HealthTags.Kafka, HealthTags.Ready]);
    // }
}
