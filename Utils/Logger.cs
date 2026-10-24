namespace TwitterNotifyBot.Utils;

public static class Logger
{
    private static readonly object Sync = new();

    public static void Info(string message, ConsoleColor? color = null)
    {
        Write("INFO", message, null, color);
    }

    public static void Warn(string message, ConsoleColor? color = null)
    {
        Write("WARN", message, null, color);
    }

    public static void Warn(Exception ex, string message, ConsoleColor? color = null)
    {
        Write("WARN", message, ex, color);
    }

    public static void Error(string message, ConsoleColor? color = null)
    {
        Write("ERROR", message, null, color);
    }

    public static void Error(Exception ex, string message, ConsoleColor? color = null)
    {
        Write("ERROR", message, ex, color);
    }

    private static void Write(string level, string message, Exception? ex, ConsoleColor? color)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var output = ex is null ? message : $"{message}{Environment.NewLine}{ex}";

        lock (Sync)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color ?? GetDefaultColor(level);
            try
            {
                Console.WriteLine($"{timestamp} [{level}] {output}");
            }
            finally
            {
                Console.ForegroundColor = previousColor;
            }
        }
    }

    private static ConsoleColor GetDefaultColor(string level)
    {
        return level switch
        {
            "INFO" => ConsoleColor.Green,
            "WARN" => ConsoleColor.Yellow,
            "ERROR" => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
    }
}
