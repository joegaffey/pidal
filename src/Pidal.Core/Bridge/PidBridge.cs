using System.Collections.Concurrent;
using System.Diagnostics;
using Pidal.Core.Abstractions;
using Pidal.Core.Diagnostics;
using Pidal.Core.Pid;

namespace Pidal.Core.Bridge;

/// <summary>
/// Connects the virtual PID wheel to the physical backend. Output reports are
/// parsed and applied; physical input is polled and forwarded northbound.
/// </summary>
public sealed class PidBridge : IDisposable
{
    private readonly IFfbBackend _backend;
    private readonly IVirtualWheel _virtualWheel;
    private readonly BridgeOptions _options;
    private readonly IPidalLog _log;
    private readonly PidEffectState _effects = new();
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _backendQueue = new(new ConcurrentQueue<Action>());

    private Timer? _watchdog;
    private Thread? _inputThread;
    private Thread? _outputThread;
    private volatile bool _running;
    private long _lastOutputTimestamp;
    private bool _actuatorsEnabled = true;
    private bool _paused;
    private bool _started;
    private bool _stopped;
    private bool _disposed;
    private readonly PidDeviceControl[] _controlSequence = new PidDeviceControl[3];
    private int _controlCount;
    private bool _reversePidUsageOrder;

    public PidBridge(
        IFfbBackend backend,
        IVirtualWheel virtualWheel,
        BridgeOptions options,
        IPidalLog log)
    {
        _backend = backend;
        _virtualWheel = virtualWheel;
        _options = options;
        _log = log;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        if (_stopped)
        {
            throw new InvalidOperationException("The bridge has already been stopped.");
        }

        _virtualWheel.OutputReceived += OnOutputReceived;
        _backend.Disconnected += OnBackendDisconnected;

        _backend.Open();
        _virtualWheel.Start();

        Interlocked.Exchange(ref _lastOutputTimestamp, Stopwatch.GetTimestamp());
        _running = true;

        _inputThread = new Thread(InputLoop)
        {
            IsBackground = true,
            Name = "PIDAL input poll",
        };
        _inputThread.Start();

        _outputThread = new Thread(OutputLoop)
        {
            IsBackground = true,
            Name = "PIDAL output apply",
        };
        _outputThread.Start();

        if (_options.WatchdogMs > 0)
        {
            var period = Math.Max(50, _options.WatchdogCheckMs);
            _watchdog = new Timer(OnWatchdog, null, period, period);
        }

        _started = true;
        PublishState();
        _log.Info($"Bridge started: {_virtualWheel.ProductName} -> {_backend.Name}.");
    }

    public void Stop()
    {
        if (_disposed || _stopped)
        {
            return;
        }

        _stopped = true;
        _started = false;
        _running = false;
        _watchdog?.Dispose();
        _watchdog = null;

        _inputThread?.Join(TimeSpan.FromSeconds(2));
        _inputThread = null;

        _backendQueue.CompleteAdding();
        _outputThread?.Join(TimeSpan.FromSeconds(10));
        _outputThread = null;

        _virtualWheel.OutputReceived -= OnOutputReceived;
        _backend.Disconnected -= OnBackendDisconnected;

        lock (_gate)
        {
            try
            {
                _backend.StopConstantForce();
            }
            catch (Exception ex)
            {
                _log.Debug($"Stop on shutdown failed: {ex.Message}");
            }

            try
            {
                _backend.Close();
            }
            catch (Exception ex)
            {
                _log.Debug($"Backend close failed: {ex.Message}");
            }

            try
            {
                _virtualWheel.Stop();
            }
            catch (Exception ex)
            {
                _log.Debug($"Virtual wheel stop failed: {ex.Message}");
            }
        }

        _log.Info("Bridge stopped.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _virtualWheel.Dispose();
        _backend.Dispose();
    }

    private void OutputLoop()
    {
        foreach (var action in _backendQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.Error("PID command failed.", ex);
            }
        }
    }

    private void InputLoop()
    {
        var hz = Math.Max(1, _options.InputPollHz);
        var intervalMs = Math.Max(1, 1000 / hz);

        while (_running)
        {
            try
            {
                var frame = _backend.PollInput();
                if (frame is { } value)
                {
                    _virtualWheel.SubmitInput(value);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Input poll failed.", ex);
            }

            Thread.Sleep(intervalMs);
        }
    }

    private void OnOutputReceived(object? sender, PidOutputPacket packet)
    {
        if (!_running)
        {
            return;
        }

        Interlocked.Exchange(ref _lastOutputTimestamp, Stopwatch.GetTimestamp());
        _log.Debug(
            $"PID report 0x{packet.ReportId:X2} source={packet.Source} " +
            $"len={packet.Data.Length}.");

        PidCommand command;
        try
        {
            command = PidReportParser.Parse(packet);
        }
        catch (Exception ex)
        {
            _log.Error($"PID parse failed for report 0x{packet.ReportId:X2}.", ex);
            return;
        }

        try
        {
            _backendQueue.Add(() =>
            {
                lock (_gate)
                {
                    Apply(command);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The bridge is stopping.
        }
    }

    private void Apply(PidCommand command)
    {
        switch (command)
        {
            case PidSetEffectCommand setEffect:
                ApplySetEffect(setEffect);
                break;

            case PidSetConstantForceCommand constantForce:
                ApplyConstantForce(constantForce);
                break;

            case PidEffectOperationCommand operation:
                ApplyEffectOperation(operation);
                break;

            case PidBlockFreeCommand blockFree:
                ApplyBlockFree(blockFree);
                break;

            case PidDeviceControlCommand deviceControl:
                ApplyDeviceControl(deviceControl);
                break;

            case PidDeviceGainCommand deviceGain:
                _backend.SetDeviceGain(deviceGain.Gain);
                _log.Debug($"Device gain {deviceGain.Gain}.");
                break;

            case PidCreateNewEffectCommand:
                ApplyCreateNewEffect();
                break;

            case PidIgnoredCommand ignored:
                _log.Debug($"Ignored PID report 0x{ignored.ReportId:X2} from {ignored.Source}.");
                break;

            case PidMalformedCommand malformed:
                _log.Warn($"Malformed PID report 0x{malformed.ReportId:X2}: {malformed.Reason}");
                break;
        }
    }

    private void ApplySetEffect(PidSetEffectCommand command)
    {
        var block = _effects.GetOrCreate(command.EffectBlockIndex);
        block.EffectType = command.EffectType;
        block.Duration = command.Duration;
        block.TriggerRepeatInterval = command.TriggerRepeatInterval;
        block.SamplePeriod = command.SamplePeriod;
        block.StartDelay = command.StartDelay;
        block.Gain = command.Gain;
        block.TriggerButton = command.TriggerButton;
        block.AxesEnable = command.AxesEnable;
        block.DirectionEnable = command.DirectionEnable;
        block.Direction = command.Direction;
        _effects.SetActive(command.EffectBlockIndex);

        _log.Debug(
            $"Set Effect EBI={command.EffectBlockIndex} type={command.EffectType} " +
            $"duration={command.Duration} gain={command.Gain}.");
    }

    private void ApplyConstantForce(PidSetConstantForceCommand command)
    {
        // Some clients (including webhid-wheel) use EBI 1 without a Create New
        // Effect / Set Effect when the descriptor has no Block Load Feature
        // report. PidEffectBlock defaults to constant force, so that path is
        // accepted here.
        var block = _effects.GetOrCreate(command.EffectBlockIndex);
        block.Magnitude = command.Magnitude;
        _effects.SetActive(command.EffectBlockIndex);

        if (!block.IsConstantForce)
        {
            _log.Debug($"Ignoring constant force for non-constant EBI={command.EffectBlockIndex}.");
            return;
        }

        if (block.IsPlaying)
        {
            _backend.UpdateConstantForce(ScaleMagnitude(command.Magnitude, block.Gain));
        }

        _log.Debug($"Constant force EBI={command.EffectBlockIndex} magnitude={command.Magnitude}.");
    }

    private void ApplyEffectOperation(PidEffectOperationCommand command)
    {
        var operation = _reversePidUsageOrder
            ? ReverseEffectOperation(command.Operation)
            : command.Operation;

        var block = _effects.GetOrCreate(command.EffectBlockIndex);
        if (operation is PidEffectOperation.Start or PidEffectOperation.StartSolo)
        {
            if (!block.IsConstantForce)
            {
                _log.Warn($"Effect EBI={command.EffectBlockIndex} is not constant force; not rendered.");
                return;
            }

            block.IsPlaying = true;
            _effects.SetActive(command.EffectBlockIndex);
            _backend.StartConstantForce(ScaleMagnitude(block.Magnitude, block.Gain));
            PublishState();
            _log.Debug($"Start EBI={command.EffectBlockIndex} magnitude={block.Magnitude}.");
            return;
        }

        block.IsPlaying = false;
        if (_effects.ActiveIndex == command.EffectBlockIndex)
        {
            _backend.StopConstantForce();
        }

        PublishState();
        _log.Debug($"Stop EBI={command.EffectBlockIndex}.");
    }

    private void ApplyBlockFree(PidBlockFreeCommand command)
    {
        var wasActive = _effects.ActiveIndex == command.EffectBlockIndex;
        var removed = _effects.Remove(command.EffectBlockIndex);
        if (removed is null)
        {
            return;
        }

        if (wasActive)
        {
            _backend.StopConstantForce();
        }

        PublishState();
        _log.Debug($"Block free EBI={command.EffectBlockIndex}.");
    }

    private void ApplyDeviceControl(PidDeviceControlCommand command)
    {
        DetectReversedUsageOrder(command.Control);

        var control = _reversePidUsageOrder
            ? ReverseDeviceControl(command.Control)
            : command.Control;

        _backend.HandleDeviceControl(control);

        if (control is PidDeviceControl.DisableActuators
            or PidDeviceControl.StopAllEffects
            or PidDeviceControl.Reset
            or PidDeviceControl.Pause)
        {
            _backend.StopConstantForce();
        }

        switch (control)
        {
            case PidDeviceControl.EnableActuators:
                _actuatorsEnabled = true;
                _paused = false;
                break;

            case PidDeviceControl.DisableActuators:
                _actuatorsEnabled = false;
                _paused = false;
                _effects.Clear();
                break;

            case PidDeviceControl.StopAllEffects:
                _paused = false;
                _effects.Clear();
                break;

            case PidDeviceControl.Reset:
                _actuatorsEnabled = true;
                _paused = false;
                _effects.Clear();
                break;

            case PidDeviceControl.Pause:
                _paused = true;
                break;

            case PidDeviceControl.Continue:
                _paused = false;
                break;
        }

        PublishState();
        _log.Debug($"Device control {control}.");
    }

    private void DetectReversedUsageOrder(PidDeviceControl control)
    {
        if (_reversePidUsageOrder || _controlCount >= _controlSequence.Length)
        {
            return;
        }

        _controlSequence[_controlCount++] = control;

        if (_controlCount == _controlSequence.Length
            && _controlSequence[0] == PidDeviceControl.StopAllEffects
            && _controlSequence[1] == PidDeviceControl.Reset
            && _controlSequence[2] == PidDeviceControl.Continue)
        {
            _reversePidUsageOrder = true;
            _log.Warn(
                "Detected reversed PID usage-array order (webhid-wheel); " +
                "enabling Effect Operation and Device Control compatibility mapping.");
        }
    }

    private static PidEffectOperation ReverseEffectOperation(PidEffectOperation operation)
    {
        return operation switch
        {
            PidEffectOperation.Start => PidEffectOperation.Stop,
            PidEffectOperation.Stop => PidEffectOperation.Start,
            _ => operation,
        };
    }

    private static PidDeviceControl ReverseDeviceControl(PidDeviceControl control)
    {
        return control switch
        {
            PidDeviceControl.EnableActuators => PidDeviceControl.Continue,
            PidDeviceControl.DisableActuators => PidDeviceControl.Pause,
            PidDeviceControl.StopAllEffects => PidDeviceControl.Reset,
            PidDeviceControl.Reset => PidDeviceControl.StopAllEffects,
            PidDeviceControl.Pause => PidDeviceControl.DisableActuators,
            PidDeviceControl.Continue => PidDeviceControl.EnableActuators,
            _ => control,
        };
    }

    private void ApplyCreateNewEffect()
    {
        var ebi = _virtualWheel.GetLastAllocatedEffectBlockIndex();
        if (ebi is > 0)
        {
            _effects.GetOrCreate(ebi.Value);
            _log.Debug($"Create New Effect allocated EBI={ebi.Value}.");
        }
        else
        {
            _log.Debug("Create New Effect received without an allocated EBI.");
        }
    }

    private void OnWatchdog(object? state)
    {
        if (!_running || _options.WatchdogMs <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var active = _effects.Active;
            if (active is not { IsPlaying: true })
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(
                Interlocked.Read(ref _lastOutputTimestamp));

            if (elapsed.TotalMilliseconds < _options.WatchdogMs)
            {
                return;
            }

            _log.Warn(
                $"No PID output for {elapsed.TotalMilliseconds:F0} ms; stopping EBI={active.Index}.");

            active.IsPlaying = false;
            _backend.StopConstantForce();
            PublishState();
        }
    }

    private void OnBackendDisconnected(object? sender, string reason)
    {
        lock (_gate)
        {
            _log.Warn($"Physical backend disconnected: {reason}");
            foreach (var block in _effects.Blocks)
            {
                block.IsPlaying = false;
            }

            PublishState();
        }
    }

    private static int ScaleMagnitude(short magnitude, byte gain)
    {
        var scaled = (int)Math.Round(magnitude * (gain / 255.0));
        return Math.Clamp(scaled, -10000, 10000);
    }

    private void PublishState()
    {
        var active = _effects.Active;
        var state = new PidState(
            EffectBlockIndex: _effects.ActiveIndex,
            ActuatorsEnabled: _actuatorsEnabled,
            EffectPlaying: active?.IsPlaying ?? false,
            Paused: _paused,
            SafetySwitch: false);

        _virtualWheel.PublishPidState(state);
    }
}
