using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Audio.SystemAudio;

/// <summary>
/// WASAPI 音频采集占位实现（Loopback + Capture + 混音）。真实实现见 issue #11。
/// </summary>
public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    public event EventHandler<AudioBuffer>? BufferReady;
    public event EventHandler<string>? DeviceDisconnected;

    public Task StartAsync(bool captureSystemAudio, bool captureMicrophone, CancellationToken ct = default)
    {
        // TODO(#11): NAudio WasapiLoopbackCapture / WasapiCapture → Mixer
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
