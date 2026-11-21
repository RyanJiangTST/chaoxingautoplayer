using System.Text;
using System.Text.Json;
using ChaoxingAutoPlayer.Models;

namespace ChaoxingAutoPlayer.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public SettingsStore(string? path = null) => Path = path ?? AppPaths.SettingsFile;

    public string Path { get; }

    public PlayerSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new PlayerSettings();
            }

            var settings = JsonSerializer.Deserialize<PlayerSettings>(
                               File.ReadAllText(Path, Encoding.UTF8),
                               JsonOptions)
                           ?? new PlayerSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Settings could not be loaded: {ex.Message}");
            return new PlayerSettings();
        }
    }

    public void Save(PlayerSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, Path, overwrite: true);
    }
}
