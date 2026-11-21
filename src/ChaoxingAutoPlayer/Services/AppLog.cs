using System.Text;
using System.Text.RegularExpressions;

namespace ChaoxingAutoPlayer.Services;

public static partial class AppLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception}");

    public static string Redact(string text) =>
        SensitiveQueryRegex().Replace(text, "$1=<redacted>");

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Redact(message)}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never stop the assistant.
        }
    }

    [GeneratedRegex(@"(?i)(enc|openc|token|ticket|sign|ak)=([^&\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryRegex();
}
