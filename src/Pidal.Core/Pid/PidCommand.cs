namespace Pidal.Core.Pid;

public abstract record PidCommand(byte ReportId);

public sealed record PidSetEffectCommand(
    byte EffectBlockIndex,
    byte EffectType,
    ushort Duration,
    ushort TriggerRepeatInterval,
    ushort SamplePeriod,
    ushort StartDelay,
    byte Gain,
    byte TriggerButton,
    byte AxesEnable,
    bool DirectionEnable,
    ushort Direction) : PidCommand(PidReportIds.SetEffect);

public sealed record PidSetConstantForceCommand(
    byte EffectBlockIndex,
    short Magnitude) : PidCommand(PidReportIds.SetConstantForce);

public sealed record PidEffectOperationCommand(
    byte EffectBlockIndex,
    PidEffectOperation Operation,
    byte LoopCount) : PidCommand(PidReportIds.EffectOperation);

public sealed record PidBlockFreeCommand(
    byte EffectBlockIndex) : PidCommand(PidReportIds.BlockFree);

public sealed record PidDeviceControlCommand(
    PidDeviceControl Control) : PidCommand(PidReportIds.DeviceControl);

public sealed record PidDeviceGainCommand(
    byte Gain) : PidCommand(PidReportIds.DeviceGain);

public sealed record PidCreateNewEffectCommand(
    byte EffectType,
    ushort ByteCount) : PidCommand(PidReportIds.CreateNewEffect);

public sealed record PidIgnoredCommand(
    byte ReportId,
    PidReportSource Source) : PidCommand(ReportId);

public sealed record PidMalformedCommand(
    byte ReportId,
    PidReportSource Source,
    string Reason) : PidCommand(ReportId);
