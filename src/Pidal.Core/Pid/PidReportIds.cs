namespace Pidal.Core.Pid;

public static class PidReportIds
{
    public const byte SetEffect = 0x11;
    public const byte SetEnvelope = 0x12;
    public const byte SetCondition = 0x13;
    public const byte SetPeriodic = 0x14;
    public const byte SetConstantForce = 0x15;
    public const byte SetRampForce = 0x16;
    public const byte CustomForceData = 0x17;
    public const byte DownloadForceSample = 0x18;
    public const byte EffectOperation = 0x1A;
    public const byte BlockFree = 0x1B;
    public const byte DeviceControl = 0x1C;
    public const byte DeviceGain = 0x1D;
    public const byte SetCustomForce = 0x1E;

    public const byte CreateNewEffect = 0x11;
    public const byte BlockLoad = 0x12;
    public const byte Pool = 0x13;
    public const byte State = 0x14;
}
