namespace ChaoxingAutoPlayer.Automation;

public static class TaskQueuePlanner
{
    public static TaskPoint? FindNext(
        IReadOnlyList<TaskPoint> videos,
        string selectedTaskKey,
        string endedTaskKey,
        IReadOnlySet<string> completed,
        IReadOnlySet<string> attempted)
    {
        if (videos.Count == 0)
        {
            return null;
        }

        var selectedIndex = videos
            .Select((task, index) => (task, index))
            .FirstOrDefault(item =>
                string.Equals(item.task.Key, selectedTaskKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.task.Key, endedTaskKey, StringComparison.OrdinalIgnoreCase))
            .index;

        for (var offset = 1; offset <= videos.Count; offset++)
        {
            var candidate = videos[(selectedIndex + offset) % videos.Count];
            if (!completed.Contains(candidate.Key)
                && !string.Equals(candidate.Key, endedTaskKey, StringComparison.OrdinalIgnoreCase)
                && !attempted.Contains(candidate.Key))
            {
                return candidate;
            }
        }

        return null;
    }
}
