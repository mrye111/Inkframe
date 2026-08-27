using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenRecorder.Audio.Mixer;
using ScreenRecorder.Audio.Pcm;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Audio.SystemAudio;

/// <summary>
/// WASAPI 音频采集（§16-18）：系统声 Loopback + 麦克风 Capture → 统一 48k/s16/立体声 → 混音器。
/// - §61 自动设备检测：默认设备（MMDeviceEnumerator 缺省端点）
/// - §54 设备断开：RecordingStopped 异常 → DeviceDisconnected 事件 + 该源按静音继续，录制不中断
/// - 格式归一：NAudio MediaFoundationResampler 到混音器契约格式
/// </summary>
public sealed class WasapiAudioCaptureService : IAudioCaptureService, IDisposable
{
    private static readonly WaveFormat MixerFormat = new(AudioMixer.SampleRate, 16, AudioMixer.Channels);

    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _mic;
    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;
    private MediaFoundationResampler? _loopbackResampler;
    private MediaFoundationResampler? _micResampler;
    private AudioMixer? _mixer;

    public event EventHandler<AudioBuffer>? BufferReady;
    public event EventHandler<string>? DeviceDisconnected;

    /// <summary>当前激活的混音器（设置页实时音量用，§34/§35）。</summary>
    public AudioMixer? Mixer => _mixer;

    public Task StartAsync(bool captureSystemAudio, bool captureMicrophone, CancellationToken ct = default)
    {
        _mixer = new AudioMixer();
        _mixer.ChunkMixed += (_, chunk) => BufferReady?.Invoke(this, new AudioBuffer
        {
            TimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp(),
            Data = chunk,
            SampleRate = AudioMixer.SampleRate,
            Channels = AudioMixer.Channels
        });

        if (captureSystemAudio)
        {
            try
            {
                _loopback = new WasapiLoopbackCapture();
                _loopbackBuffer = new BufferedWaveProvider(_loopback.WaveFormat) { DiscardOnBufferOverflow = true };
                _loopbackResampler = new MediaFoundationResampler(_loopbackBuffer, MixerFormat) { ResamplerQuality = 60 };
                _loopback.DataAvailable += OnLoopbackData;
                _loopback.RecordingStopped += OnSourceStopped("系统声音");
                _loopback.StartRecording();
            }
            catch (Exception ex)
            {
                DeviceDisconnected?.Invoke(this, $"系统声音初始化失败：{ex.Message}");
                _loopback = null;
            }
        }

        if (captureMicrophone)
        {
            try
            {
                _mic = new WasapiCapture();
                _micBuffer = new BufferedWaveProvider(_mic.WaveFormat) { DiscardOnBufferOverflow = true };
                _micResampler = new MediaFoundationResampler(_micBuffer, MixerFormat) { ResamplerQuality = 60 };
                _mic.DataAvailable += OnMicData;
                _mic.RecordingStopped += OnSourceStopped("麦克风");
                _mic.StartRecording();
            }
            catch (Exception ex)
            {
                DeviceDisconnected?.Invoke(this, $"麦克风初始化失败：{ex.Message}");
                _mic = null;
            }
        }

        _mixer.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _mixer?.Stop();
        try { _loopback?.StopRecording(); } catch { /* 设备可能已断开 */ }
        try { _mic?.StopRecording(); } catch { }
        return Task.CompletedTask;
    }

    private void OnLoopbackData(object? sender, WaveInEventArgs e) =>
        PushResampled(_loopbackBuffer, _loopbackResampler, e, _mixer!.PushSystemAudio);

    private void OnMicData(object? sender, WaveInEventArgs e) =>
        PushResampled(_micBuffer, _micResampler, e, _mixer!.PushMicrophone);

    /// <summary>原生块入缓冲 → 从重采样器读归一化 s16le/48k/立体声 → 混音器。</summary>
    private static void PushResampled(BufferedWaveProvider? buffer0, MediaFoundationResampler? resampler, WaveInEventArgs e, Action<byte[]> push)
    {
        if (buffer0 is null || resampler is null) return;
        buffer0.AddSamples(e.Buffer, 0, e.BytesRecorded);
        var outBuf = new byte[e.BytesRecorded * 2 + 4096];   // 重采样后余量
        int read;
        while ((read = resampler.Read(outBuf, 0, outBuf.Length)) > 0)
            push(outBuf.AsSpan(0, read).ToArray());
    }

    private EventHandler<StoppedEventArgs> OnSourceStopped(string sourceName) => (_, e) =>
    {
        // §54：异常断开 → 通知上层，该源后续按静音处理（队列无数据 mixer 自动填静音）
        if (e.Exception is not null)
            DeviceDisconnected?.Invoke(this, $"{sourceName}设备断开：{e.Exception.Message}");
    };

    public void Dispose()
    {
        StopAsync().Wait();
        _mixer?.Dispose();
        _loopback?.Dispose();
        _mic?.Dispose();
        _loopbackResampler?.Dispose();
        _micResampler?.Dispose();
    }
}
