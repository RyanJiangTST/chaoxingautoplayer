using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ChaoxingAutoPlayer.Automation;
using ChaoxingAutoPlayer.Models;
using ChaoxingAutoPlayer.Services;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ChaoxingAutoPlayer;

public partial class MainWindow : Window
{
    private readonly BrowserBridge _bridge;
    private readonly CheckpointStore _checkpointStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly IInteractiveQuizSolver _quizSolver = new PageMetadataQuizSolver();
    private readonly SelfDisciplineLockSource _lockSource;
    private readonly DispatcherTimer _checkpointTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _resumeWatchdog = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _activityPulse = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly Dictionary<string, IReadOnlyList<TaskPoint>> _tasksByFrame = new();
    private readonly Dictionary<string, string> _taskSignaturesByFrame = new();
    private readonly Dictionary<string, string> _taskUrlsByFrame = new();
    private readonly HashSet<string> _completedTaskKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _skippedTaskKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attemptedTaskKeys = new(StringComparer.OrdinalIgnoreCase);

    private PlaybackCheckpoint _checkpoint;
    private PlayerSettings _settings;
    private CourseLink? _course;
    private VideoSnapshot? _currentVideo;
    private WinForms.NotifyIcon? _tray;
    private bool _allowClose;
    private bool _manualRequested;
    private bool _automationRunning;
    private bool _pointerInside;
    private bool _sleeping;
    private bool _continueWithoutCursor = true;
    private bool _lockArmed;
    private bool _lockActive;
    private bool _lockDriven;
    private bool _advancing;
    private bool _resumeClickPending;
    private bool _resumeAfterNavigation;
    private bool _initialized;
    private bool _quizPaused;
    private bool _quizWaitingForResult;
    private InteractiveQuizPrompt? _activeQuiz;
    private string _activeQuizFrameId = string.Empty;
    private IReadOnlyList<InteractiveQuizAttempt> _quizAttempts = Array.Empty<InteractiveQuizAttempt>();
    private int _quizAttemptIndex;
    private string _selectedTaskKey = string.Empty;
    private double _pendingResumeAt;
    private DateTime _lastVideoUpdateUtc = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureCreated();
        _checkpoint = _checkpointStore.Load();
        _settings = _settingsStore.Load();
        _continueWithoutCursor = _settings.CursorBehavior == CursorBehavior.ContinueWithoutCursor;
        _lockArmed = _settings.LockReady;
        _bridge = new BrowserBridge(BrowserView);
        _lockSource = new SelfDisciplineLockSource();
        _lockActive = _lockSource.IsFullscreenLocked;

        _bridge.TasksChanged += OnTasksChanged;
        _bridge.FrameDestroyed += OnFrameDestroyed;
        _bridge.VideoChanged += OnVideoChanged;
        _bridge.VideoEnded += OnVideoEnded;
        _bridge.Blocked += OnBlocked;
        _bridge.QuizDetected += OnQuizDetected;
        _bridge.QuizResolved += OnQuizResolved;
        _bridge.QuizWrong += OnQuizWrong;
        _bridge.QuizNeedsAttention += OnQuizNeedsAttention;
        _bridge.NavigationStarting += OnNavigationStarting;
        _bridge.NavigationCompleted += OnNavigationCompleted;
        _lockSource.FullscreenLockChanged += OnFullscreenLockChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _checkpointTimer.Tick += (_, _) => SaveCheckpoint();
        _resumeWatchdog.Tick += (_, _) => EnsureVideoStillPlaying();
        _activityPulse.Tick += async (_, _) =>
        {
            if (_automationRunning)
            {
                await _bridge.PulseUserActivityAsync();
            }
        };

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Activated += (_, _) => EvaluateAutomation();
        Deactivated += (_, _) => EvaluateAutomation();

        CreateTrayIcon();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _bridge.InitializeAsync();
            _initialized = true;
            _checkpointTimer.Start();
            _resumeWatchdog.Start();
            _activityPulse.Start();
            ApplySettings();

            if (CourseUrlParser.TryParse(_checkpoint.EntryUrl, out var saved))
            {
                UrlBox.Text = _checkpoint.EntryUrl;
                _course = saved;
                StatusText.Text = "已恢复上次课程，正在加载…";
                _bridge.Navigate(saved.Url.AbsoluteUri);
            }
            else
            {
                StatusText.Text = "请粘贴任意一个课程视频网址。";
            }

            UpdateLockStatus();
            UpdateModeSummary();
        }
        catch (Exception ex)
        {
            StatusText.Text = "WebView2 初始化失败。";
            AppLog.Error("WebView2 initialization failed.", ex);
            MessageBox.Show(
                this,
                "无法启动课程浏览器。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n"
                + "可从 Microsoft 官网安装 Evergreen Runtime 后重试。\n\n"
                + ex.Message,
                "超星视频连播器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (!CourseUrlParser.TryParse(UrlBox.Text, out var course))
        {
            StatusText.Text = "网址必须是 https://*.chaoxing.com 的学习页。";
            return;
        }

        var resumeAfterNavigation = _manualRequested || _automationRunning;
        PauseAutomation("正在打开新课程…");
        _manualRequested = resumeAfterNavigation;
        _resumeAfterNavigation = resumeAfterNavigation;
        _course = course;
        _currentVideo = null;
        _selectedTaskKey = string.Empty;
        _tasksByFrame.Clear();
        _taskSignaturesByFrame.Clear();
        _taskUrlsByFrame.Clear();
        _completedTaskKeys.Clear();
        _skippedTaskKeys.Clear();
        _attemptedTaskKeys.Clear();
        SkippedList.ItemsSource = null;

        _checkpoint = new PlaybackCheckpoint
        {
            EntryUrl = course.Url.AbsoluteUri,
            CourseKey = course.CourseKey,
        };
        SaveCheckpoint();

        StatusText.Text = "正在加载课程，请完成登录并等待任务点扫描…";
        AppLog.Info($"Opening course {course.CourseKey}.");
        _bridge.Navigate(course.Url.AbsoluteUri);
        UpdateStartButton();
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _bridge.Scan();
        StatusText.Text = _lockActive
            ? "SelfDiscipline 全屏锁屏中，正在重新扫描。"
            : "正在重新扫描章节和视频任务点…";
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (!CourseUrlParser.TryParse(UrlBox.Text, out var course))
        {
            StatusText.Text = "请先打开一个有效的课程视频网址。";
            return;
        }

        _course = course;
        _manualRequested = !_manualRequested;
        if (!_manualRequested)
        {
            _quizPaused = false;
            _quizWaitingForResult = false;
            _activeQuiz = null;
            QuizActionButton.Visibility = Visibility.Collapsed;
        }
        _pendingResumeAt = _manualRequested && _checkpoint.CurrentTime > 1
            ? _checkpoint.CurrentTime
            : 0;
        EvaluateAutomation();
        UpdateStartButton();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings = dialog.Settings;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Settings could not be saved: {ex.Message}");
            StatusText.Text = "设置已在本次运行中生效，但保存失败。";
        }

        ApplySettings();
    }

    private void ApplySettings()
    {
        _continueWithoutCursor = _settings.CursorBehavior == CursorBehavior.ContinueWithoutCursor;
        _lockArmed = _settings.LockReady;

        if (_initialized)
        {
            _bridge.SetMuted(_settings.Muted);
            if (_automationRunning)
            {
                _ = _bridge.SetFocusEmulationAsync(_continueWithoutCursor || _lockDriven);
                _ = _bridge.PulseUserActivityAsync();
            }

            EvaluateAutomation();
        }

        UpdateLockStatus();
        UpdateModeSummary();
    }

    private void UpdateModeSummary()
    {
        if (ModeSummaryText is null)
        {
            return;
        }

        var mute = _settings.Muted ? "静音" : "有声";
        var cursor = _continueWithoutCursor ? "光标离开继续" : "光标离开暂停";
        var quiz = _settings.QuizMode == InteractiveQuizMode.AutoAttempt
            ? $"互动题 {_settings.PreferredOption}→{_settings.SecondaryOption}→其余"
            : "互动题暂停提示";
        ModeSummaryText.Text = $"{mute} · {cursor} · {quiz}";
    }

    private void BrowserView_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pointerInside = true;
        EvaluateAutomation();
        if (_automationRunning && _continueWithoutCursor)
        {
            StatusText.Text = "光标已返回网页；防暂停连播仍在运行。";
        }
    }

    private void BrowserView_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pointerInside = false;
        EvaluateAutomation();
        if (_automationRunning && _continueWithoutCursor)
        {
            StatusText.Text = "光标已离开；防暂停模式继续发送网页活跃事件。";
        }
    }

    private void EvaluateAutomation()
    {
        if (!_initialized || _quizPaused)
        {
            return;
        }

        var lockDrive = _lockActive && _lockArmed && !_sleeping;
        var pointerDrive = _manualRequested && _pointerInside && IsActive && !_sleeping;
        var shouldRun = AutomationPolicy.ShouldRun(
            _sleeping,
            _lockActive,
            _lockArmed,
            _manualRequested,
            _pointerInside,
            IsActive,
            _continueWithoutCursor);
        if (shouldRun)
        {
            if (!_automationRunning)
            {
                StartAutomation(lockDrive);
            }
        }
        else if (_automationRunning)
        {
            PauseAutomation(_pointerInside || _lockActive
                ? "已暂停。"
                : "光标离开课程网页，视频已暂停并保存。返回网页后继续。");
        }
    }

    private void StartAutomation(bool lockDriven)
    {
        if (_pendingResumeAt > 1 && !IsOnCheckpointVideo())
        {
            var checkpointTask = FindCheckpointTask();
            if (checkpointTask is not null
                && !string.Equals(checkpointTask.Key, _selectedTaskKey, StringComparison.OrdinalIgnoreCase))
            {
                _automationRunning = true;
                _lockDriven = lockDriven;
                _selectedTaskKey = checkpointTask.Key;
                _resumeClickPending = true;
                StatusText.Text = $"正在回到上次停止的视频：{checkpointTask.Title}";
                _bridge.ClickTask(checkpointTask.Key);
                _ = ResumeAfterTaskClickAsync();
                return;
            }
        }

        _automationRunning = true;
        _lockDriven = lockDriven;
        _lastVideoUpdateUtc = DateTime.UtcNow;
        if (lockDriven || _continueWithoutCursor)
        {
            _ = _bridge.SetFocusEmulationAsync(true);
            _ = _bridge.PulseUserActivityAsync();
        }

        _bridge.Start(_settings.Muted, _pendingResumeAt);
        _pendingResumeAt = 0;
        _resumeClickPending = false;
        StatusText.Text = lockDriven
            ? "SelfDiscipline 全屏锁屏中，正在自动连播…"
            : _continueWithoutCursor
                ? "防暂停连播中：光标可以离开网页，系统鼠标不会被控制。"
                : "自动连播中；光标离开网页会暂停。";
        AppLog.Info(
            lockDriven
                ? $"Automation started by SelfDiscipline lock (continueWithoutCursor={_continueWithoutCursor})."
                : $"Automation started manually (continueWithoutCursor={_continueWithoutCursor}).");
        UpdateStartButton();
    }

    private async Task ResumeAfterTaskClickAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (!_automationRunning || !_resumeClickPending)
        {
            return;
        }

        _resumeClickPending = false;
        _bridge.Start(_settings.Muted, _pendingResumeAt);
        _pendingResumeAt = 0;
        StatusText.Text = "已回到上次视频，正在恢复播放进度…";
    }

    private void PauseAutomation(string status)
    {
        if (_initialized)
        {
            _bridge.Pause();
        }

        _automationRunning = false;
        _lockDriven = false;
        _ = _bridge.SetFocusEmulationAsync(false);
        StatusText.Text = status;
        SaveCheckpoint();
        UpdateStartButton();
    }

    private void OnTasksChanged(object? sender, TaskScanResult result)
    {
        Dispatcher.Invoke(() =>
        {
            var frame = result.FrameId;
            if (string.IsNullOrWhiteSpace(frame))
            {
                return;
            }

            var pageGroup = TaskPageGroup(result.PageUrl);
            var staleFrames = pageGroup.Length == 0
                ? Array.Empty<string>()
                : _taskUrlsByFrame
                    .Where(pair => pair.Key != frame
                                   && string.Equals(TaskPageGroup(pair.Value), pageGroup, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToArray();
            foreach (var staleFrame in staleFrames)
            {
                _tasksByFrame.Remove(staleFrame);
                _taskSignaturesByFrame.Remove(staleFrame);
                _taskUrlsByFrame.Remove(staleFrame);
            }

            _tasksByFrame[frame] = result.Tasks;
            _taskUrlsByFrame[frame] = result.PageUrl;
            var signature = string.Join(
                '|',
                result.Tasks.Select(task => $"{task.Key}:{task.Kind}:{task.Completed}"));
            if (_taskSignaturesByFrame.TryGetValue(frame, out var previous)
                && string.Equals(previous, signature, StringComparison.Ordinal))
            {
                return;
            }

            _taskSignaturesByFrame[frame] = signature;
            var videoTasks = result.Tasks.Where(task => task.Kind == "video").ToList();
            AppLog.Info(
                $"Task scan {frame[^Math.Min(8, frame.Length)..]}: "
                + $"videos={videoTasks.Count}, documents={result.Tasks.Count(task => task.Kind == "document")}, "
                + $"chapters={result.Tasks.Count(task => task.Kind == "chapter")}, "
                + $"videoCompleted={videoTasks.Count(task => task.Completed)}, "
                + $"videoTasks={string.Join("; ", videoTasks.Select(task => $"{(task.Key.Length > 10 ? task.Key[^10..] : task.Key)}:{task.Completed}:{Trim(task.Title)}"))}.");

            foreach (var task in result.Tasks)
            {
                if (task.Completed)
                {
                    _completedTaskKeys.Add(task.Key);
                }

                if (task.Kind is not ("video" or "chapter"))
                {
                    _skippedTaskKeys.Add(task.Key);
                }
            }

            UpdateQueueText();
            UpdateSkippedList();

            if (_resumeAfterNavigation
                && AllTasks().Any(task => task.Kind == "video"))
            {
                _resumeAfterNavigation = false;
                EvaluateAutomation();
            }
        });
    }

    private void OnFrameDestroyed(object? sender, string frameId)
    {
        Dispatcher.Invoke(() =>
        {
            _tasksByFrame.Remove(frameId);
            _taskSignaturesByFrame.Remove(frameId);
            _taskUrlsByFrame.Remove(frameId);
            UpdateQueueText();
            UpdateSkippedList();
        });
    }

    private void OnVideoChanged(object? sender, VideoSnapshot video)
    {
        Dispatcher.Invoke(() =>
        {
            _currentVideo = video;
            _lastVideoUpdateUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(video.TaskKey) && string.IsNullOrWhiteSpace(_selectedTaskKey))
            {
                _selectedTaskKey = video.TaskKey;
            }

            CurrentVideoText.Text = $"当前视频：{video.Title}";
            ProgressText.Text = video.Duration > 0
                ? $"播放进度：{FormatTime(video.CurrentTime)} / {FormatTime(video.Duration)}"
                : $"播放进度：{FormatTime(video.CurrentTime)}";
            SaveCheckpoint();
        });
    }

    private void OnVideoEnded(object? sender, VideoSnapshot video)
    {
        Dispatcher.Invoke(() => _ = AdvanceAsync(video));
    }

    private void OnBlocked(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            AppLog.Warn(message);
        });
    }

    private void OnQuizDetected(object? sender, InteractiveQuizSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _activeQuiz = new InteractiveQuizPrompt(
                snapshot.Question,
                snapshot.QuestionType,
                snapshot.Options);
            _activeQuizFrameId = snapshot.FrameId;
            _quizAttempts = _quizSolver.BuildAttempts(_activeQuiz, _settings);
            _quizAttemptIndex = 0;
            _quizWaitingForResult = false;
            AppLog.Info(
                $"Interactive quiz detected: type={snapshot.QuestionType}, "
                + $"options={snapshot.Options.Count}, attempts={_quizAttempts.Count}.");

            if (_settings.QuizMode == InteractiveQuizMode.AutoAttempt && _quizAttempts.Count > 0)
            {
                _quizPaused = false;
                _automationRunning = true;
                _bridge.Start(_settings.Muted, 0);
                BeginQuizAttempt();
            }
            else
            {
                PauseForQuiz("检测到视频互动题，已暂停等待处理。");
            }
        });
    }

    private void BeginQuizAttempt()
    {
        if (_activeQuiz is null || _quizAttemptIndex >= _quizAttempts.Count)
        {
            PauseForQuiz("互动题所有预设选项均已尝试，请手动处理。");
            return;
        }

        var attempt = _quizAttempts[_quizAttemptIndex];
        _quizWaitingForResult = true;
        AppLog.Info(
            $"Interactive quiz attempt {_quizAttemptIndex + 1}: "
            + $"{string.Join(',', attempt.OptionIndexes)} ({attempt.Source}).");
        StatusText.Text =
            $"正在处理视频互动题（第 {_quizAttemptIndex + 1}/{_quizAttempts.Count} 次，"
            + $"{attempt.Source}）：{_activeQuiz.Question}";
        _bridge.ResolveQuiz(_activeQuizFrameId, attempt.OptionIndexes);
    }

    private void OnQuizResolved(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _quizPaused = false;
            _quizWaitingForResult = false;
            _activeQuiz = null;
            _activeQuizFrameId = string.Empty;
            _quizAttempts = Array.Empty<InteractiveQuizAttempt>();
            _quizAttemptIndex = 0;
            QuizActionButton.Visibility = Visibility.Collapsed;
            StatusText.Text = message;
            _automationRunning = true;
            _bridge.Start(_settings.Muted, 0);
            AppLog.Info("Interactive quiz resolved.");
        });
    }

    private void OnQuizWrong(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _quizWaitingForResult = false;
            _quizAttemptIndex++;
            AppLog.Warn($"Interactive quiz answer was rejected: {message}");
            if (_settings.QuizMode == InteractiveQuizMode.AutoAttempt
                && _quizAttemptIndex < _quizAttempts.Count)
            {
                _ = RetryQuizAfterDelayAsync();
            }
            else
            {
                PauseForQuiz("互动题答案未通过，请检查后手动处理。");
            }
        });
    }

    private async Task RetryQuizAfterDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        if (_activeQuiz is not null && _automationRunning && !_quizPaused)
        {
            BeginQuizAttempt();
        }
    }

    private void OnQuizNeedsAttention(object? sender, string message)
    {
        Dispatcher.Invoke(() => PauseForQuiz(message));
    }

    private void PauseForQuiz(string message)
    {
        _quizPaused = true;
        _quizWaitingForResult = false;
        _automationRunning = false;
        _bridge.Pause();
        _ = _bridge.SetFocusEmulationAsync(false);
        QuizActionButton.Visibility = Visibility.Visible;
        QuizActionButton.IsEnabled = _activeQuiz is not null;
        StatusText.Text = message;
        AppLog.Warn(message);
        SaveCheckpoint();
    }

    private async void QuizActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeQuiz is null)
        {
            QuizActionButton.Visibility = Visibility.Collapsed;
            return;
        }

        var solverSettings = _settings.Clone();
        solverSettings.QuizMode = InteractiveQuizMode.AutoAttempt;
        _quizAttempts = _quizSolver.BuildAttempts(_activeQuiz, solverSettings);
        _quizAttemptIndex = 0;
        if (_quizAttempts.Count == 0)
        {
            StatusText.Text = "当前题目没有可自动判断的选项，请直接在网页中作答。";
            return;
        }

        _quizPaused = false;
        _automationRunning = true;
        _bridge.Start(_settings.Muted, 0);
        await Task.Delay(300);
        BeginQuizAttempt();
    }

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_automationRunning)
            {
                _bridge.Scan();
                if (_resumeClickPending)
                {
                    _resumeClickPending = false;
                    _pendingResumeAt = _checkpoint.CurrentTime;
                    StartAutomation(_lockActive && _lockArmed && !_sleeping);
                }
                else
                {
                    _bridge.Start(_settings.Muted, 0);
                }
            }
            else
            {
                _bridge.Scan();
                if (_resumeAfterNavigation)
                {
                    EvaluateAutomation();
                }
            }
        });
    }

    private void OnNavigationStarting(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _tasksByFrame.Clear();
            _taskSignaturesByFrame.Clear();
            _taskUrlsByFrame.Clear();
            _quizPaused = false;
            _quizWaitingForResult = false;
            _activeQuiz = null;
            _activeQuizFrameId = string.Empty;
            _quizAttempts = Array.Empty<InteractiveQuizAttempt>();
            QuizActionButton.Visibility = Visibility.Collapsed;
            UpdateQueueText();
            UpdateSkippedList();
        });
    }

    private async Task AdvanceAsync(VideoSnapshot endedVideo)
    {
        if (_advancing)
        {
            return;
        }

        _advancing = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (!_automationRunning)
            {
                return;
            }

            _bridge.Scan();
            await Task.Delay(TimeSpan.FromSeconds(1));

            var videos = AllTasks()
                .Where(task => task.Kind == "video")
                .OrderBy(task => task.Order)
                .ThenBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (videos.Count == 0)
            {
                if (!await AdvancePastNonVideoTasksAsync(endedVideo.TaskKey))
                {
                    PauseAutomation("没有找到后续视频。后续章节可能被前置测验锁定，请先处理右侧列出的未完成项。");
                }
                return;
            }

            var next = TaskQueuePlanner.FindNext(
                videos,
                _selectedTaskKey,
                endedVideo.TaskKey,
                _completedTaskKeys,
                _attemptedTaskKeys);
            if (next is null)
            {
                if (!await AdvancePastNonVideoTasksAsync(endedVideo.TaskKey))
                {
                    PauseAutomation("当前课程视频已处理完，或后续章节被前置任务锁定。请检查已跳过的测验和文档。");
                }
                return;
            }

            _attemptedTaskKeys.Add(next.Key);
            _selectedTaskKey = next.Key;
            StatusText.Text = $"正在切换到下一个视频：{next.Title}";
            _bridge.ClickTask(next.Key);

            await Task.Delay(TimeSpan.FromSeconds(12));
            if (_automationRunning
                && (_currentVideo is null
                    || string.Equals(_currentVideo.TaskKey, endedVideo.TaskKey, StringComparison.OrdinalIgnoreCase)
                    || _currentVideo.Ended))
            {
                _bridge.ClickNextFallback();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Advance to next video failed.", ex);
            StatusText.Text = "切换到下一个视频失败，请手动检查页面。";
        }
        finally
        {
            _advancing = false;
        }
    }

    private async Task<bool> AdvancePastNonVideoTasksAsync(string previousVideoKey)
    {
        var chapters = AllTasks()
            .Where(task => task.Kind == "chapter")
            .OrderBy(task => task.Order)
            .ToList();
        var currentIndex = chapters.FindIndex(task => task.Current);
        var nextChapter = chapters
            .Skip(currentIndex >= 0 ? currentIndex + 1 : 0)
            .FirstOrDefault(task => !task.Locked && !task.Completed);
        if (nextChapter is not null)
        {
            StatusText.Text = $"正在进入后续章节：{nextChapter.Title}";
            _bridge.ClickTask(nextChapter.Key);
            await Task.Delay(TimeSpan.FromSeconds(8));
            _bridge.Scan();
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (_currentVideo is not null
                && !string.Equals(_currentVideo.TaskKey, previousVideoKey, StringComparison.OrdinalIgnoreCase)
                && !_currentVideo.Ended)
            {
                return true;
            }
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!_automationRunning)
            {
                return false;
            }

            StatusText.Text = attempt == 0
                ? "正在跳过非视频任务，寻找后续章节…"
                : $"正在继续寻找下一个视频（{attempt + 1}/6）…";
            _bridge.ClickNextFallback();
            await Task.Delay(TimeSpan.FromSeconds(7));
            _bridge.Scan();
            await Task.Delay(TimeSpan.FromSeconds(1));

            if (_currentVideo is not null
                && !string.Equals(_currentVideo.TaskKey, previousVideoKey, StringComparison.OrdinalIgnoreCase)
                && !_currentVideo.Ended)
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<TaskPoint> AllTasks() =>
        _tasksByFrame.Values.SelectMany(tasks => tasks)
            .GroupBy(task => task.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private static string TaskPageGroup(string pageUrl)
    {
        return Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}"
            : string.Empty;
    }

    private void UpdateQueueText()
    {
        var videos = AllTasks().Where(task => task.Kind == "video").ToList();
        var unfinished = videos.Count(task => !task.Completed && !_completedTaskKeys.Contains(task.Key));
        QueueText.Text = $"视频队列：{videos.Count} 个视频，{unfinished} 个待播放";
    }

    private void UpdateSkippedList()
    {
        var skipped = AllTasks()
            .Where(task => task.Kind is not ("video" or "chapter"))
            .OrderBy(task => task.Order)
            .Select(task => $"[{TaskKindLabel(task.Kind)}] {Trim(task.Title)}")
            .Distinct()
            .ToList();

        SkippedList.ItemsSource = skipped;
    }

    private void SaveCheckpoint()
    {
        if (_course is null && CourseUrlParser.TryParse(UrlBox?.Text, out var parsed))
        {
            _course = parsed;
        }

        try
        {
            _checkpoint.Version = 1;
            _checkpoint.EntryUrl = _course?.Url.AbsoluteUri ?? _checkpoint.EntryUrl;
            _checkpoint.CourseKey = _course?.CourseKey ?? _checkpoint.CourseKey;
            _checkpoint.TaskKey = string.IsNullOrWhiteSpace(_selectedTaskKey)
                ? _currentVideo?.TaskKey ?? string.Empty
                : _selectedTaskKey;
            _checkpoint.TaskTitle = _currentVideo?.Title ?? _checkpoint.TaskTitle;
            _checkpoint.CurrentTime = _currentVideo?.CurrentTime ?? _checkpoint.CurrentTime;
            _checkpoint.Duration = _currentVideo?.Duration ?? _checkpoint.Duration;
            _checkpoint.CompletedTaskKeys = _completedTaskKeys.ToList();
            _checkpoint.SkippedTaskKeys = _skippedTaskKeys.ToList();
            _checkpointStore.Save(_checkpoint);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Checkpoint save failed: {ex.Message}");
        }
    }

    private void EnsureVideoStillPlaying()
    {
        if (!_automationRunning || _quizPaused || _quizWaitingForResult)
        {
            return;
        }

        if (_currentVideo is { Paused: true, Ended: false }
            && DateTime.UtcNow - _lastVideoUpdateUtc > TimeSpan.FromSeconds(10))
        {
            _bridge.Start(_settings.Muted, 0);
        }
    }

    private bool IsOnCheckpointVideo()
    {
        if (_currentVideo is null || string.IsNullOrWhiteSpace(_checkpoint.TaskTitle))
        {
            return false;
        }

        var current = _currentVideo.Title;
        var saved = _checkpoint.TaskTitle;
        return current.Contains(saved, StringComparison.OrdinalIgnoreCase)
               || saved.Contains(current, StringComparison.OrdinalIgnoreCase);
    }

    private TaskPoint? FindCheckpointTask()
    {
        var tasks = AllTasks();
        var exact = tasks.FirstOrDefault(task =>
            string.Equals(task.Key, _checkpoint.TaskKey, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        if (string.IsNullOrWhiteSpace(_checkpoint.TaskTitle))
        {
            return null;
        }

        return tasks.FirstOrDefault(task =>
            task.Kind == "video"
            && (task.Title.Contains(_checkpoint.TaskTitle, StringComparison.OrdinalIgnoreCase)
                || _checkpoint.TaskTitle.Contains(task.Title, StringComparison.OrdinalIgnoreCase)));
    }

    private void OnFullscreenLockChanged(object? sender, bool active)
    {
        Dispatcher.Invoke(() =>
        {
            _lockActive = active;
            UpdateLockStatus();

            if (active && _lockArmed && !_sleeping)
            {
                EvaluateAutomation();
            }
            else if (!active && _automationRunning)
            {
                _manualRequested = false;
                PauseAutomation("SelfDiscipline 全屏锁屏已结束，视频已暂停并保存。");
            }
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _sleeping = true;
                _manualRequested = false;
                PauseAutomation("系统即将睡眠，视频已暂停并保存。唤醒后不会自动播放。");
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _sleeping = false;
                StatusText.Text = "系统已唤醒，连播保持暂停；确认后请手动点击继续。";
            }
        });
    }

    private void UpdateLockStatus()
    {
        if (!IsInitialized || HintText is null)
        {
            return;
        }

        if (!_lockArmed)
        {
            HintText.Text = _continueWithoutCursor
                ? "防暂停模式：光标可以离开课程网页，程序只发送网页内部的活跃事件，不移动或锁定系统鼠标。"
                : "普通模式：光标必须停留在课程网页内；离开会暂停，返回后继续。程序不会锁定或移动系统鼠标。";
        }
        else if (_lockActive)
        {
            HintText.Text = "SelfDiscipline 全屏锁屏中：已绕过前台光标检查，正在后台连播。";
        }
        else
        {
            HintText.Text = "已准备锁屏联动：保持程序运行，SelfDiscipline 进入全屏锁屏后自动开始。";
        }
    }

    private void UpdateStartButton()
    {
        StartButton.Content = _manualRequested ? "暂停连播" : "开始连播";
    }

    private static string FormatTime(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            return "00:00";
        }

        var time = TimeSpan.FromSeconds(value);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static string TaskKindLabel(string kind) => kind switch
    {
        "quiz" => "测验",
        "document" => "文档",
        _ => "其他",
    };

    private static string Trim(string value) =>
        value.Length <= 100 ? value : value[..100] + "…";

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowFromTray());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowClose = true;
            Close();
        });

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "超星视频连播器",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall back to the Windows default icon.
        }

        return Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            SaveCheckpoint();
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        _tray?.ShowBalloonTip(
            3500,
            "超星视频连播器",
            "程序仍在托盘运行；SelfDiscipline 锁屏联动会继续生效。",
            WinForms.ToolTipIcon.Info);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _checkpointTimer.Stop();
        _resumeWatchdog.Stop();
        _activityPulse.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _lockSource.Dispose();
        _bridge.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        Application.Current.Shutdown();
    }
}
