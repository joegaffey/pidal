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
}
