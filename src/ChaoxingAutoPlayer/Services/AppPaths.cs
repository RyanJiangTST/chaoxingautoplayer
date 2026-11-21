namespace ChaoxingAutoPlayer.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChaoxingAutoPlayer");

    public static string WebViewData => Path.Combine(Root, "WebView2");

    public static string Logs => Path.Combine(Root, "logs");

    public static string LogFile => Path.Combine(Logs, "app.log");

    public static string StateFile => Path.Combine(Root, "state.json");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string SelfDisciplineLockFile
    {
        get
        {
            var stateRoot = Environment.GetEnvironmentVariable("SELFDISCIPLINE_STATE_DIR");
            return Path.Combine(
                string.IsNullOrWhiteSpace(stateRoot)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SelfDiscipline")
                    : stateRoot,
                "lock-state.json");
        }
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WebViewData);
        Directory.CreateDirectory(Logs);
    }
}
