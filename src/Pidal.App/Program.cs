using Pidal.Core.Bridge;
using Pidal.DirectInput;
using Pidal.Virtual;

namespace Pidal.App;

internal static class Program
{
    public static int Main(string[] args)
    {
        AppOptions options;
        try
        {
            options = CommandLine.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            CommandLine.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            CommandLine.PrintUsage();
            return 0;
        }

        if (options.ListDevices)
        {
            try
            {
                PrintDevices();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not list DirectInput devices: {ex.Message}");
                return 1;
            }
        }

        if (options.Diagnose)
        {
            try
            {
                PrintDiagnostics();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not diagnose DirectInput devices: {ex.Message}");
                return 1;
            }
        }

        var backendOptions = new DirectInputOptions
        {
            DeviceName = options.DeviceName,
            DeviceGuid = options.DeviceGuid,
            AxisOffset = options.AxisOffset,
            InvertForce = options.Invert,
            Gain = (int)Math.Round(options.GainPercent / 100.0 * 10000.0),
        };

        if (options.TestForce is { } testForce)
        {
            try
            {
                using var testBackend = new DirectInputBackend(backendOptions);
                testBackend.Open();
                Console.WriteLine(
                    $"Applying constant force {testForce} for {options.TestForceMs} ms...");
                testBackend.StartConstantForce(testForce);
                Thread.Sleep(options.TestForceMs);
                testBackend.StopConstantForce();
                testBackend.Close();
                Console.WriteLine("Force test complete.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Force test failed: {ex.Message}");
                return 1;
            }
        }

        if (!Elevation.IsAdministrator())
        {
            Console.WriteLine("Administrator elevation is required to create the virtual HID device.");
            Console.WriteLine("Relaunching elevated...");

            if (Elevation.TryRelaunchElevated(args))
            {
                return 0;
            }

            Console.Error.WriteLine("Elevation was cancelled. Run PIDAL from an elevated terminal.");
            return 1;
        }

        var log = new ConsoleLog(options.Verbose);
        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Set();
        };

        var bridgeOptions = new BridgeOptions
        {
            InputPollHz = options.PollHz,
            WatchdogMs = options.WatchdogMs,
        };

        var virtualOptions = new VirtualWheelOptions();

        using var backend = new DirectInputBackend(backendOptions);
        using var virtualWheel = new HidMaestroVirtualWheel(virtualOptions);
        using var bridge = new PidBridge(backend, virtualWheel, bridgeOptions, log);

        try
        {
            bridge.Start();
            Console.WriteLine();
            Console.WriteLine("PIDAL is running.");
            Console.WriteLine("  Browser: select 'PIDAL Virtual Wheel' in the WebHID device picker.");
            Console.WriteLine("  Stop:    press Ctrl+C.");
            Console.WriteLine();
            stop.Wait();
        }
        catch (Exception ex)
        {
            log.Error("PIDAL failed to start.", ex);
            return 1;
        }
        finally
        {
            bridge.Stop();
        }

        return 0;
    }

    private static void PrintDiagnostics()
    {
        var devices = DirectInputBackend.Diagnose();
        if (devices.Count == 0)
        {
            Console.WriteLine("No DirectInput devices found.");
            return;
        }

        foreach (var device in devices)
        {
            Console.WriteLine(
                $"{device.ProductName} ({device.InstanceName}) " +
                $"guid={device.InstanceGuid} type={device.Type} " +
                $"ffb={(device.ForceFeedback ? "yes" : "no")} " +
                $"virtual={(device.IsVirtual ? "yes" : "no")}");

            if (device.Error is not null)
            {
                Console.WriteLine($"  error: {device.Error}");
            }

            foreach (var axis in device.Axes)
            {
                Console.WriteLine(
                    $"  axis name='{axis.Name}' type={axis.ObjectType} " +
                    $"usage={axis.UsagePage:X2}:{axis.Usage:X2} offset={axis.Offset} " +
                    $"objectId=0x{axis.ObjectId:X8} flags={axis.ObjectIdFlags} " +
                    $"ffbActuator={(axis.ForceFeedbackActuator ? "yes" : "no")}");
            }

            foreach (var effect in device.Effects)
            {
                Console.WriteLine(
                    $"  effect name='{effect.Name}' type={effect.Type} guid={effect.Guid}");
            }
        }
    }

    private static void PrintDevices()
    {
        var devices = DirectInputBackend.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No DirectInput devices found.");
            return;
        }

        Console.WriteLine($"{"GUID",-38} {"FFB",-4} {"VIRTUAL",-7} {"TYPE",-10} NAME");
        foreach (var device in devices)
        {
            Console.WriteLine(
                $"{device.InstanceGuid,-38} " +
                $"{(device.ForceFeedback ? "yes" : "no"),-4} " +
                $"{(device.IsVirtual ? "yes" : "no"),-7} " +
                $"{device.Type,-10} " +
                $"{device.ProductName} ({device.InstanceName})");
        }
    }
}
