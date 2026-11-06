namespace Pidal.Core.Pid;

/// <summary>HID PID 1.0 Device Control values.</summary>
public enum PidDeviceControl : byte
{
    EnableActuators = 1,
    DisableActuators = 2,
    StopAllEffects = 3,
    Reset = 4,
    Pause = 5,
    Continue = 6,
}
