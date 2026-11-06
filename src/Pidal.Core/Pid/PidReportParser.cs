using System.Buffers.Binary;

namespace Pidal.Core.Pid;

/// <summary>
/// Parses standard PID 1.0 reports against the canonical HIDMaestro PID FFB
/// block. Unknown or malformed reports are
/// returned as commands rather than thrown so the bridge can log and continue.
/// </summary>
public static class PidReportParser
{
    public static PidCommand Parse(PidOutputPacket packet)
    {
        var data = packet.Data.Span;

        if (packet.Source == PidReportSource.HidFeature)
        {
            return packet.ReportId == PidReportIds.CreateNewEffect
                ? ParseCreateNewEffect(packet.ReportId, data)
                : new PidIgnoredCommand(packet.ReportId, packet.Source);
        }

        if (packet.Source != PidReportSource.HidOutput)
        {
            return new PidIgnoredCommand(packet.ReportId, packet.Source);
        }

        return packet.ReportId switch
        {
            PidReportIds.SetEffect => ParseSetEffect(packet.ReportId, data),
            PidReportIds.SetConstantForce => ParseSetConstantForce(packet.ReportId, data),
            PidReportIds.EffectOperation => ParseEffectOperation(packet.ReportId, data),
            PidReportIds.BlockFree => ParseBlockFree(packet.ReportId, data),
            PidReportIds.DeviceControl => ParseDeviceControl(packet.ReportId, data),
            PidReportIds.DeviceGain => ParseDeviceGain(packet.ReportId, data),
            PidReportIds.SetEnvelope or
            PidReportIds.SetCondition or
            PidReportIds.SetPeriodic or
            PidReportIds.SetRampForce or
            PidReportIds.CustomForceData or
            PidReportIds.DownloadForceSample or
            PidReportIds.SetCustomForce => new PidIgnoredCommand(packet.ReportId, packet.Source),
            _ => new PidIgnoredCommand(packet.ReportId, packet.Source),
        };
    }

    private static PidCommand ParseSetEffect(byte reportId, ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
        {
            return Malformed(reportId, data.Length, 13);
        }

        var axesAndDirection = data[12];
        var direction = data.Length >= 15
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(13, 2))
            : (ushort)0;

        return new PidSetEffectCommand(
            EffectBlockIndex: data[0],
            EffectType: data[1],
            Duration: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2)),
            TriggerRepeatInterval: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2)),
            SamplePeriod: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)),
            StartDelay: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            Gain: data[10],
            TriggerButton: data[11],
            AxesEnable: (byte)(axesAndDirection & 0x03),
            DirectionEnable: (axesAndDirection & 0x04) != 0,
            Direction: direction);
    }

    private static PidCommand ParseSetConstantForce(byte reportId, ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return Malformed(reportId, data.Length, 3);
        }

        return new PidSetConstantForceCommand(
            EffectBlockIndex: data[0],
            Magnitude: BinaryPrimitives.ReadInt16LittleEndian(data.Slice(1, 2)));
    }

    private static PidCommand ParseEffectOperation(byte reportId, ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return Malformed(reportId, data.Length, 2);
        }

        var operation = data[1];
        if (operation is < (byte)PidEffectOperation.Start or > (byte)PidEffectOperation.Stop)
        {
            return new PidMalformedCommand(reportId, PidReportSource.HidOutput,
                $"Effect Operation value {operation} is outside 1..3.");
        }

        return new PidEffectOperationCommand(
            EffectBlockIndex: data[0],
            Operation: (PidEffectOperation)operation,
            LoopCount: data.Length >= 3 ? data[2] : (byte)1);
    }

    private static PidCommand ParseBlockFree(byte reportId, ReadOnlySpan<byte> data)
    {
        return data.Length < 1
            ? Malformed(reportId, data.Length, 1)
            : new PidBlockFreeCommand(data[0]);
    }

    private static PidCommand ParseDeviceControl(byte reportId, ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return Malformed(reportId, data.Length, 1);
        }

        var control = data[0];
        if (control is < (byte)PidDeviceControl.EnableActuators or > (byte)PidDeviceControl.Continue)
        {
            return new PidMalformedCommand(reportId, PidReportSource.HidOutput,
                $"Device Control value {control} is outside 1..6.");
        }

        return new PidDeviceControlCommand((PidDeviceControl)control);
    }

    private static PidCommand ParseDeviceGain(byte reportId, ReadOnlySpan<byte> data)
    {
        return data.Length < 1
            ? Malformed(reportId, data.Length, 1)
            : new PidDeviceGainCommand(data[0]);
    }

    private static PidCommand ParseCreateNewEffect(byte reportId, ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return new PidMalformedCommand(reportId, PidReportSource.HidFeature,
                $"Create New Effect needs 3 data bytes, got {data.Length}.");
        }

        var byteCount = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1, 2)) & 0x03FF);
        return new PidCreateNewEffectCommand(
            EffectType: data[0],
            ByteCount: byteCount);
    }

    private static PidMalformedCommand Malformed(byte reportId, int actual, int required)
    {
        return new PidMalformedCommand(reportId, PidReportSource.HidOutput,
            $"Report 0x{reportId:X2} needs at least {required} data bytes, got {actual}.");
    }
}
