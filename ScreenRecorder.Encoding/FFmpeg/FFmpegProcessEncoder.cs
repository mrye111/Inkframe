using System.Diagnostics;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Encoding.FFmpeg;

/// <summary>
/// ffmpeg.exe 进程管线编码器（#6 定案：V1 方案 A）。
/// BGRA 帧 → stdin rawvideo → H.264 → MP4。编码器经 EncoderProber 运行时探测。
/// 每实例对应一次录制会话，不作为单例复用。
/// </summary>
public sealed class FFmpegProcessEncoder : IVideoEncoder, IDisposable
{
    private readonly FfmpegLocator _locator;
    private readonly EncoderProber _prober;

    private Process? _ffmpeg;
    private VideoEncoderOptions? _options;
    private string? _resolvedEncoder;
    private System.IO.Pipes.NamedPipeServerStream? _audioPipe;
    private Task? _pipeWaitTask;

    public FFmpegProcessEncoder() : this(new FfmpegLocator()) { }

    public FFmpegProcessEncoder(FfmpegLocator locator, EncoderProber? prober = null)
    {
        _locator = locator;
        _prober = prober ?? new EncoderProber(locator.ResolveForDevelopment());
    }

    /// <summary>实际使用的编码器（InitializeAsync 后可读，用于日志与 UI 提示）。</summary>
    public string? ResolvedEncoder => _resolvedEncoder;

    public Task InitializeAsync(VideoEncoderOptions options)
    {
        _options = options;
        _resolvedEncoder = options.Encoder == "auto" ? _prober.Probe() : options.Encoder;

        var qualityArgs = QualityArgs(_resolvedEncoder, options.Quality);

        // 音频轨：命名管道作第二输入（Windows 上 ffmpeg 只有 pipe:0 一个描述符管道，音频走 \\.\pipe\）
        string? audioPipeName = null;
        if (options.AudioEnabled)
        {
            audioPipeName = "inkframe-audio-" + Guid.NewGuid().ToString("N");
            _audioPipe = new System.IO.Pipes.NamedPipeServerStream(
                audioPipeName, System.IO.Pipes.PipeDirection.Out, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
            _pipeWaitTask = _audioPipe.WaitForConnectionAsync();   // ffmpeg 启动时会来连
        }

        var args = "-y -hide_banner -loglevel error"
            + $" -f rawvideo -pix_fmt bgra -s {options.Width}x{options.Height} -r {options.Fps} -i pipe:0"
            + (audioPipeName is not null
                ? $" -f s16le -ar 48000 -ac 2 -i \\\\.\\pipe\\{audioPipeName}"
                : "")
            + $" -c:v {_resolvedEncoder} {qualityArgs}"
            + (audioPipeName is not null ? " -map 0:v -map 1:a -c:a aac -shortest" : "")
            + " -pix_fmt yuv420p"
            + " \"" + options.OutputFilePath + "\"";

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputFilePath)!);
        _ffmpeg = Process.Start(new ProcessStartInfo(_locator.ResolveForDevelopment(), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true   // 异步消费到日志文件
        }) ?? throw new InvalidOperationException("ffmpeg 启动失败");

        var logFile = options.OutputFilePath + ".ffmpeg.log";
        _ = Task.Run(async () =>
        {
            var text = await _ffmpeg.StandardError.ReadToEndAsync();
            await File.WriteAllTextAsync(logFile, text);
        });
        return Task.CompletedTask;
    }

    public Task EncodeFrameAsync(VideoFrame frame)
    {
        if (_ffmpeg is null || _options is null) throw new InvalidOperationException("未初始化");
        if (frame.PixelData is null)
            throw new ArgumentException("进程管线需要 CPU 像素（VideoFrame.PixelData）", nameof(frame));

        // 同步写：背压由管道天然提供；上层掉帧策略（§49）保证这里不长时间阻塞
        _ffmpeg.StandardInput.BaseStream.Write(frame.PixelData, 0, frame.PixelData.Length);
        return Task.CompletedTask;
    }

    public Task EncodeAudioAsync(AudioBuffer buffer)
    {
        if (_audioPipe is null) return Task.CompletedTask;
        if (!_audioPipe.IsConnected)
            return Task.CompletedTask;   // ffmpeg 尚未连上（启动竞态）：丢 20ms 无感
        return _audioPipe.WriteAsync(buffer.Data, 0, buffer.Data.Length);
    }

    public async Task FlushAsync()
    {
        if (_ffmpeg is null) return;
        _ffmpeg.StandardInput.Close();          // 视频输入关闭
        if (_audioPipe is not null)
        {
            if (_pipeWaitTask is not null) await Task.WhenAny(_pipeWaitTask, Task.Delay(2000));
            _audioPipe.Close();                 // 音频输入关闭 → ffmpeg 收到双 EOF 收尾封装
        }
        await _ffmpeg.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task StopAsync()
    {
        await FlushAsync();
        if (_ffmpeg is not null && _ffmpeg.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg 异常退出（{_ffmpeg.ExitCode}），详见输出旁 .ffmpeg.log");
    }

    /// <summary>画质档位 → 编码参数（§14：低/标准/高清/超清）。硬编用 -b:v，openh264 用 -b:v。</summary>
    private static string QualityArgs(string encoder, string quality) => quality switch
    {
        "低" => "-b:v 2M",
        "标准" => "-b:v 5M",
        "高清" => "-b:v 10M",
        "超清" => "-b:v 20M",
        _ => "-b:v 5M"
    } + (encoder == "h264_nvenc" ? " -preset p4" : "");

    public void Dispose()
    {
        _audioPipe?.Dispose();
        try
        {
            if (_ffmpeg is { HasExited: false })
            {
                _ffmpeg.StandardInput.Close();
                _ffmpeg.Kill();
            }
        }
        catch { /* 析构路径不抛 */ }
        _ffmpeg?.Dispose();
    }
}
