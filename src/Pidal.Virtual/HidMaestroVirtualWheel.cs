using System.Buffers.Binary;
using HIDMaestro;
using Pidal.Core.Abstractions;
using Pidal.Core.Pid;

namespace Pidal.Virtual;

/// <summary>
/// Standard HID PID 1.0 wheel implemented with HIDMaestro. The descriptor is a
/// joystick with X/Y/Rz/Rx axes, 32 buttons, an 8-way hat, and the canonical
/// HIDMaestro PID FFB block.
/// </summary>
public sealed class HidMaestroVirtualWheel : IVirtualWheel
{
    private readonly VirtualWheelOptions _options;
    private HMContext? _context;
    private HMController? _controller;
    private bool _started;
    private bool _disposed;

    public HidMaestroVirtualWheel(VirtualWheelOptions options)
    {
        _options = options;
    }

    public string ProductName => _options.ProductName;

    public event EventHandler<PidOutputPacket>? OutputReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _context = new HMContext();
        _context.LoadDefaultProfiles();
        _context.InstallDriver();

        var profile = BuildProfile();
        _controller = _context.CreateController(profile, _options.IdentityKey);
        _controller.PublishPidPool(
            ramPoolSize: _options.RamPoolSize,
            simultaneousEffectsMax: _options.MaxSimultaneousEffects,
            deviceManagedPool: true,
            sharedParameterBlocks: false);
        _controller.PublishPidState(
            effectBlockIndex: 0,
            flags: PidStateFlags.ActuatorsEnabled | PidStateFlags.ActuatorPower);
        _controller.OutputReceived += OnOutputReceived;

        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        if (_controller is not null)
        {
            _controller.OutputReceived -= OnOutputReceived;
            _controller.Dispose();
            _controller = null;
        }

        _context?.Dispose();
        _context = null;
        _started = false;
    }

    public void SubmitInput(WheelInputFrame frame)
    {
        var controller = _controller;
        if (controller is null)
        {
            return;
        }

        Span<byte> data = stackalloc byte[13];

        var steering = Math.Clamp(frame.Steering - 32768, short.MinValue, short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(data[..2], (short)steering);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(2, 2), ClampU16(frame.Throttle));
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(4, 2), ClampU16(frame.Brake));
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(6, 2), ClampU16(frame.Clutch));
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(8, 4), frame.Buttons);
        data[12] = frame.Hat is >= 0 and < 8 ? (byte)frame.Hat : (byte)8;

        // SubmitRawReport takes data bytes only; the driver prepends Report ID 0x01.
        controller.SubmitRawReport(data);
    }

    public void PublishPidState(PidState state)
    {
        var controller = _controller;
        if (controller is null)
        {
            return;
        }

        var flags = PidStateFlags.None;
        if (state.ActuatorsEnabled)
        {
            flags |= PidStateFlags.ActuatorsEnabled | PidStateFlags.ActuatorPower;
        }

        if (state.EffectPlaying)
        {
            flags |= PidStateFlags.EffectPlaying;
        }

        if (state.Paused)
        {
            flags |= PidStateFlags.DeviceIsPaused;
        }

        if (state.SafetySwitch)
        {
            flags |= PidStateFlags.SafetySwitch;
        }

        controller.PublishPidState(state.EffectBlockIndex, flags);
    }

    public byte? GetLastAllocatedEffectBlockIndex()
    {
        var controller = _controller;
        if (controller is null)
        {
            return null;
        }

        var blockLoad = controller.GetCurrentPidBlockLoad();
        return blockLoad.EffectBlockIndex == 0 ? null : blockLoad.EffectBlockIndex;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private HMProfile BuildProfile()
    {
        var descriptor = new HidDescriptorBuilder()
            .Joystick()
            .AddAxis(HMAxis.X, 16, -32768, 32767)
            .AddAxis(HMAxis.Y, 16)
            .AddAxis(HMAxis.Rz, 16)
            .AddAxis(HMAxis.Rx, 16)
            .AddButtons(32)
            .AddHat(8)
            .AddPidFfbBlock();

        return new HMProfileBuilder()
            .Id(_options.IdentityKey)
            .Name(_options.ProductName)
            .Vendor(_options.Manufacturer)
            .Vid(_options.VendorId)
            .Pid(_options.ProductId)
            .ProductString(_options.ProductName)
            .ManufacturerString(_options.Manufacturer)
            .Type("wheel")
            .Connection("usb")
            .FromDescriptorBuilder(descriptor)
            .Notes("PIDAL virtual HID PID 1.0 wheel")
            .Build();
    }

    private void OnOutputReceived(HMController controller, HMOutputPacket packet)
    {
        var source = packet.Source switch
        {
            HMOutputSource.HidOutput => PidReportSource.HidOutput,
            HMOutputSource.HidFeature => PidReportSource.HidFeature,
            HMOutputSource.HidFeatureRead => PidReportSource.HidFeatureRead,
            HMOutputSource.XInput => PidReportSource.XInput,
            _ => PidReportSource.HidOutput,
        };

        OutputReceived?.Invoke(
            this,
            new PidOutputPacket(source, packet.ReportId, packet.Data.ToArray()));
    }

    private static ushort ClampU16(int value)
    {
        return (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }
}
