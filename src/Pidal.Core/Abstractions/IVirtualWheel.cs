using Pidal.Core.Pid;

namespace Pidal.Core.Abstractions;

/// <summary>
/// Northbound virtual wheel. The HIDMaestro implementation exposes a standard
/// HID PID 1.0 joystick to WebHID.
/// </summary>
public interface IVirtualWheel : IDisposable
{
    string ProductName { get; }

    event EventHandler<PidOutputPacket>? OutputReceived;

    void Start();

    void Stop();

    void SubmitInput(WheelInputFrame frame);

    void PublishPidState(PidState state);

    /// <summary>
    /// The EBI assigned by the HIDMaestro driver after a Create New Effect
    /// Feature report, or null when no allocation has happened.
    /// </summary>
    byte? GetLastAllocatedEffectBlockIndex();
}
