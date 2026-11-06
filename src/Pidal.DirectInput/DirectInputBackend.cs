using System.Runtime.InteropServices;
using Pidal.Core.Abstractions;
using Pidal.Core.Pid;
using Vortice.DirectInput;

namespace Pidal.DirectInput;

/// <summary>
/// Wheel-agnostic DirectInput backend. It selects a physical force-feedback
/// device, creates one constant-force effect on the steering axis, and polls
/// axes/buttons/hat for the virtual wheel.
/// </summary>
public sealed class DirectInputBackend : IFfbBackend
{
    private readonly DirectInputOptions _options;
    private readonly object _gate = new();

    private IDirectInput8? _directInput;
    private IDirectInputDevice8? _device;
    private IDirectInputEffect? _effect;
    private HiddenWindow? _hiddenWindow;
    private IntPtr _window;
    private int _steeringAxisOffset;
    private int _deviceGain = 10000;
    private bool _isPlaying;
    private bool _disconnectedRaised;
    private bool _timerResolutionRaised;
    private bool _open;

    public DirectInputBackend(DirectInputOptions options)
    {
        _options = options;
    }

    public string Name => _device is null ? "DirectInput (not open)" : $"DirectInput: {_device.DeviceInfo.ProductName}";

    public bool IsOpen => _open;

    public event EventHandler<string>? Disconnected;

    public static IReadOnlyList<DirectInputDeviceInfo> ListDevices()
    {
        using var directInput = DInput.DirectInput8Create();
        var result = new List<DirectInputDeviceInfo>();

        foreach (var instance in EnumerateCandidates(directInput))
        {
            var forceFeedback = false;
            try
            {
                using var device = directInput.CreateDevice(instance.InstanceGuid);
                forceFeedback = device.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
            }
            catch
            {
                // A device that cannot be opened is still worth listing.
            }

            result.Add(new DirectInputDeviceInfo(
                InstanceGuid: instance.InstanceGuid,
                InstanceName: instance.InstanceName,
                ProductName: instance.ProductName,
                Type: instance.Type.ToString(),
                ForceFeedback: forceFeedback,
                IsVirtual: IsVirtualDevice(instance)));
        }

        return result;
    }

    public static IReadOnlyList<DirectInputDeviceDiagnostics> Diagnose()
    {
        using var directInput = DInput.DirectInput8Create();
        var result = new List<DirectInputDeviceDiagnostics>();

        foreach (var instance in EnumerateCandidates(directInput))
        {
            var axes = new List<DirectInputAxisDiagnostics>();
            var effects = new List<DirectInputEffectDiagnostics>();
            string? error = null;
            var forceFeedback = false;

            try
            {
                using var device = directInput.CreateDevice(instance.InstanceGuid);
                forceFeedback = device.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);

                foreach (var obj in device.GetObjects())
                {
                    axes.Add(new DirectInputAxisDiagnostics(
                        Name: obj.Name ?? string.Empty,
                        ObjectType: obj.ObjectType.ToString(),
                        UsagePage: obj.UsagePage,
                        Usage: obj.Usage,
                        Offset: obj.Offset,
                        ObjectId: (int)obj.ObjectId,
                        ObjectIdFlags: obj.ObjectId.Flags.ToString(),
                        ForceFeedbackActuator: obj.ObjectId.Flags.HasFlag(DeviceObjectTypeFlags.ForceFeedbackActuator)));
                }

                foreach (var effect in device.GetEffects())
                {
                    effects.Add(new DirectInputEffectDiagnostics(
                        Name: effect.Name ?? string.Empty,
                        Type: effect.Type.ToString(),
                        Guid: effect.Guid));
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            result.Add(new DirectInputDeviceDiagnostics(
                InstanceGuid: instance.InstanceGuid,
                InstanceName: instance.InstanceName,
                ProductName: instance.ProductName,
                Type: instance.Type.ToString(),
                ForceFeedback: forceFeedback,
                IsVirtual: IsVirtualDevice(instance),
                Error: error,
                Axes: axes,
                Effects: effects));
        }

        return result;
    }

    public void Open()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_open, this);

            _hiddenWindow = HiddenWindow.Create();
            _window = _hiddenWindow.Handle;
            _timerResolutionRaised = NativeMethods.timeBeginPeriod(1) == 0;
            _directInput = DInput.DirectInput8Create();

            var selected = SelectDevice(_directInput, _options);
            _device = selected.Device;
            _deviceGain = _options.Gain;

            var cooperative = _options.Exclusive
                ? CooperativeLevel.Exclusive | CooperativeLevel.Background
                : CooperativeLevel.NonExclusive | CooperativeLevel.Background;

            Check(_device.SetCooperativeLevel(_window, cooperative), "SetCooperativeLevel");
            Check(_device.SetDataFormat<RawJoystickState>(), "SetDataFormat");
            ConfigureForceFeedback(_device);
            Check(_device.Acquire(), "Acquire");

            _steeringAxisOffset = _options.AxisOffset ?? DiscoverSteeringAxisOffset(_device);
            CreateEffect(_device);
            _open = true;
            _disconnectedRaised = false;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (!_open && _device is null && _directInput is null
                && _hiddenWindow is null && !_timerResolutionRaised)
            {
                return;
            }

            try
            {
                StopConstantForceCore();
            }
            catch
            {
                // Best effort on shutdown.
            }

            if (_effect is not null)
            {
                try
                {
                    _effect.Unload();
                }
                catch
                {
                    // Best effort.
                }

                _effect.Dispose();
                _effect = null;
            }

            if (_device is not null)
            {
                try
                {
                    _device.Unacquire();
                }
                catch
                {
                    // Best effort.
                }

                _device.Dispose();
                _device = null;
            }

            _directInput?.Dispose();
            _directInput = null;

            _hiddenWindow?.Dispose();
            _hiddenWindow = null;
            _window = IntPtr.Zero;
            _open = false;

            if (_timerResolutionRaised)
            {
                NativeMethods.timeEndPeriod(1);
                _timerResolutionRaised = false;
            }
        }
    }

    public void StartConstantForce(int magnitude)
    {
        lock (_gate)
        {
            if (_effect is null)
            {
                return;
            }

            SetMagnitudeCore(magnitude);

            if (!_isPlaying)
            {
                Check(_effect.Start(-1), "Effect.Start");
                _isPlaying = true;
            }
        }
    }

    public void UpdateConstantForce(int magnitude)
    {
        lock (_gate)
        {
            if (_effect is null)
            {
                return;
            }

            SetMagnitudeCore(magnitude);

            if (!_isPlaying)
            {
                Check(_effect.Start(-1), "Effect.Start");
                _isPlaying = true;
            }
        }
    }

    public void StopConstantForce()
    {
        lock (_gate)
        {
            StopConstantForceCore();
        }
    }

    public void SetDeviceGain(int gain)
    {
        lock (_gate)
        {
            _deviceGain = Math.Clamp(gain, 0, 255);

            if (_effect is null)
            {
                return;
            }

            var directInputGain = (int)Math.Round(_deviceGain / 255.0 * 10000.0);
            _effect.SetParameters(
                new EffectParameters { Gain = directInputGain },
                EffectParameterFlags.Gain);
        }
    }

    public void HandleDeviceControl(PidDeviceControl control)
    {
        lock (_gate)
        {
            if (_device is null)
            {
                return;
            }

            switch (control)
            {
                case PidDeviceControl.EnableActuators:
                    SendForceFeedback(ForceFeedbackCommand.SetActuatorsOn);
                    EnsureAcquired();
                    break;

                case PidDeviceControl.Continue:
                    SendForceFeedback(ForceFeedbackCommand.Continue);
                    EnsureAcquired();
                    break;

                case PidDeviceControl.DisableActuators:
                    StopConstantForceCore();
                    SendForceFeedback(ForceFeedbackCommand.SetActuatorsOff);
                    break;

                case PidDeviceControl.StopAllEffects:
                    StopConstantForceCore();
                    SendForceFeedback(ForceFeedbackCommand.StopAll);
                    break;

                case PidDeviceControl.Reset:
                    StopConstantForceCore();
                    SendForceFeedback(ForceFeedbackCommand.Reset);
                    break;

                case PidDeviceControl.Pause:
                    StopConstantForceCore();
                    SendForceFeedback(ForceFeedbackCommand.Pause);
                    break;
            }
        }
    }

    public WheelInputFrame? PollInput()
    {
        lock (_gate)
        {
            if (!_open || _device is null)
            {
                return null;
            }

            var poll = _device.Poll();
            if (poll.Failure)
            {
                if (!TryReacquire())
                {
                    RaiseDisconnected($"Poll failed: {poll.Description}");
                    return null;
                }
            }

            JoystickState state;
            try
            {
                state = _device.GetCurrentJoystickState();
            }
            catch (Exception ex)
            {
                if (!TryReacquire())
                {
                    RaiseDisconnected($"GetCurrentJoystickState failed: {ex.Message}");
                    return null;
                }

                state = _device.GetCurrentJoystickState();
            }

            _disconnectedRaised = false;
            return new WheelInputFrame(
                Steering: state.X,
                Throttle: state.Y,
                Brake: state.RotationZ,
                Clutch: state.RotationX,
                Buttons: PackButtons(state.Buttons),
                Hat: ConvertPov(state.PointOfViewControllers));
        }
    }

    public void Dispose()
    {
        Close();
    }

    private static void ConfigureForceFeedback(IDirectInputDevice8 device)
    {
        // Keep the wheel's centering spring from masking or fighting the effect.
        try
        {
            device.Properties.AutoCenter = false;
        }
        catch
        {
            // Not every driver exposes autocenter.
        }

        // Raise the device-wide FFB gain; effect gain is set separately.
        try
        {
            device.Properties.ForceFeedbackGain = 10000;
        }
        catch
        {
            // Not every driver exposes a global gain.
        }
    }

    private void CreateEffect(IDirectInputDevice8 device)
    {
        var effectInfo = device
            .GetEffects(EffectType.ConstantForce)
            .FirstOrDefault();

        if (effectInfo is null)
        {
            throw new InvalidOperationException(
                $"'{device.DeviceInfo.ProductName}' does not expose a DirectInput constant-force effect.");
        }

        Exception? objectIdError = null;
        var actuatorIds = FindForceFeedbackActuatorIds(device);
        if (actuatorIds.Length > 0)
        {
            try
            {
                _effect = CreateEffectCore(
                    device,
                    effectInfo.Guid,
                    EffectFlags.ObjectIds,
                    actuatorIds);
                return;
            }
            catch (Exception ex)
            {
                objectIdError = ex;
            }
        }

        try
        {
            _effect = CreateEffectCore(
                device,
                effectInfo.Guid,
                EffectFlags.ObjectOffsets,
                new[] { _steeringAxisOffset });
        }
        catch (Exception offsetError)
        {
            var detail = objectIdError is null
                ? offsetError.Message
                : $"{objectIdError.Message} / {offsetError.Message}";

            throw new InvalidOperationException(
                $"CreateEffect failed for '{device.DeviceInfo.ProductName}' " +
                $"(steering offset {_steeringAxisOffset}, actuators {actuatorIds.Length}): {detail}",
                objectIdError is null ? offsetError : new AggregateException(objectIdError, offsetError));
        }
    }

    private IDirectInputEffect CreateEffectCore(
        IDirectInputDevice8 device,
        Guid effectGuid,
        EffectFlags axisFlags,
        int[] axes)
    {
        var parameters = new EffectParameters
        {
            Flags = EffectFlags.Cartesian | axisFlags,
            Duration = int.MaxValue,
            SamplePeriod = 0,
            Gain = _deviceGain,
            TriggerButton = -1,
            TriggerRepeatInterval = int.MaxValue,
            Axes = axes,
            Directions = new int[axes.Length],
            Parameters = new ConstantForce { Magnitude = 0 },
        };

        return device.CreateEffect(effectGuid, parameters);
    }

    private static int[] FindForceFeedbackActuatorIds(IDirectInputDevice8 device)
    {
        var actuators = device
            .GetObjects()
            .Where(obj => obj.ObjectId.Flags.HasFlag(DeviceObjectTypeFlags.ForceFeedbackActuator))
            .ToList();

        if (actuators.Count == 0)
        {
            return Array.Empty<int>();
        }

        return actuators
            .OrderByDescending(obj =>
                obj.ObjectType == ObjectGuid.XAxis
                || (obj.UsagePage == 0x01 && obj.Usage == 0x30))
            .Select(obj => (int)obj.ObjectId)
            .ToArray();
    }

    private void SetMagnitudeCore(int magnitude)
    {
        if (_effect is null)
        {
            return;
        }

        var max = Math.Clamp(_options.MaxForce, 0, 10000);
        var value = Math.Clamp(magnitude, -max, max);
        if (_options.InvertForce)
        {
            value = -value;
        }

        _effect.SetParameters(
            new EffectParameters { Parameters = new ConstantForce { Magnitude = value } },
            EffectParameterFlags.TypeSpecificParameters);
    }

    private void StopConstantForceCore()
    {
        if (_effect is null)
        {
            return;
        }

        try
        {
            SetMagnitudeCore(0);
            if (_isPlaying)
            {
                Check(_effect.Stop(), "Effect.Stop");
            }
        }
        finally
        {
            _isPlaying = false;
        }
    }

    private void SendForceFeedback(ForceFeedbackCommand command)
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            _device.SendForceFeedbackCommand(command);
        }
        catch
        {
            // Not every driver implements every force-feedback command.
        }
    }

    private void EnsureAcquired()
    {
        if (_device is null)
        {
            return;
        }

        var acquired = _device.Acquire();
        if (acquired.Failure)
        {
            RaiseDisconnected($"Acquire failed: {acquired.Description}");
        }
    }

    private bool TryReacquire()
    {
        if (_device is null)
        {
            return false;
        }

        try
        {
            return !_device.Acquire().Failure;
        }
        catch
        {
            return false;
        }
    }

    private void RaiseDisconnected(string reason)
    {
        if (_disconnectedRaised)
        {
            return;
        }

        _disconnectedRaised = true;
        _isPlaying = false;
        Disconnected?.Invoke(this, reason);
    }

    private static SelectedDevice SelectDevice(IDirectInput8 directInput, DirectInputOptions options)
    {
        var candidates = EnumerateCandidates(directInput)
            .Where(instance => !IsVirtualDevice(instance))
            .ToList();

        if (options.DeviceGuid is { } guid)
        {
            candidates = candidates
                .Where(instance => instance.InstanceGuid == guid)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(options.DeviceName))
        {
            candidates = candidates
                .Where(instance => MatchesName(instance, options.DeviceName))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No matching physical DirectInput device found. " +
                "Check --device/--guid, the wheel's driver, and its native/PC mode.");
        }

        foreach (var instance in candidates)
        {
            IDirectInputDevice8? device = null;
            try
            {
                device = directInput.CreateDevice(instance.InstanceGuid);
                if (!device.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback))
                {
                    device.Dispose();
                    continue;
                }

                return new SelectedDevice(device);
            }
            catch
            {
                device?.Dispose();
            }
        }

        var names = string.Join(", ", candidates.Select(c => c.ProductName));
        throw new InvalidOperationException(
            $"No force-feedback device among: {names}. " +
            "Install the vendor driver, use the wheel's native/PC mode, or choose another backend.");
    }

    private static IEnumerable<DeviceInstance> EnumerateCandidates(IDirectInput8 directInput)
    {
        var seen = new HashSet<Guid>();
        foreach (var type in new[] { DeviceType.Driving, DeviceType.Joystick })
        {
            IList<DeviceInstance> devices;
            try
            {
                devices = directInput.GetDevices(type, DeviceEnumerationFlags.AttachedOnly);
            }
            catch
            {
                continue;
            }

            foreach (var instance in devices)
            {
                if (seen.Add(instance.InstanceGuid))
                {
                    yield return instance;
                }
            }
        }
    }

    private static bool IsVirtualDevice(DeviceInstance instance)
    {
        var text = $"{instance.ProductName} {instance.InstanceName}";
        return text.Contains("PIDAL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PadForge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesName(DeviceInstance instance, string name)
    {
        return instance.ProductName.Contains(name, StringComparison.OrdinalIgnoreCase)
            || instance.InstanceName.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    private static int DiscoverSteeringAxisOffset(IDirectInputDevice8 device)
    {
        var axes = device.GetObjects(DeviceObjectTypeFlags.Axis);
        var steering = axes.FirstOrDefault(axis =>
            axis.UsagePage == 0x01 && axis.Usage == 0x30);

        if (steering is not null)
        {
            return steering.Offset;
        }

        return axes.Count > 0 ? axes[0].Offset : 0;
    }

    private static uint PackButtons(IReadOnlyList<bool> buttons)
    {
        uint mask = 0;
        var count = Math.Min(32, buttons.Count);
        for (var i = 0; i < count; i++)
        {
            if (buttons[i])
            {
                mask |= 1u << i;
            }
        }

        return mask;
    }

    private static int ConvertPov(IReadOnlyList<int> pointOfViewControllers)
    {
        if (pointOfViewControllers.Count == 0)
        {
            return -1;
        }

        var value = pointOfViewControllers[0];
        if (value < 0)
        {
            return -1;
        }

        var normalized = ((value % 36000) + 36000) % 36000;
        return (int)Math.Round(normalized / 4500.0) % 8;
    }

    private static void Check(SharpGen.Runtime.Result result, string operation)
    {
        if (result.Failure)
        {
            throw new InvalidOperationException($"{operation} failed: {result.Description}");
        }
    }

    private sealed record SelectedDevice(IDirectInputDevice8 Device);
}

internal static class NativeMethods
{
    [DllImport("winmm.dll")]
    internal static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    internal static extern uint timeEndPeriod(uint period);
}
