namespace Pidal.Core.Bridge;

public sealed record BridgeOptions
{
    /// <summary>Physical input polling rate. 0 or negative is clamped to 1 Hz.</summary>
    public int InputPollHz { get; init; } = 250;

    /// <summary>
    /// Stop a playing effect when no PID output report has arrived for this
    /// long. 0 disables the watchdog.
    /// </summary>
    public int WatchdogMs { get; init; } = 5000;

    public int WatchdogCheckMs { get; init; } = 250;

    /// <summary>
    /// PID usage-array order. Auto detects the reversed order used by current
    /// webhid-wheel builds; Standard and Reversed force a mode.
    /// </summary>
    public PidUsageOrder PidUsageOrder { get; init; } = PidUsageOrder.Auto;
}
