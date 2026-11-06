using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pidal.App;

internal static class Elevation
{
    public static bool IsAdministrator()
    {
        return NativeMethods.IsUserAnAdmin();
    }

    public static bool TryRelaunchElevated(string[] args)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return false;
        }

        var fileName = processPath;
        var arguments = args;

        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (string.IsNullOrEmpty(assemblyPath))
            {
                return false;
            }

            arguments = new[] { assemblyPath }.Concat(args).ToArray();
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = string.Join(" ", arguments.Select(Quote)),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the UAC prompt.
            return false;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsUserAnAdmin();
    }
}
