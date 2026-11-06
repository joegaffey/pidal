using Pidal.Core.Diagnostics;

namespace Pidal.App;

internal sealed class ConsoleLog : IPidalLog
{
    private readonly bool _verbose;

    public ConsoleLog(bool verbose)
    {
        _verbose = verbose;
    }

    public void Debug(string message)
    {
        if (_verbose)
        {
            Write("DBG", message, ConsoleColor.DarkGray);
        }
    }

    public void Info(string message)
    {
        Write("INF", message, ConsoleColor.Gray);
    }

    public void Warn(string message)
    {
        Write("WRN", message, ConsoleColor.Yellow);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERR", message, ConsoleColor.Red);
        if (exception is not null)
        {
            Console.Error.WriteLine(exception);
        }
    }

    private static void Write(string level, string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
