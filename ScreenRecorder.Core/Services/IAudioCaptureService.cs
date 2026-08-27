namespace ScreenRecorder.Core.Services;

/// <summary>音频采集接口（§45）：系统声 Loopback + 麦克风 Capture + 混音（§16-18）。实现见 #11。</summary>
public interface IAudioCaptureService
{
    Task StartAsync(bool captureSystemAudio, bool captureMicrophone, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    event EventHandler<AudioBuffer>? BufferReady;

    /// <summary>音频设备断开通知（§54）：不中断录制，由上层提示。</summary>
    event EventHandler<string>? DeviceDisconnected;

    /// <summary>输出电平通知（§35）：0..1 RMS，UI 音量条用。</summary>
    event EventHandler<float>? LevelChanged;
}

public sealed class AudioBuffer
{
    public long TimestampTicks { get; init; }
    public required byte[] Data { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
}
