namespace Pidal.Core.Pid;

/// <summary>
/// The subset of the PID State report PIDAL publishes.
/// </summary>
public readonly record struct PidState(
    byte EffectBlockIndex,
    bool ActuatorsEnabled,
    bool EffectPlaying,
    bool Paused,
    bool SafetySwitch)
{
    public static PidState Initial { get; } = new(0, true, false, false, false);
}
