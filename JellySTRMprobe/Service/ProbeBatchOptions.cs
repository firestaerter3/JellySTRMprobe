using System;
using System.Threading;
using System.Threading.Tasks;

namespace JellySTRMprobe.Service;

/// <summary>
/// Settings for one batch of probes.
/// </summary>
public class ProbeBatchOptions
{
    /// <summary>
    /// Gets the maximum number of concurrent probe operations.
    /// </summary>
    public int Parallelism { get; init; } = 1;

    /// <summary>
    /// Gets the per-item timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets the cooldown in milliseconds after each probe.
    /// </summary>
    public int CooldownMs { get; init; }

    /// <summary>
    /// Gets the largest random extra pause in milliseconds after each probe.
    /// </summary>
    public int CooldownJitterMs { get; init; }

    /// <summary>
    /// Gets the number of consecutive failures after which the batch stops. Zero or less means never.
    /// </summary>
    public int MaxConsecutiveFailures { get; init; }

    /// <summary>
    /// Gets a callback awaited before each probe, for example to wait until nobody is watching.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeEachAsync { get; init; }
}
