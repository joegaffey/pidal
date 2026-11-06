namespace Pidal.Core.Pid;

/// <summary>
/// One PID effect block. The first release renders only constant force, but the
/// state tracks the other fields so future backends can use them.
/// </summary>
public sealed class PidEffectBlock
{
    public PidEffectBlock(byte index)
    {
        Index = index;
    }

    public byte Index { get; }

    public byte EffectType { get; set; } = (byte)PidEffectType.ConstantForce;

    public ushort Duration { get; set; }

    public ushort TriggerRepeatInterval { get; set; }

    public ushort SamplePeriod { get; set; }

    public ushort StartDelay { get; set; }

    public byte Gain { get; set; } = 255;

    public byte TriggerButton { get; set; }

    public byte AxesEnable { get; set; }

    public bool DirectionEnable { get; set; }

    public ushort Direction { get; set; }

    public short Magnitude { get; set; }

    public bool IsPlaying { get; set; }

    public bool IsConstantForce => EffectType == (byte)PidEffectType.ConstantForce;
}
