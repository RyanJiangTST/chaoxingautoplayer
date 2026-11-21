namespace ChaoxingAutoPlayer.Models;

public sealed class PlaybackCheckpoint
{
    public int Version { get; set; } = 1;

    public string EntryUrl { get; set; } = string.Empty;

    public string CourseKey { get; set; } = string.Empty;

    public string TaskKey { get; set; } = string.Empty;

    public string TaskTitle { get; set; } = string.Empty;

    public double CurrentTime { get; set; }

    public double Duration { get; set; }

    public List<string> CompletedTaskKeys { get; set; } = new();

    public List<string> SkippedTaskKeys { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
