using System.Collections.Concurrent;
using System.Diagnostics;
using Pidal.Core.Abstractions;
using Pidal.Core.Bridge;
using Pidal.Core.Diagnostics;
using Pidal.Core.Pid;

namespace Pidal.Core.Tests;

public class PidBridgeTests
{
    [Fact]
    public void AppliesWebHidWheelSequenceWithoutCreateNewEffect()
    {
        var backend = new FakeBackend();
        var wheel = new FakeVirtualWheel();
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 0);

        bridge.Start();
        wheel.Emit(SetEffect(1));
        wheel.Emit(SetConstantForce(1, 5000));
        wheel.Emit(EffectOperation(1, PidEffectOperation.Start));

        Assert.Equal(new[] { 5000 }, backend.Starts);
        Assert.Contains(wheel.States, state => state.EffectPlaying && state.EffectBlockIndex == 1);

        wheel.Emit(SetConstantForce(1, -2000));
        Assert.Contains(-2000, backend.Updates);

        wheel.Emit(EffectOperation(1, PidEffectOperation.Stop));
        Assert.True(backend.StopCount > 0);
        Assert.Contains(wheel.States, state => !state.EffectPlaying);
    }

    [Fact]
    public void AcceptsConstantForceWithoutSetEffect()
    {
        var backend = new FakeBackend();
        var wheel = new FakeVirtualWheel();
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 0);

        bridge.Start();
        wheel.Emit(SetConstantForce(1, 3200));
        wheel.Emit(EffectOperation(1, PidEffectOperation.Start));

        Assert.Equal(new[] { 3200 }, backend.Starts);
    }

    [Fact]
    public void UsesAllocatedEbiFromCreateNewEffect()
    {
        var backend = new FakeBackend();
        var wheel = new FakeVirtualWheel { LastEbi = 5 };
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 0);

        bridge.Start();
        wheel.Emit(CreateNewEffect());
        wheel.Emit(SetConstantForce(5, 1000));
        wheel.Emit(EffectOperation(5, PidEffectOperation.Start));

        Assert.Equal(new[] { 1000 }, backend.Starts);
        Assert.Contains(wheel.States, state => state.EffectBlockIndex == 5);
    }

    [Fact]
    public void ResetStopsAndClearsEffects()
    {
        var backend = new FakeBackend();
        var wheel = new FakeVirtualWheel();
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 0);

        bridge.Start();
        wheel.Emit(SetEffect(1));
        wheel.Emit(SetConstantForce(1, 4000));
        wheel.Emit(EffectOperation(1, PidEffectOperation.Start));
        wheel.Emit(DeviceControl(PidDeviceControl.Reset));

        Assert.Contains(PidDeviceControl.Reset, backend.Controls);
        Assert.True(backend.StopCount > 0);
        Assert.Contains(wheel.States, state => !state.EffectPlaying);
    }

    [Fact]
    public void WatchdogStopsStaleEffect()
    {
        var backend = new FakeBackend();
        var wheel = new FakeVirtualWheel();
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 100);

        bridge.Start();
        wheel.Emit(SetEffect(1));
        wheel.Emit(SetConstantForce(1, 5000));
        wheel.Emit(EffectOperation(1, PidEffectOperation.Start));

        var stopwatch = Stopwatch.StartNew();
        while (backend.StopCount == 0 && stopwatch.ElapsedMilliseconds < 3000)
        {
            Thread.Sleep(20);
        }

        Assert.True(backend.StopCount > 0);
        Assert.Contains(wheel.States, state => !state.EffectPlaying);
    }

    [Fact]
    public void PollsInputIntoVirtualWheel()
    {
        var backend = new FakeBackend
        {
            NextInput = new WheelInputFrame(1000, 2000, 3000, 4000, 0b101, 3),
        };
        var wheel = new FakeVirtualWheel();
        using var bridge = CreateBridge(backend, wheel, watchdogMs: 0);

        bridge.Start();
        var stopwatch = Stopwatch.StartNew();
        while (wheel.InputFrames.Count == 0 && stopwatch.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(10);
        }

        Assert.Contains(new WheelInputFrame(1000, 2000, 3000, 4000, 0b101, 3), wheel.InputFrames);
    }

    private static PidBridge CreateBridge(FakeBackend backend, FakeVirtualWheel wheel, int watchdogMs)
    {
        return new PidBridge(
            backend,
            wheel,
            new BridgeOptions
            {
                InputPollHz = 1000,
                WatchdogMs = watchdogMs,
                WatchdogCheckMs = 20,
            },
            new FakeLog());
    }

    private static PidOutputPacket SetEffect(byte ebi)
    {
        var data = new byte[13];
        data[0] = ebi;
        data[1] = (byte)PidEffectType.ConstantForce;
        data[10] = 255;
        data[12] = 0b0000_0111;
        return new PidOutputPacket(PidReportSource.HidOutput, PidReportIds.SetEffect, data);
    }

    private static PidOutputPacket SetConstantForce(byte ebi, short magnitude)
    {
        var data = new byte[3];
        data[0] = ebi;
        data[1] = (byte)magnitude;
        data[2] = (byte)(magnitude >> 8);
        return new PidOutputPacket(PidReportSource.HidOutput, PidReportIds.SetConstantForce, data);
    }

    private static PidOutputPacket EffectOperation(byte ebi, PidEffectOperation operation)
    {
        return new PidOutputPacket(
            PidReportSource.HidOutput,
            PidReportIds.EffectOperation,
            new byte[] { ebi, (byte)operation, 1 });
    }

    private static PidOutputPacket DeviceControl(PidDeviceControl control)
    {
        return new PidOutputPacket(
            PidReportSource.HidOutput,
            PidReportIds.DeviceControl,
            new[] { (byte)control });
    }

    private static PidOutputPacket CreateNewEffect()
    {
        return new PidOutputPacket(
            PidReportSource.HidFeature,
            PidReportIds.CreateNewEffect,
            new byte[] { (byte)PidEffectType.ConstantForce, 0x2C, 0x01 });
    }

    private sealed class FakeBackend : IFfbBackend
    {
        public string Name => "fake";

        public bool IsOpen { get; private set; }

        public event EventHandler<string>? Disconnected;

        public List<int> Starts { get; } = new();

        public List<int> Updates { get; } = new();

        public List<PidDeviceControl> Controls { get; } = new();

        public int StopCount { get; private set; }

        public int Gain { get; private set; }

        public WheelInputFrame? NextInput { get; set; }

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        public void StartConstantForce(int magnitude) => Starts.Add(magnitude);

        public void UpdateConstantForce(int magnitude) => Updates.Add(magnitude);

        public void StopConstantForce() => StopCount++;

        public void SetDeviceGain(int gain) => Gain = gain;

        public void HandleDeviceControl(PidDeviceControl control)
        {
            Controls.Add(control);
            if (control is PidDeviceControl.DisableActuators
                or PidDeviceControl.StopAllEffects
                or PidDeviceControl.Reset
                or PidDeviceControl.Pause)
            {
                StopCount++;
            }
        }

        public WheelInputFrame? PollInput() => NextInput;

        public void Dispose()
        {
            _ = Disconnected;
        }
    }

    private sealed class FakeVirtualWheel : IVirtualWheel
    {
        public string ProductName => "fake wheel";

        public event EventHandler<PidOutputPacket>? OutputReceived;

        public bool Started { get; private set; }

        public byte? LastEbi { get; set; }

        public ConcurrentQueue<PidState> States { get; } = new();

        public ConcurrentQueue<WheelInputFrame> InputFrames { get; } = new();

        public void Start() => Started = true;

        public void Stop() => Started = false;

        public void SubmitInput(WheelInputFrame frame) => InputFrames.Enqueue(frame);

        public void PublishPidState(PidState state) => States.Enqueue(state);

        public byte? GetLastAllocatedEffectBlockIndex() => LastEbi;

        public void Emit(PidOutputPacket packet) => OutputReceived?.Invoke(this, packet);

        public void Dispose()
        {
            _ = OutputReceived;
        }
    }

    private sealed class FakeLog : IPidalLog
    {
        public void Debug(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
