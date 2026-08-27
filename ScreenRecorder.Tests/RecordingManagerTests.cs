using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.Infrastructure;
using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Tests;

public sealed class RecordingManagerTests
{
    private sealed class FakeCapture : IScreenCaptureService
    {
        public (int Width, int Height) CurrentSize => (320, 240);
        public event EventHandler<VideoFrame>? FrameArrived;
        public bool Started { get; private set; }

        public Task StartAsync(RecordingRequest request, CancellationToken ct = default)
        { Started = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Emit() => FrameArrived?.Invoke(this,
            new VideoFrame { Width = 320, Height = 240, PixelData = new byte[320 * 240 * 4] });
    }

    private sealed class FakeEncoder : IVideoEncoder
    {
        public int Frames;
        public bool Initialized, Stopped;
        public Task InitializeAsync(VideoEncoderOptions options) { Initialized = true; return Task.CompletedTask; }
        public Task EncodeFrameAsync(VideoFrame frame) { Frames++; return Task.CompletedTask; }
        public Task FlushAsync() => Task.CompletedTask;
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
    }

    private sealed class FakeAudio : IAudioCaptureService
    {
        public event EventHandler<AudioBuffer>? BufferReady { add { } remove { } }
        public event EventHandler<string>? DeviceDisconnected { add { } remove { } }
        public Task StartAsync(bool s, bool m, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (RecordingManager Manager, FakeCapture Capture, FakeEncoder Encoder) Build()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        var provider = new ServiceCollection()
            .AddInfrastructure()
            .AddSingleton<IScreenCaptureService>(capture)
            .AddSingleton<IAudioCaptureService>(new FakeAudio())
            .AddSingleton<Func<IVideoEncoder>>(_ => () => encoder)
            .AddSingleton<IRecordingManager, RecordingManager>()
            .BuildServiceProvider();
        return ((RecordingManager)provider.GetRequiredService<IRecordingManager>(), capture, encoder);
    }

    [Fact]
    public async Task Full_Lifecycle_With_Real_Pipeline_Wiring()
    {
        var (manager, capture, encoder) = Build();

        await manager.StartAsync(new RecordingRequest { Mode = RecordingMode.FullScreen });
        // 倒计时结束 → Recording（§20 3 秒）
        Assert.Equal(RecordingState.Recording, manager.State);
        Assert.True(capture.Started);
        Assert.True(encoder.Initialized);

        await Task.Delay(50);   // 等墙钟走过 1/30s，节流器放行首帧
        capture.Emit();   // 录制中 → 写入
        Assert.Equal(1, encoder.Frames);

        await manager.PauseAsync();
        capture.Emit();   // 暂停中 → 丢弃（§22 无缝时间轴）
        Assert.Equal(1, encoder.Frames);

        await manager.ResumeAsync();
        await Task.Delay(80);   // 时钟继续走：written=1 需等 expected 超过 1
        capture.Emit();
        Assert.Equal(2, encoder.Frames);

        string? completed = null;
        manager.RecordingCompleted += (_, path) => completed = path;

        await manager.StopAsync();
        Assert.True(encoder.Stopped);
        Assert.Equal(RecordingState.Idle, manager.State);
        Assert.NotNull(completed);
        Assert.EndsWith(".mp4", completed);
    }

    [Fact]
    public async Task Pause_Before_Start_Throws()
    {
        var (manager, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PauseAsync());
    }
}

public sealed class FrameThrottlerTests
{
    [Fact]
    public void Throttles_To_Target_Fps_By_Wall_Clock()
    {
        var throttler = new FrameThrottler(30);
        // t=1.0s 时期望帧数 = 30：放 30 帧，第 31 帧丢弃
        for (var i = 0; i < 30; i++)
            Assert.True(throttler.ShouldWrite(TimeSpan.FromSeconds(1.0)));
        Assert.False(throttler.ShouldWrite(TimeSpan.FromSeconds(1.0)));
        // 墙钟前进 → 放行新的一帧
        Assert.True(throttler.ShouldWrite(TimeSpan.FromSeconds(1.04)));
        Assert.False(throttler.ShouldWrite(TimeSpan.FromSeconds(1.04)));
        // t=0 起始时刻不放帧（避免开录瞬间灌帧）
        var fresh = new FrameThrottler(30);
        Assert.False(fresh.ShouldWrite(TimeSpan.Zero));
    }
}
