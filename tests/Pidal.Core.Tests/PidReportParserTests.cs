using Pidal.Core.Pid;

namespace Pidal.Core.Tests;

public class PidReportParserTests
{
    [Fact]
    public void ParsesSetEffect()
    {
        var data = new byte[13];
        data[0] = 3;
        data[1] = (byte)PidEffectType.ConstantForce;
        WriteU16(data, 2, 5000);
        WriteU16(data, 4, 10);
        WriteU16(data, 6, 1);
        WriteU16(data, 8, 25);
        data[10] = 200;
        data[11] = 4;
        data[12] = 0b0000_0111; // X + Y enabled, direction enabled

        var command = Assert.IsType<PidSetEffectCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.SetEffect, data)));

        Assert.Equal(3, command.EffectBlockIndex);
        Assert.Equal((byte)PidEffectType.ConstantForce, command.EffectType);
        Assert.Equal(5000, command.Duration);
        Assert.Equal(10, command.TriggerRepeatInterval);
        Assert.Equal(1, command.SamplePeriod);
        Assert.Equal(25, command.StartDelay);
        Assert.Equal(200, command.Gain);
        Assert.Equal(4, command.TriggerButton);
        Assert.Equal(0b11, command.AxesEnable);
        Assert.True(command.DirectionEnable);
    }

    [Fact]
    public void ParsesNegativeConstantForce()
    {
        var data = new byte[3];
        data[0] = 1;
        WriteI16(data, 1, -7500);

        var command = Assert.IsType<PidSetConstantForceCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.SetConstantForce, data)));

        Assert.Equal(1, command.EffectBlockIndex);
        Assert.Equal(-7500, command.Magnitude);
    }

    [Theory]
    [InlineData((byte)PidEffectOperation.Start, 1)]
    [InlineData((byte)PidEffectOperation.StartSolo, 2)]
    [InlineData((byte)PidEffectOperation.Stop, 3)]
    public void ParsesEffectOperation(byte operation, byte expected)
    {
        var data = new byte[] { 1, operation, 0 };

        var command = Assert.IsType<PidEffectOperationCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.EffectOperation, data)));

        Assert.Equal(1, command.EffectBlockIndex);
        Assert.Equal(expected, (byte)command.Operation);
        Assert.Equal(0, command.LoopCount);
    }

    [Fact]
    public void EffectOperationDefaultsLoopCount()
    {
        var data = new byte[] { 2, (byte)PidEffectOperation.Start };

        var command = Assert.IsType<PidEffectOperationCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.EffectOperation, data)));

        Assert.Equal(1, command.LoopCount);
    }

    [Fact]
    public void ParsesDeviceControl()
    {
        var data = new byte[] { (byte)PidDeviceControl.Reset };

        var command = Assert.IsType<PidDeviceControlCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.DeviceControl, data)));

        Assert.Equal(PidDeviceControl.Reset, command.Control);
    }

    [Fact]
    public void RejectsUnknownDeviceControl()
    {
        var data = new byte[] { 9 };

        var command = Assert.IsType<PidMalformedCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.DeviceControl, data)));

        Assert.Contains("outside 1..6", command.Reason);
    }

    [Fact]
    public void ParsesDeviceGain()
    {
        var command = Assert.IsType<PidDeviceGainCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.DeviceGain, new byte[] { 255 })));

        Assert.Equal(255, command.Gain);
    }

    [Fact]
    public void ParsesBlockFree()
    {
        var command = Assert.IsType<PidBlockFreeCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.BlockFree, new byte[] { 7 })));

        Assert.Equal(7, command.EffectBlockIndex);
    }

    [Fact]
    public void ParsesCreateNewEffectFeature()
    {
        var data = new byte[] { (byte)PidEffectType.ConstantForce, 0x2C, 0x01 };

        var command = Assert.IsType<PidCreateNewEffectCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidFeature, PidReportIds.CreateNewEffect, data)));

        Assert.Equal((byte)PidEffectType.ConstantForce, command.EffectType);
        Assert.Equal(300, command.ByteCount);
    }

    [Fact]
    public void MasksCreateNewEffectByteCountToTenBits()
    {
        var data = new byte[] { 1, 0xFF, 0xFF };

        var command = Assert.IsType<PidCreateNewEffectCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidFeature, PidReportIds.CreateNewEffect, data)));

        Assert.Equal(0x03FF, command.ByteCount);
    }

    [Fact]
    public void RejectsShortSetEffect()
    {
        var command = Assert.IsType<PidMalformedCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.SetEffect, new byte[12])));

        Assert.Contains("at least 13", command.Reason);
    }

    [Fact]
    public void IgnoresPeriodicReport()
    {
        var command = Assert.IsType<PidIgnoredCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidOutput, PidReportIds.SetPeriodic, new byte[12])));

        Assert.Equal(PidReportIds.SetPeriodic, command.ReportId);
    }

    [Fact]
    public void IgnoresFeatureRead()
    {
        var command = Assert.IsType<PidIgnoredCommand>(
            PidReportParser.Parse(Packet(PidReportSource.HidFeatureRead, PidReportIds.BlockLoad, new byte[5])));

        Assert.Equal(PidReportSource.HidFeatureRead, command.Source);
    }

    private static PidOutputPacket Packet(PidReportSource source, byte reportId, byte[] data)
    {
        return new PidOutputPacket(source, reportId, data);
    }

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteI16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }
}
