namespace ScreenRecorder.Core.Services;

/// <summary>编码抽象接口（§43）：UI 不允许知道具体编码实现。</summary>
public interface IVideoEncoder
{
    Task InitializeAsync(VideoEncoderOptions options);
    Task EncodeFrameAsync(VideoFrame frame);

    /// <summary>写入一块混合音频（s16le/48kHz/立体声）。仅 options.AudioEnabled 时会被调用。</summary>
    Task EncodeAudioAsync(AudioBuffer buffer);

    Task FlushAsync();
    Task StopAsync();
}

public sealed record VideoEncoderOptions
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Fps { get; init; } = 30;
    public string Quality { get; init; } = "标准";
    public string Encoder { get; init; } = "auto";   // auto/nvenc/qsv/amf/software

    /// <summary>是否带音频轨（系统声/麦克风任一启用）。</summary>
    public bool AudioEnabled { get; init; }
    public required string OutputFilePath { get; init; }
}

/// <summary>
/// 一帧画面。进程管线（V1）走 PixelData（BGRA 打包字节）；TextureHandle 预留给 DLL 路线（#14 预研）。
/// </summary>
public sealed class VideoFrame
{
    public long TimestampTicks { get; init; }
    public IntPtr TextureHandle { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>BGRA 打包像素（Width*4*Height 字节）。进程编码器直接写 stdin。</summary>
    public byte[]? PixelData { get; init; }

    /// <summary>BGRA 像素原生指针 + 长度（采集层 DIB 位直供，零拷贝；二选一，优先于 PixelData）。</summary>
    public IntPtr PixelPtr { get; init; }
    public int PixelByteCount { get; init; }
}
