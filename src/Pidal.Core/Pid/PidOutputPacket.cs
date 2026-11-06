namespace Pidal.Core.Pid;

/// <summary>
/// A HID output or feature report captured from the virtual wheel.
/// <see cref="Data"/> excludes the Report ID, matching HIDMaestro's
/// HMOutputPacket.Data.
/// </summary>
public readonly record struct PidOutputPacket(
    PidReportSource Source,
    byte ReportId,
    ReadOnlyMemory<byte> Data);
