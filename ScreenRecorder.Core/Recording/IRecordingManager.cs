namespace ScreenRecorder.Core.Recording;

/// <summary>
/// 所有录制行为的统一入口（§7.1）。UI 只与本接口交互。
/// </summary>
public interface IRecordingManager
{
    RecordingState State { get; }
    RecordingSession? CurrentSession { get; }

    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    Task StartAsync(RecordingRequest request, CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>一次录制的发起参数（模式/区域/画质/音频源）。</summary>
public sealed record RecordingRequest
{
    public required RecordingMode Mode { get; init; }
    public string? TargetWindowHandle { get; init; }   // 窗口模式
    public ScreenRect? Region { get; init; }           // 区域模式（物理像素）
}

public enum RecordingMode { FullScreen, Window, Region }

/// <summary>虚拟坐标系中的矩形（多显示器，§51）。</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);
