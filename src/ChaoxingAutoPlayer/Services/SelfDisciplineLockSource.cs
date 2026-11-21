using System.Text.Json;

namespace ChaoxingAutoPlayer.Services;

public sealed class SelfDisciplineLockSource : IDisposable
{
    private readonly string _path;
    private readonly Timer _timer;
    private bool _active;
    private bool _disposed;

    public SelfDisciplineLockSource(string? path = null)
    {
        _path = path ?? AppPaths.SelfDisciplineLockFile;
        _active = TryReadFullscreenLock(_path);
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public event EventHandler<bool>? FullscreenLockChanged;

    public bool IsFullscreenLocked => _active;

    public static bool ParseFullscreenLock(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetProperty(root, "Active", out var activeElement)
                || activeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !activeElement.GetBoolean()
                || !TryGetProperty(root, "Mode", out var modeElement))
            {
                return false;
            }

            return modeElement.ValueKind switch
            {
                JsonValueKind.Number when modeElement.TryGetInt32(out var number) => number == 1,
                JsonValueKind.String => string.Equals(modeElement.GetString(), "Lock", StringComparison.OrdinalIgnoreCase)
                                        || modeElement.GetString() == "1",
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadFullscreenLock(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return ParseFullscreenLock(reader.ReadToEnd());
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var active = TryReadFullscreenLock(_path);
        if (active == _active)
        {
            return;
        }

        _active = active;
        FullscreenLockChanged?.Invoke(this, active);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
