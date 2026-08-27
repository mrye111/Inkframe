namespace ScreenRecorder.Core.Recording;

/// <summary>
/// 所有录制行为的统一入口（§7.1）。UI 只与本接口交互。
/// </summary>
public interface IRecordingManager
{
    RecordingState State { get; }
    RecordingSession? CurrentSession { get; }

    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    /// <summary>录制完成事件：参数为输出文件完整路径。</summary>
    event EventHandler<string>? RecordingCompleted;

    Task StartAsync(RecordingRequest request, CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>一次录制的发起参数（模式/区域/画质/音频源）。</summary>
public sealed record RecordingRequest
{
    public required RecordingMode Mode { get; init; }
    public string? TargetWindowHandle { get; init; }   // 窗口模式：HWND 十六进制字符串
    public string? TargetMonitor { get; init; }        // 全屏模式：显示器设备名，null = 主显示器
    public ScreenRect? Region { get; init; }           // 区域模式：虚拟坐标系矩形（§51）
    public bool RecordCursor { get; init; } = true;    // §30
    public bool HighlightCursor { get; init; }         // §30 鼠标高亮
}

public enum RecordingMode { FullScreen, Window, Region }

/// <summary>虚拟坐标系中的矩形（多显示器，§51）。</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);
