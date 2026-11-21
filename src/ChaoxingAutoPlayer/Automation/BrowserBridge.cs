using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChaoxingAutoPlayer.Automation;

public sealed record TaskPoint(
    string Key,
    string Title,
    string Kind,
    bool Completed,
    int Order,
    string FrameId,
    bool Locked = false,
    bool Current = false);

public sealed record VideoSnapshot(
    string TaskKey,
    string Title,
    double CurrentTime,
    double Duration,
    bool Paused,
    bool Ended,
    string SourceUrl,
    string FrameId);

public sealed record TaskScanResult(string FrameId, IReadOnlyList<TaskPoint> Tasks, string PageUrl);

public sealed record InteractiveQuizSnapshot(
    string FrameId,
    string Question,
    string QuestionType,
    bool Multiple,
    IReadOnlyList<InteractiveQuizOption> Options);

public sealed class BrowserBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebView2 _webView;
    private readonly HashSet<CoreWebView2Frame> _frames = new();
    private readonly Dictionary<CoreWebView2Frame, string> _frameIds = new();
    private readonly string _script;
    private bool _initialized;
    private bool _muted = true;
    private bool _started;
    private double _resumeAt;

    public BrowserBridge(WebView2 webView)
    {
        _webView = webView;
        _script = LoadScript();
    }

    public event EventHandler<TaskScanResult>? TasksChanged;

    public event EventHandler<VideoSnapshot>? VideoChanged;

    public event EventHandler<VideoSnapshot>? VideoEnded;

    public event EventHandler<string>? Blocked;

    public event EventHandler? NavigationCompleted;

    public event EventHandler? NavigationStarting;

    public event EventHandler<string>? FrameDestroyed;

    public event EventHandler<InteractiveQuizSnapshot>? QuizDetected;

    public event EventHandler<string>? QuizResolved;

    public event EventHandler<string>? QuizWrong;

    public event EventHandler<string>? QuizNeedsAttention;

    public bool IsInitialized => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        var arguments = "--autoplay-policy=no-user-gesture-required "
            + "--disable-background-timer-throttling "
            + "--disable-renderer-backgrounding "
            + "--disable-backgrounding-occluded-windows "
            + "--disable-features=CalculateNativeWinOcclusion";
        if (int.TryParse(Environment.GetEnvironmentVariable("CXAP_DEBUG_PORT"), out var debugPort)
            && debugPort is > 0 and < 65536)
        {
            arguments += $" --remote-debugging-port={debugPort}";
        }

        var options = new CoreWebView2EnvironmentOptions(arguments);

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Services.AppPaths.WebViewData,
            options);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.IsMuted = _muted;
        core.WebMessageReceived += OnWebMessageReceived;
        core.FrameCreated += (_, args) => HookFrame(args.Frame);
        core.NavigationStarting += (_, _) => NavigationStarting?.Invoke(this, EventArgs.Empty);
        core.NavigationCompleted += (_, _) => NavigationCompleted?.Invoke(this, EventArgs.Empty);
        core.NewWindowRequested += OnNewWindowRequested;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(_script);
        _initialized = true;
    }

    public void Navigate(string url)
    {
        if (_initialized)
        {
            _webView.CoreWebView2.Navigate(url);
        }
    }

    public void Start(bool muted, double resumeAt)
    {
        _started = true;
        _resumeAt = resumeAt;
        SetMuted(muted);
        Send(new
        {
            source = "chaoxing-auto-player",
            command = "start",
            muted,
            resumeAt,
        });
    }

    public void Pause()
    {
        _started = false;
        Send(new { source = "chaoxing-auto-player", command = "pause" });
    }

    public void Scan() => Send(new { source = "chaoxing-auto-player", command = "scan" });

    public void ClickTask(string key) => Send(new
    {
        source = "chaoxing-auto-player",
        command = "clickTask",
        key,
    });

    public void ClickNextFallback() => Send(new
    {
        source = "chaoxing-auto-player",
        command = "nextVideo",
    });

    public void ResolveQuiz(string frameId, IReadOnlyList<int> optionIndexes)
    {
        var message = JsonSerializer.Serialize(new
        {
            source = "chaoxing-auto-player",
            command = "resolveQuiz",
            optionIndexes,
        });
        var frame = _frameIds
            .FirstOrDefault(pair => string.Equals(pair.Value, frameId, StringComparison.Ordinal))
            .Key;
        if (frame is not null)
        {
            try
            {
                frame.PostWebMessageAsJson(message);
                return;
            }
            catch
            {
                _frameIds.Remove(frame);
            }
        }

        Send(new
        {
            source = "chaoxing-auto-player",
            command = "resolveQuiz",
            optionIndexes,
        });
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        if (_initialized)
        {
            _webView.CoreWebView2.IsMuted = muted;
        }

        Send(new
        {
            source = "chaoxing-auto-player",
            command = "setMuted",
            muted,
        });
    }

    public async Task SetFocusEmulationAsync(bool enabled)
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Emulation.setFocusEmulationEnabled",
                JsonSerializer.Serialize(new { enabled }));

            if (enabled)
            {
                await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.setWebLifecycleState",
                    """{"state":"active"}""");
                await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Emulation.setIdleOverride",
                    """{"isUserActive":true,"isScreenUnlocked":true}""");
            }
            else
            {
                await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Emulation.clearIdleOverride",
                    "{}");
            }
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"Page focus emulation could not be changed: {ex.Message}");
        }
    }

    public async Task PulseUserActivityAsync()
    {
        if (!_initialized)
        {
            return;
        }

        var width = Math.Max(40, _webView.ActualWidth);
        var height = Math.Max(40, _webView.ActualHeight);
        var x = Math.Round(width / 2);
        var y = Math.Round(height * 0.55);
        try
        {
            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Input.dispatchMouseEvent",
                JsonSerializer.Serialize(new
                {
                    type = "mouseMoved",
                    x,
                    y,
                    button = "none",
                    buttons = 0,
                    pointerType = "mouse",
                }));
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"Trusted mouse activity pulse failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _frames.Clear();
        if (_initialized)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
    }

    private void HookFrame(CoreWebView2Frame frame)
    {
        if (!_frames.Add(frame))
        {
            return;
        }

        frame.Destroyed += (_, _) =>
        {
            _frames.Remove(frame);
            if (_frameIds.Remove(frame, out var frameId))
            {
                FrameDestroyed?.Invoke(this, frameId);
            }
        };
        frame.FrameCreated += (_, args) => HookFrame(args.Frame);
        frame.WebMessageReceived += OnFrameWebMessageReceived;
        frame.NavigationStarting += (_, _) =>
        {
            if (_frameIds.Remove(frame, out var frameId))
            {
                FrameDestroyed?.Invoke(this, frameId);
            }
        };
        frame.NavigationCompleted += async (_, _) =>
        {
            try
            {
                await frame.ExecuteScriptAsync(_script);
                SendStartToFrame(frame);
            }
            catch (Exception ex)
            {
                Services.AppLog.Warn($"Frame script injection failed: {ex.Message}");
            }
        };
        _ = InjectFrameAsync(frame);
    }

    private void Send(object message)
    {
        if (!_initialized)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message);
        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // The top-level document may be navigating.
        }

        foreach (var frame in _frames.ToArray())
        {
            try
            {
                frame.PostWebMessageAsJson(json);
            }
            catch
            {
                _frames.Remove(frame);
            }
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        ProcessMessage(args.WebMessageAsJson);
    }

    private void OnFrameWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (sender is CoreWebView2Frame frame)
        {
            var frameId = TryReadFrameId(args.WebMessageAsJson);
            if (!string.IsNullOrWhiteSpace(frameId))
            {
                _frameIds[frame] = frameId;
            }
        }

        ProcessMessage(args.WebMessageAsJson);
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<BridgeMessage>(json, JsonOptions);
            if (message?.Source != "chaoxing-auto-player")
            {
                return;
            }

            if (message.Type == "tasks" && message.Tasks is not null)
            {
                var points = message.Tasks
                    .Where(task => !string.IsNullOrWhiteSpace(task.Key))
                    .Select(task => new TaskPoint(
                        task.Key!,
                        task.Title ?? string.Empty,
                        task.Kind ?? "other",
                        task.Completed,
                        task.Order,
                        message.FrameId ?? string.Empty,
                        task.Locked,
                        task.Current))
                    .ToList();
                TasksChanged?.Invoke(this, new TaskScanResult(
                    message.FrameId ?? string.Empty,
                    points,
                    message.Url ?? string.Empty));
            }
            else if (message.Type is "video" or "ended" && message.Video is not null)
            {
                var snapshot = new VideoSnapshot(
                    message.Video.TaskKey ?? string.Empty,
                    message.Video.Title ?? string.Empty,
                    message.Video.CurrentTime,
                    message.Video.Duration,
                    message.Video.Paused,
                    message.Video.Ended || message.Type == "ended",
                    message.Video.SourceUrl ?? string.Empty,
                    message.FrameId ?? string.Empty);
                VideoChanged?.Invoke(this, snapshot);
                if (snapshot.Ended)
                {
                    VideoEnded?.Invoke(this, snapshot);
                }
            }
            else if (message.Type == "blocked")
            {
                Blocked?.Invoke(this, message.Message ?? "课程页面需要手动处理。");
            }
            else if (message.Type == "quizDetected" && message.Quiz is not null)
            {
                var options = message.Quiz.Options?
                    .Select(option => new InteractiveQuizOption(
                        option.Index,
                        option.Text ?? string.Empty,
                        option.MarkedCorrect))
                    .ToList() ?? new List<InteractiveQuizOption>();
                QuizDetected?.Invoke(this, new InteractiveQuizSnapshot(
                    message.FrameId ?? string.Empty,
                    message.Quiz.Question ?? string.Empty,
                    message.Quiz.QuestionType ?? string.Empty,
                    message.Quiz.Multiple,
                    options));
            }
            else if (message.Type == "quizResolved")
            {
                QuizResolved?.Invoke(this, message.Message ?? "互动题已完成。");
            }
            else if (message.Type == "quizWrong")
            {
                QuizWrong?.Invoke(this, message.Message ?? "互动题答案错误。");
            }
            else if (message.Type == "quizNeedsAttention")
            {
                QuizNeedsAttention?.Invoke(this, message.Message ?? "互动题需要手动处理。");
            }
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"Browser message could not be parsed: {ex.Message}");
        }
    }

    private async Task InjectFrameAsync(CoreWebView2Frame frame)
    {
        try
        {
            await frame.ExecuteScriptAsync(_script);
            SendStartToFrame(frame);
        }
        catch
        {
            // The iframe often navigates immediately after creation; NavigationCompleted runs it again.
        }
    }

    private void SendStartToFrame(CoreWebView2Frame frame)
    {
        if (!_started)
        {
            return;
        }

        try
        {
            frame.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                source = "chaoxing-auto-player",
                command = "start",
                muted = _muted,
                resumeAt = _resumeAt,
            }));
        }
        catch
        {
            // The iframe may have navigated away again.
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("chaoxing.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase)))
        {
            _webView.CoreWebView2.Navigate(args.Uri);
            return;
        }

        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"External login window could not be opened: {ex.Message}");
        }
    }

    private static string LoadScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Automation.Bridge.js", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded browser bridge script is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? TryReadFrameId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("frameId", out var frameId)
                ? frameId.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class BridgeMessage
    {
        public string? Source { get; set; }
        public string? Type { get; set; }
        public string? FrameId { get; set; }
        public string? Message { get; set; }
        public string? Url { get; set; }
        public List<TaskMessage>? Tasks { get; set; }
        public VideoMessage? Video { get; set; }
        public QuizMessage? Quiz { get; set; }
    }

    private sealed class TaskMessage
    {
        public string? Key { get; set; }
        public string? Title { get; set; }
        public string? Kind { get; set; }
        public bool Completed { get; set; }
        public bool Locked { get; set; }
        public bool Current { get; set; }
        public int Order { get; set; }
    }

    private sealed class VideoMessage
    {
        public string? TaskKey { get; set; }
        public string? Title { get; set; }
        public double CurrentTime { get; set; }
        public double Duration { get; set; }
        public bool Paused { get; set; }
        public bool Ended { get; set; }
        public string? SourceUrl { get; set; }
    }

    private sealed class QuizMessage
    {
        public string? Question { get; set; }
        public string? QuestionType { get; set; }
        public bool Multiple { get; set; }
        public string? Message { get; set; }
        public List<QuizOptionMessage>? Options { get; set; }
    }

    private sealed class QuizOptionMessage
    {
        public int Index { get; set; }
        public string? Text { get; set; }
        public bool MarkedCorrect { get; set; }
    }
}
