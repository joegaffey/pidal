namespace Pidal.App;

internal sealed record AppOptions
{
    public bool ShowHelp { get; init; }

    public bool ListDevices { get; init; }

    public string? DeviceName { get; init; }

    public Guid? DeviceGuid { get; init; }

    public int? AxisOffset { get; init; }

    public bool Invert { get; init; }

    public int GainPercent { get; init; } = 100;

    public int WatchdogMs { get; init; } = 5000;

    public int PollHz { get; init; } = 250;

    public bool Verbose { get; init; }
}

internal static class CommandLine
{
    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options = options with { ShowHelp = true };
                    break;

                case "--list":
                    options = options with { ListDevices = true };
                    break;

                case "--device":
                    options = options with { DeviceName = Next(args, ref i, arg) };
                    break;

                case "--guid":
                    var guidText = Next(args, ref i, arg);
                    options = options with { DeviceGuid = Guid.Parse(guidText) };
                    break;

                case "--axis":
                    options = options with { AxisOffset = ParseInt(Next(args, ref i, arg), arg) };
                    break;

                case "--invert":
                    options = options with { Invert = true };
                    break;

                case "--gain":
                    var gain = ParseInt(Next(args, ref i, arg), arg);
                    if (gain is < 0 or > 100)
                    {
                        throw new ArgumentException("--gain must be between 0 and 100.");
                    }

                    options = options with { GainPercent = gain };
                    break;

                case "--watchdog-ms":
                    var watchdog = ParseInt(Next(args, ref i, arg), arg);
                    if (watchdog < 0)
                    {
                        throw new ArgumentException("--watchdog-ms cannot be negative.");
                    }

                    options = options with { WatchdogMs = watchdog };
                    break;

                case "--no-watchdog":
                    options = options with { WatchdogMs = 0 };
                    break;

                case "--poll-hz":
                    var poll = ParseInt(Next(args, ref i, arg), arg);
                    if (poll is < 1 or > 2000)
                    {
                        throw new ArgumentException("--poll-hz must be between 1 and 2000.");
                    }

                    options = options with { PollHz = poll };
                    break;

                case "-v":
                case "--verbose":
                    options = options with { Verbose = true };
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            PIDAL - PID Abstraction Layer

            Usage:
              Pidal.App.exe [options]

            Options:
              --list                 List DirectInput devices and exit.
              --device <name>        Select a device by product/instance name fragment.
              --guid <guid>          Select a device by DirectInput instance GUID.
              --axis <offset>        Steering axis data-format offset (default: X).
              --invert               Invert constant-force direction.
              --gain <0-100>         Force gain percentage (default: 100).
              --watchdog-ms <ms>     Stop force after no PID output (default: 5000).
              --no-watchdog          Disable the stale-output watchdog.
              --poll-hz <hz>         Physical input poll rate (default: 250).
              -v, --verbose          Verbose command logging.
              -h, --help             Show this help.

            The app must run elevated because HIDMaestro creates a virtual HID device.
            """);
    }

    private static string Next(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static int ParseInt(string text, string option)
    {
        if (!int.TryParse(text, out var value))
        {
            throw new ArgumentException($"{option} requires an integer, got '{text}'.");
        }

        return value;
    }
}
