using ScreenRecorder.Core.Services;
using ScreenRecorder.Infrastructure.Configuration;
using ScreenRecorder.Infrastructure.Diagnostics;

namespace ScreenRecorder.Core.Recording;

/// <summary>
/// 录制编排核心（§7.1，issue #10）：
/// 磁盘检查（§27）→ 命名（§25）→ 崩溃标记（§56）→ 倒计时（§20）→
/// 采集 + 编码接线 → 帧节流（§49）→ 暂停丢帧无缝（§22）→ 停止封装。
/// 音频在 #11 接入；暂停期间不喂帧，rawvideo 管道天然把暂停时段从时间轴剔除。
/// </summary>
public sealed class RecordingManager : IRecordingManager
{
    /// <summary>§27：开录前最低剩余空间要求。</summary>
    public const long MinFreeBytes = 512L * 1024 * 1024;   // 512 MB

    /// <summary>§20：开始录制倒计时秒数。</summary>
    public const int CountdownSeconds = 3;

    private readonly ConfigService _config;
    private readonly IScreenCaptureService _capture;
    private readonly IAudioCaptureService _audio;
    private readonly Func<IVideoEncoder> _encoderFactory;
    private readonly SessionMarkerStore _markers;

    private FrameThrottler? _throttler;
    private IVideoEncoder? _encoder;
    private System.Diagnostics.Stopwatch? _clock;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public RecordingSession? CurrentSession { get; private set; }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    /// <summary>录制完成事件：参数为输出文件完整路径。</summary>
    public event EventHandler<string>? RecordingCompleted;

    public RecordingManager(
        ConfigService config,
        IScreenCaptureService capture,
        IAudioCaptureService audio,
        Func<IVideoEncoder> encoderFactory,
        SessionMarkerStore markers)
    {
        _config = config;
        _capture = capture;
        _audio = audio;
        _encoderFactory = encoderFactory;
        _markers = markers;
    }

    public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        EnsureState(RecordingState.Idle);
        var cfg = _config.Current;

        // §27 磁盘空间检查：明确阻止，不静默开录
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cfg.OutputDirectory))!);
        if (drive.AvailableFreeSpace < MinFreeBytes)
            throw new InsufficientDiskSpaceException
            { DriveName = drive.Name, FreeBytes = drive.AvailableFreeSpace, RequiredBytes = MinFreeBytes };

        var outputPath = RecordingOutputNamer.NextPath(cfg.OutputDirectory, DateTimeOffset.Now);

        var session = new RecordingSession { Request = request, OutputFilePath = outputPath };
        CurrentSession = session;
        _markers.Mark(session.Id, outputPath);   // §56 崩溃标记

        TransitionTo(RecordingState.Countdown);
        await Task.Delay(TimeSpan.FromSeconds(CountdownSeconds), ct);   // §20 倒计时

        // 采集先行：编码器初始化需要真实画面尺寸
        await _capture.StartAsync(request, ct);
        var (w, h) = _capture.CurrentSize;

        var audioEnabled = cfg.Audio.SystemAudio || cfg.Audio.Microphone;

        _encoder = _encoderFactory();
        await _encoder.InitializeAsync(new VideoEncoderOptions
        {
            Width = w,
            Height = h,
            Fps = cfg.Video.Fps,
            Quality = cfg.Video.Quality,
            Encoder = cfg.Video.Encoder,
            AudioEnabled = audioEnabled,
            OutputFilePath = outputPath
        });

        _throttler = new FrameThrottler(cfg.Video.Fps);
        _clock = System.Diagnostics.Stopwatch.StartNew();
        _capture.FrameArrived += OnFrameArrived;

        // §16-18：音频按配置启用，混合块路由进编码器（§48：暂停期丢音频块，与视频同步剔除）
        if (audioEnabled)
        {
            _audio.BufferReady += OnAudioBuffer;
            await _audio.StartAsync(cfg.Audio.SystemAudio, cfg.Audio.Microphone, ct);
        }

        TransitionTo(RecordingState.Recording);
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        EnsureState(RecordingState.Recording);
        CurrentSession?.MarkPaused();
        TransitionTo(RecordingState.Paused);   // 暂停期帧在 OnFrameArrived 被丢弃 → 时间轴无缝（§22）
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken ct = default)
    {
        EnsureState(RecordingState.Paused);
        CurrentSession?.MarkResumed();
        TransitionTo(RecordingState.Recording);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (State is not (RecordingState.Recording or RecordingState.Paused))
            throw new InvalidOperationException($"当前状态 {State} 不允许停止");

        TransitionTo(RecordingState.Stopping);
        _capture.FrameArrived -= OnFrameArrived;
        _audio.BufferReady -= OnAudioBuffer;
        await _capture.StopAsync(ct);
        await _audio.StopAsync(ct);

        if (_encoder is not null)
        {
            await _encoder.StopAsync();   // flush + 封装 MP4
            (_encoder as IDisposable)?.Dispose();
            _encoder = null;
        }

        var output = CurrentSession?.OutputFilePath;
        if (CurrentSession is not null)
        {
            _markers.Clear(CurrentSession.Id);   // 正常结束 → 清除崩溃标记
            CurrentSession.Dispose();
            CurrentSession = null;
        }
        _clock = null;
        _throttler = null;
        TransitionTo(RecordingState.Idle);

        if (output is not null)
            RecordingCompleted?.Invoke(this, output);
    }

    private void OnFrameArrived(object? sender, VideoFrame frame)
    {
        if (State != RecordingState.Recording || _encoder is null || _throttler is null || _clock is null)
            return;
        if (!_throttler.ShouldWrite(_clock.Elapsed))
            return;
        _encoder.EncodeFrameAsync(frame).GetAwaiter().GetResult();   // 同步消费复用缓冲（采集契约）
    }

    private void OnAudioBuffer(object? sender, AudioBuffer buffer)
    {
        if (State != RecordingState.Recording || _encoder is null)
            return;   // 暂停：音频块丢弃，与视频帧剔除保持同一时间轴（§22/§48）
        _encoder.EncodeAudioAsync(buffer).GetAwaiter().GetResult();
    }

    private void EnsureState(RecordingState expected)
    {
        if (State != expected)
            throw new InvalidOperationException($"期望状态 {expected}，当前 {State}");
    }

    private void TransitionTo(RecordingState next)
    {
        var old = State;
        State = next;
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs { OldState = old, NewState = next });
    }
}
