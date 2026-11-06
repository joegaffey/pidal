using Pidal.Core.Abstractions;
using Pidal.Core.Bridge;
using Pidal.Core.Diagnostics;
using Pidal.DirectInput;
using Pidal.Virtual;

namespace Pidal.Gui;

public sealed class MainForm : Form
{
    private readonly ComboBox _deviceCombo = new();
    private readonly Button _refreshButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();

    private readonly NumericUpDown _forceValue = new();
    private readonly Button _applyForceButton = new();
    private readonly Button _stopForceButton = new();

    private readonly ProgressBar _steeringBar = new();
    private readonly ProgressBar _throttleBar = new();
    private readonly ProgressBar _brakeBar = new();
    private readonly ProgressBar _clutchBar = new();
    private readonly Label _buttonsLabel = new();
    private readonly Label _hatLabel = new();

    private readonly NumericUpDown _gainValue = new();
    private readonly CheckBox _invertCheck = new();
    private readonly NumericUpDown _watchdogValue = new();
    private readonly NumericUpDown _pollHzValue = new();
    private readonly ComboBox _usageOrderCombo = new();
    private readonly CheckBox _verboseCheck = new();

    private readonly TextBox _logBox = new();
    private readonly Button _clearLogButton = new();

    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _inputTimer = new();

    private readonly List<DirectInputDeviceInfo> _devices = new();

    private PidBridge? _bridge;
    private DirectInputBackend? _backend;
    private HidMaestroVirtualWheel? _virtualWheel;
    private GuiLog? _log;

    private WheelInputFrame _latestInput;
    private int _hasInput;
    private bool _starting;
    private bool _closing;

    public MainForm()
    {
        Text = "PIDAL";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 560);
        Size = new Size(820, 640);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        BuildTray();

        _inputTimer.Interval = 33;
        _inputTimer.Tick += (_, _) => UpdateInputDisplay();
        _inputTimer.Start();

        Shown += async (_, _) => await RefreshDevicesAsync();
        FormClosing += OnFormClosing;
        UpdateControls();
    }

    private void BuildUi()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6),
        };

        tabs.TabPages.Add(BuildBridgeTab());
        tabs.TabPages.Add(BuildInputTab());
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildLogTab());

        Controls.Add(tabs);
    }

    private TabPage BuildBridgeTab()
    {
        var page = new TabPage("Bridge");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var deviceGroup = new GroupBox
        {
            Text = "Physical wheel",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
        };
        var deviceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
        };
        deviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _deviceCombo.Dock = DockStyle.Fill;
        _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _refreshButton.Text = "Refresh";
        _refreshButton.AutoSize = true;
        _refreshButton.Click += async (_, _) => await RefreshDevicesAsync();
        deviceLayout.Controls.Add(_deviceCombo, 0, 0);
        deviceLayout.Controls.Add(_refreshButton, 1, 0);
        deviceGroup.Controls.Add(deviceLayout);

        var bridgeGroup = new GroupBox
        {
            Text = "Bridge",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
        };
        var bridgeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
        };
        bridgeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bridgeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _startButton.Text = "Start bridge";
        _startButton.AutoSize = true;
        _startButton.Click += async (_, _) => await StartBridgeAsync();
        _stopButton.Text = "Stop bridge";
        _stopButton.AutoSize = true;
        _stopButton.Click += async (_, _) => await StopBridgeAsync();

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
        };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_stopButton);

        _statusLabel.Text = "Stopped";
        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font(Font, FontStyle.Bold);
        _detailLabel.Text = "No bridge running.";
        _detailLabel.AutoSize = true;
        _detailLabel.MaximumSize = new Size(700, 0);

        bridgeLayout.Controls.Add(buttons, 0, 0);
        bridgeLayout.SetColumnSpan(buttons, 2);
        bridgeLayout.Controls.Add(_statusLabel, 0, 1);
        bridgeLayout.SetColumnSpan(_statusLabel, 2);
        bridgeLayout.Controls.Add(_detailLabel, 0, 2);
        bridgeLayout.SetColumnSpan(_detailLabel, 2);
        bridgeGroup.Controls.Add(bridgeLayout);

        var testGroup = new GroupBox
        {
            Text = "Direct force test",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
        };
        var testLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
        };
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _forceValue.Minimum = -10000;
        _forceValue.Maximum = 10000;
        _forceValue.Increment = 500;
        _forceValue.Value = 3000;
        _forceValue.Width = 100;

        _applyForceButton.Text = "Apply force";
        _applyForceButton.AutoSize = true;
        _applyForceButton.Click += (_, _) => _bridge?.ApplyTestForce((int)_forceValue.Value);

        _stopForceButton.Text = "Stop force";
        _stopForceButton.AutoSize = true;
        _stopForceButton.Click += (_, _) => _bridge?.StopTestForce();

        var note = new Label
        {
            Text = "Tests the physical DirectInput path directly, bypassing the browser.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };

        testLayout.Controls.Add(new Label { Text = "Magnitude", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        testLayout.Controls.Add(_forceValue, 1, 0);
        testLayout.Controls.Add(_applyForceButton, 2, 0);
        testLayout.Controls.Add(_stopForceButton, 3, 0);
        testLayout.Controls.Add(note, 0, 1);
        testLayout.SetColumnSpan(note, 4);
        testGroup.Controls.Add(testLayout);

        layout.Controls.Add(deviceGroup, 0, 0);
        layout.Controls.Add(bridgeGroup, 0, 1);
        layout.Controls.Add(testGroup, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildInputTab()
    {
        var page = new TabPage("Input");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddInputRow(layout, 0, "Steering", _steeringBar);
        AddInputRow(layout, 1, "Throttle", _throttleBar);
        AddInputRow(layout, 2, "Brake", _brakeBar);
        AddInputRow(layout, 3, "Clutch", _clutchBar);

        _buttonsLabel.Text = "Buttons: 0x00000000";
        _buttonsLabel.AutoSize = true;
        _hatLabel.Text = "Hat: centered";
        _hatLabel.AutoSize = true;

        layout.Controls.Add(new Label { Text = "Buttons", AutoSize = true }, 0, 4);
        layout.Controls.Add(_buttonsLabel, 1, 4);
        layout.Controls.Add(new Label { Text = "Hat", AutoSize = true }, 0, 5);
        layout.Controls.Add(_hatLabel, 1, 5);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Settings");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _gainValue.Minimum = 0;
        _gainValue.Maximum = 100;
        _gainValue.Value = 100;
        _gainValue.Width = 100;

        _invertCheck.Text = "Invert force direction";
        _invertCheck.AutoSize = true;

        _watchdogValue.Minimum = 0;
        _watchdogValue.Maximum = 60000;
        _watchdogValue.Increment = 500;
        _watchdogValue.Value = 5000;
        _watchdogValue.Width = 100;

        _pollHzValue.Minimum = 1;
        _pollHzValue.Maximum = 1000;
        _pollHzValue.Value = 250;
        _pollHzValue.Width = 100;

        _usageOrderCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _usageOrderCombo.Items.AddRange(new object[] { "Auto", "Standard", "Reversed" });
        _usageOrderCombo.SelectedIndex = 0;
        _usageOrderCombo.Width = 160;

        _verboseCheck.Text = "Verbose logging";
        _verboseCheck.AutoSize = true;
        _verboseCheck.CheckedChanged += (_, _) =>
        {
            if (_log is not null)
            {
                _log.Verbose = _verboseCheck.Checked;
            }
        };

        layout.Controls.Add(new Label { Text = "Gain (%)", AutoSize = true }, 0, 0);
        layout.Controls.Add(_gainValue, 1, 0);
        layout.Controls.Add(new Label { Text = "Force direction", AutoSize = true }, 0, 1);
        layout.Controls.Add(_invertCheck, 1, 1);
        layout.Controls.Add(new Label { Text = "Watchdog (ms)", AutoSize = true }, 0, 2);
        layout.Controls.Add(_watchdogValue, 1, 2);
        layout.Controls.Add(new Label { Text = "Input poll (Hz)", AutoSize = true }, 0, 3);
        layout.Controls.Add(_pollHzValue, 1, 3);
        layout.Controls.Add(new Label { Text = "PID usage order", AutoSize = true }, 0, 4);
        layout.Controls.Add(_usageOrderCombo, 1, 4);
        layout.Controls.Add(new Label { Text = "Logging", AutoSize = true }, 0, 5);
        layout.Controls.Add(_verboseCheck, 1, 5);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("Log");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Both;
        _logBox.WordWrap = false;
        _logBox.Font = new Font("Consolas", 9F);

        _clearLogButton.Text = "Clear";
        _clearLogButton.AutoSize = true;
        _clearLogButton.Anchor = AnchorStyles.Right;
        _clearLogButton.Click += (_, _) => _logBox.Clear();

        layout.Controls.Add(_logBox, 0, 0);
        layout.Controls.Add(_clearLogButton, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void BuildTray()
    {
        _trayIcon.Icon = SystemIcons.Application;
        _trayIcon.Text = "PIDAL";
        _trayIcon.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        menu.Items.Add("Start bridge", null, async (_, _) => await StartBridgeAsync());
        menu.Items.Add("Stop bridge", null, async (_, _) => await StopBridgeAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task RefreshDevicesAsync()
    {
        if (_closing || _bridge is not null || _starting)
        {
            return;
        }

        _refreshButton.Enabled = false;
        try
        {
            var devices = await Task.Run(DirectInputBackend.ListDevices);
            _devices.Clear();
            _devices.AddRange(devices);

            _deviceCombo.Items.Clear();
            foreach (var device in devices)
            {
                _deviceCombo.Items.Add(new DeviceItem(device));
            }

            var preferred = devices.FirstOrDefault(d => d.ForceFeedback && !d.IsVirtual)
                ?? devices.FirstOrDefault();

            if (preferred is not null)
            {
                for (var i = 0; i < _deviceCombo.Items.Count; i++)
                {
                    if (_deviceCombo.Items[i] is DeviceItem item
                        && item.Info.InstanceGuid == preferred.InstanceGuid)
                    {
                        _deviceCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            AppendLog("INF", $"Found {devices.Count} DirectInput device(s).");
        }
        catch (Exception ex)
        {
            AppendLog("ERR", $"Device enumeration failed: {ex.Message}");
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private async Task StartBridgeAsync()
    {
        if (_closing || _bridge is not null || _starting)
        {
            return;
        }

        _starting = true;
        UpdateControls();

        PidBridge? bridge = null;
        DirectInputBackend? backend = null;
        HidMaestroVirtualWheel? virtualWheel = null;

        try
        {
            var selected = (_deviceCombo.SelectedItem as DeviceItem)?.Info;

            var backendOptions = new DirectInputOptions
            {
                DeviceGuid = selected?.InstanceGuid,
                InvertForce = _invertCheck.Checked,
                Gain = (int)Math.Round((double)_gainValue.Value / 100.0 * 10000.0),
            };

            var bridgeOptions = new BridgeOptions
            {
                InputPollHz = (int)_pollHzValue.Value,
                WatchdogMs = (int)_watchdogValue.Value,
                PidUsageOrder = _usageOrderCombo.SelectedIndex switch
                {
                    1 => PidUsageOrder.Standard,
                    2 => PidUsageOrder.Reversed,
                    _ => PidUsageOrder.Auto,
                },
            };

            var log = new GuiLog(AppendLog) { Verbose = _verboseCheck.Checked };
            backend = new DirectInputBackend(backendOptions);
            virtualWheel = new HidMaestroVirtualWheel(new VirtualWheelOptions());
            bridge = new PidBridge(backend, virtualWheel, bridgeOptions, log);
            bridge.InputFrame += OnInputFrame;
            bridge.StatusChanged += OnStatusChanged;

            await Task.Run(bridge.Start);

            if (_closing)
            {
                bridge.Dispose();
                return;
            }

            _backend = backend;
            _virtualWheel = virtualWheel;
            _bridge = bridge;
            _log = log;
            AppendLog("INF", "Bridge started.");
        }
        catch (Exception ex)
        {
            AppendLog("ERR", $"Bridge start failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "PIDAL", MessageBoxButtons.OK, MessageBoxIcon.Error);

            if (bridge is not null)
            {
                bridge.InputFrame -= OnInputFrame;
                bridge.StatusChanged -= OnStatusChanged;
                bridge.Dispose();
            }
            else
            {
                backend?.Dispose();
                virtualWheel?.Dispose();
            }
        }
        finally
        {
            _starting = false;
            UpdateControls();
        }
    }

    private async Task StopBridgeAsync()
    {
        var bridge = _bridge;
        if (bridge is null)
        {
            return;
        }

        _bridge = null;
        _backend = null;
        _virtualWheel = null;
        _log = null;

        try
        {
            await Task.Run(bridge.Stop);
        }
        catch (Exception ex)
        {
            AppendLog("ERR", $"Bridge stop failed: {ex.Message}");
        }

        bridge.InputFrame -= OnInputFrame;
        bridge.StatusChanged -= OnStatusChanged;
        bridge.Dispose();
        AppendLog("INF", "Bridge stopped.");
        UpdateControls();
    }

    private void OnInputFrame(object? sender, WheelInputFrame frame)
    {
        _latestInput = frame;
        Interlocked.Exchange(ref _hasInput, 1);
    }

    private void OnStatusChanged(object? sender, BridgeStatus status)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => OnStatusChanged(sender, status)));
            }
            catch (InvalidOperationException)
            {
                // The form is closing.
            }

            return;
        }

        _statusLabel.Text = status.Running ? "Running" : "Stopped";
        _statusLabel.ForeColor = status.Running ? Color.DarkGreen : Color.DarkRed;
        _detailLabel.Text =
            $"{status.VirtualWheelName} -> {status.BackendName} | " +
            $"EBI {status.ActiveEffectBlock} | playing {status.EffectPlaying} | " +
            $"actuators {status.ActuatorsEnabled} | usage " +
            (status.ReverseUsageOrder ? "reversed" : "standard");

        _trayIcon.Text = status.Running ? "PIDAL - running" : "PIDAL - stopped";
        UpdateControls();
    }

    private void UpdateInputDisplay()
    {
        if (Interlocked.Exchange(ref _hasInput, 0) == 0)
        {
            return;
        }

        var frame = _latestInput;
        _steeringBar.Value = Math.Clamp(frame.Steering, 0, 65535);
        _throttleBar.Value = Math.Clamp(frame.Throttle, 0, 65535);
        _brakeBar.Value = Math.Clamp(frame.Brake, 0, 65535);
        _clutchBar.Value = Math.Clamp(frame.Clutch, 0, 65535);
        _buttonsLabel.Text = $"Buttons: 0x{frame.Buttons:X8}";
        _hatLabel.Text = frame.Hat < 0 ? "Hat: centered" : $"Hat: {frame.Hat}";
    }

    private void UpdateControls()
    {
        var running = _bridge is not null;
        _startButton.Enabled = !running && !_starting;
        _stopButton.Enabled = running && !_starting;
        _applyForceButton.Enabled = running;
        _stopForceButton.Enabled = running;
        _deviceCombo.Enabled = !running && !_starting;
        _refreshButton.Enabled = !running && !_starting;
    }

    private void AppendLog(string level, string message)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AppendLog(level, message)));
            }
            catch (InvalidOperationException)
            {
                // The form is closing.
            }

            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        _logBox.AppendText(line + Environment.NewLine);

        if (_logBox.TextLength > 250_000)
        {
            _logBox.Clear();
            _logBox.AppendText("[log truncated]" + Environment.NewLine);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _inputTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        try
        {
            _bridge?.Stop();
        }
        catch
        {
            // Best effort on exit.
        }

        _bridge?.Dispose();
        _bridge = null;
    }

    private static void AddInputRow(TableLayoutPanel layout, int row, string label, ProgressBar bar)
    {
        bar.Minimum = 0;
        bar.Maximum = 65535;
        bar.Dock = DockStyle.Fill;
        bar.Height = 18;

        layout.Controls.Add(
            new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left },
            0,
            row);
        layout.Controls.Add(bar, 1, row);
    }

    private sealed class DeviceItem
    {
        public DeviceItem(DirectInputDeviceInfo info)
        {
            Info = info;
        }

        public DirectInputDeviceInfo Info { get; }

        public override string ToString()
        {
            var flags = new List<string>();
            if (Info.ForceFeedback)
            {
                flags.Add("FFB");
            }

            if (Info.IsVirtual)
            {
                flags.Add("virtual");
            }

            var suffix = flags.Count == 0 ? string.Empty : $" [{string.Join(", ", flags)}]";
            return $"{Info.ProductName} ({Info.Type}){suffix}";
        }
    }

    private sealed class GuiLog : IPidalLog
    {
        private readonly Action<string, string> _write;

        public GuiLog(Action<string, string> write)
        {
            _write = write;
        }

        public bool Verbose { get; set; }

        public void Debug(string message)
        {
            if (Verbose)
            {
                _write("DBG", message);
            }
        }

        public void Info(string message) => _write("INF", message);

        public void Warn(string message) => _write("WRN", message);

        public void Error(string message, Exception? exception = null)
        {
            _write("ERR", message);
            if (exception is not null)
            {
                _write("ERR", exception.ToString());
            }
        }
    }
}
