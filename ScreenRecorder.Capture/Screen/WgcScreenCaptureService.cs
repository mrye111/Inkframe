using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Capture.Screen;

/// <summary>
/// Windows.Graphics.Capture 采集服务占位实现。
/// 实施依据：research/wgc-wpf-details.md（IGraphicsCaptureItemInterop + CreateFreeThreaded + 专用线程）。
/// 真实实现见 issue #12。
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService
{
    public event EventHandler<VideoFrame>? FrameArrived;

    public Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        // TODO(#12): IGraphicsCaptureItemInterop.CreateForWindow/CreateForMonitor → FramePool → Session
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        // TODO(#12): Dispose session/pool
        return Task.CompletedTask;
    }
}
