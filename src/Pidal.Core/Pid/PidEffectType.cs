namespace Pidal.Core.Pid;

/// <summary>
/// Effect Type values in the canonical HIDMaestro PID block. The descriptor
/// lists the usages in this order, so the wire value is the index starting at 1.
/// </summary>
public enum PidEffectType : byte
{
    ConstantForce = 1,
    RampForce = 2,
    Square = 3,
    Sine = 4,
    Triangle = 5,
    SawtoothUp = 6,
    SawtoothDown = 7,
    Spring = 8,
    Damper = 9,
    Inertia = 10,
    Friction = 11,
    CustomForce = 12,
}
