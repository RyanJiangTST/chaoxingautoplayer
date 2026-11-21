namespace ChaoxingAutoPlayer.Services;

public sealed record CourseLink(
    Uri Url,
    string CourseId,
    string ChapterId,
    string ClassId,
    string CourseKey);

public static class CourseUrlParser
{
    public static bool TryParse(string? input, out CourseLink link)
    {
        link = null!;
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("chaoxing.com", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        var courseId = Get(query, "courseid", "courseId");
        var chapterId = Get(query, "chapterid", "chapterId", "knowledgeid");
        var classId = Get(query, "clazzid", "classid", "classId");
        var key = !string.IsNullOrWhiteSpace(courseId)
            ? $"course:{courseId}:{classId}"
            : $"url:{uri.Host}{uri.AbsolutePath}";

        link = new CourseLink(uri, courseId, chapterId, classId, key);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }
        }

        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> query, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (query.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
