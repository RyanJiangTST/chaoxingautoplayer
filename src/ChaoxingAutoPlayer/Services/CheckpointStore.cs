using System.Text;
using System.Text.Json;
using ChaoxingAutoPlayer.Models;

namespace ChaoxingAutoPlayer.Services;

public sealed class CheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public CheckpointStore(string? path = null) => Path = path ?? AppPaths.StateFile;

    public string Path { get; }

    public PlaybackCheckpoint Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new PlaybackCheckpoint();
            }

            return JsonSerializer.Deserialize<PlaybackCheckpoint>(
                       File.ReadAllText(Path, Encoding.UTF8),
                       JsonOptions)
                   ?? new PlaybackCheckpoint();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Checkpoint could not be loaded: {ex.Message}");
            return new PlaybackCheckpoint();
        }
    }

    public void Save(PlaybackCheckpoint checkpoint)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        checkpoint.UpdatedAt = DateTimeOffset.Now;

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(checkpoint, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, Path, overwrite: true);
    }
}
