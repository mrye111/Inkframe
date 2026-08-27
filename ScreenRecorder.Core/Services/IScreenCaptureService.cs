using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.Core.Services;

/// <summary>画面采集接口（§44）。实现见 #12（WGC，research/wgc-wpf-details.md）。</summary>
public interface IScreenCaptureService
{
    Task StartAsync(RecordingRequest request, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    /// <summary>采集画面尺寸（物理像素），StartAsync 后可读；编码器初始化依赖。</summary>
    (int Width, int Height) CurrentSize { get; }

    /// <summary>帧事件：时间戳基于会话统一时钟（§47）。</summary>
    event EventHandler<VideoFrame>? FrameArrived;
}
