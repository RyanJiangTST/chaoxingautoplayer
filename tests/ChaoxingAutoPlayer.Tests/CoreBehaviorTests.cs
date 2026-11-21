using ChaoxingAutoPlayer.Automation;
using ChaoxingAutoPlayer.Models;
using ChaoxingAutoPlayer.Services;

namespace ChaoxingAutoPlayer.Tests;

public class CoreBehaviorTests
{
    [Theory]
    [InlineData("""{"Active":true,"Mode":1}""", true)]
    [InlineData("""{"Active":true,"Mode":"Lock"}""", true)]
    [InlineData("""{"Active":true,"Mode":2}""", false)]
    [InlineData("""{"Active":true,"Mode":"Corner"}""", false)]
    [InlineData("""{"Active":false,"Mode":1}""", false)]
    [InlineData("""{}""", false)]
    [InlineData("not-json", false)]
    public void Self_discipline_lock_requires_an_active_full_screen_lock(string json, bool expected)
    {
        Assert.Equal(expected, SelfDisciplineLockSource.ParseFullscreenLock(json));
    }

    [Fact]
    public void Course_url_supports_common_chaoxing_parameter_spellings()
    {
        var ok = CourseUrlParser.TryParse(
            "https://mooc1.chaoxing.com/mycourse/studentstudy?courseid=10&chapterid=20&clazzid=30",
            out var course);

        Assert.True(ok);
        Assert.Equal("10", course.CourseId);
        Assert.Equal("20", course.ChapterId);
        Assert.Equal("30", course.ClassId);
        Assert.Equal("course:10:30", course.CourseKey);
    }

    [Theory]
    [InlineData("https://example.com/course")]
    [InlineData("http://mooc1.chaoxing.com/course")]
    [InlineData("")]
    public void Course_url_rejects_non_chaoxing_or_non_https_urls(string value)
    {
        Assert.False(CourseUrlParser.TryParse(value, out _));
    }

    [Fact]
    public void Checkpoint_round_trip_preserves_resume_position()
    {
        var root = Path.Combine(Path.GetTempPath(), "cxap-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "state.json");
        try
        {
            var store = new CheckpointStore(path);
            store.Save(new PlaybackCheckpoint
            {
                EntryUrl = "https://mooc1.chaoxing.com/course",
                CourseKey = "course:10:30",
                TaskKey = "video-abc",
                TaskTitle = "Test video",
                CurrentTime = 42.5,
                Duration = 120,
                CompletedTaskKeys = { "video-done" },
                SkippedTaskKeys = { "quiz-1" },
            });

            var loaded = store.Load();

            Assert.Equal(42.5, loaded.CurrentTime);
            Assert.Equal("video-abc", loaded.TaskKey);
            Assert.Contains("video-done", loaded.CompletedTaskKeys);
            Assert.Contains("quiz-1", loaded.SkippedTaskKeys);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Queue_skips_completed_and_attempted_videos_and_wraps()
    {
        var videos = new[]
        {
            Video("a", 0),
            Video("b", 1),
            Video("c", 2),
        };

        var next = TaskQueuePlanner.FindNext(
            videos,
            selectedTaskKey: "b",
            endedTaskKey: "b",
            completed: new HashSet<string> { "c" },
            attempted: new HashSet<string> { "b" });

        Assert.Equal("a", next?.Key);
    }

    [Theory]
    [InlineData(false, false, false, true, true, true, true)]
    [InlineData(false, false, false, true, false, true, false)]
    [InlineData(false, false, false, true, true, false, false)]
    [InlineData(false, true, true, false, false, false, true)]
    [InlineData(false, true, false, false, false, false, false)]
    [InlineData(true, true, true, true, true, true, false)]
    public void Automation_policy_pauses_on_pointer_leave_and_sleep(
        bool sleeping,
        bool lockActive,
        bool lockArmed,
        bool manualRequested,
        bool pointerInside,
        bool windowActive,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutomationPolicy.ShouldRun(
                sleeping,
                lockActive,
                lockArmed,
                manualRequested,
                pointerInside,
                windowActive));
    }

    [Fact]
    public void Continue_without_cursor_keeps_running_after_focus_loss()
    {
        Assert.True(
            AutomationPolicy.ShouldRun(
                sleeping: false,
                lockActive: false,
                lockArmed: false,
                manualRequested: true,
                pointerInside: false,
                windowActive: false,
                continueWithoutCursor: true));
    }

    [Fact]
    public void Page_marked_answers_take_priority()
    {
        var solver = new PageMetadataQuizSolver();
        var prompt = new InteractiveQuizPrompt(
            "测试题",
            "多选题",
            new[]
            {
                new InteractiveQuizOption(0, "A", false),
                new InteractiveQuizOption(1, "B", true),
                new InteractiveQuizOption(2, "C", true),
            });

        var attempts = solver.BuildAttempts(
            prompt,
            new PlayerSettings
            {
                QuizMode = InteractiveQuizMode.PauseAndPrompt,
                PreferredOption = 1,
            });

        var attempt = Assert.Single(attempts);
        Assert.Equal(new[] { 1, 2 }, attempt.OptionIndexes);
        Assert.Equal("page-answer", attempt.Source);
    }

    [Fact]
    public void Preferred_secondary_and_remaining_order_is_respected()
    {
        var solver = new PageMetadataQuizSolver();
        var prompt = new InteractiveQuizPrompt(
            "测试题",
            "单选题",
            Enumerable.Range(0, 4)
                .Select(index => new InteractiveQuizOption(index, $"Option {index + 1}", false))
                .ToList());

        var attempts = solver.BuildAttempts(
            prompt,
            new PlayerSettings
            {
                QuizMode = InteractiveQuizMode.AutoAttempt,
                PreferredOption = 2,
                SecondaryOption = 4,
                TryRemainingOptions = true,
                MaxQuizAttempts = 4,
            });

        Assert.Equal(new[] { 1, 3, 0, 2 }, attempts.Select(attempt => attempt.OptionIndexes[0]));
    }

    [Fact]
    public void Unmarked_multiple_choice_pauses_instead_of_guessing()
    {
        var solver = new PageMetadataQuizSolver();
        var prompt = new InteractiveQuizPrompt(
            "多选题",
            "多选题",
            new[]
            {
                new InteractiveQuizOption(0, "A", false),
                new InteractiveQuizOption(1, "B", false),
            });

        var attempts = solver.BuildAttempts(
            prompt,
            new PlayerSettings { QuizMode = InteractiveQuizMode.AutoAttempt });

        Assert.Empty(attempts);
    }

    [Fact]
    public void Settings_normalize_duplicate_option_preferences()
    {
        var settings = new PlayerSettings
        {
            PreferredOption = 2,
            SecondaryOption = 2,
            MaxQuizAttempts = 99,
        };

        settings.Normalize();

        Assert.Equal(2, settings.PreferredOption);
        Assert.Equal(3, settings.SecondaryOption);
        Assert.Equal(4, settings.MaxQuizAttempts);
    }

    [Fact]
    public void Settings_round_trip_preserves_quiz_configuration()
    {
        var root = Path.Combine(Path.GetTempPath(), "cxap-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new PlayerSettings
            {
                Muted = false,
                CursorBehavior = CursorBehavior.PauseOnLeave,
                LockReady = true,
                QuizMode = InteractiveQuizMode.AutoAttempt,
                PreferredOption = 3,
                SecondaryOption = 1,
                TryRemainingOptions = false,
                MaxQuizAttempts = 2,
            });

            var loaded = store.Load();

            Assert.False(loaded.Muted);
            Assert.Equal(CursorBehavior.PauseOnLeave, loaded.CursorBehavior);
            Assert.True(loaded.LockReady);
            Assert.Equal(3, loaded.PreferredOption);
            Assert.Equal(1, loaded.SecondaryOption);
            Assert.False(loaded.TryRemainingOptions);
            Assert.Equal(2, loaded.MaxQuizAttempts);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TaskPoint Video(string key, int order) =>
        new(key, key, "video", false, order, "frame");
}
