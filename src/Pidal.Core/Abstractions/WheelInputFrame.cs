namespace Pidal.Core.Abstractions;

/// <summary>
/// A platform-neutral snapshot of the physical wheel.
/// Axis values use the DirectInput convention (0..65535, centered at 32768).
/// </summary>
public readonly record struct WheelInputFrame(
    int Steering,
    int Throttle,
    int Brake,
    int Clutch,
    uint Buttons,
    int Hat)
{
    public static WheelInputFrame Neutral { get; } = new(32768, 0, 0, 0, 0, -1);
}
