namespace ScreenRecorder.Core.Services;

/// <summary>编码抽象接口（§43）：UI 不允许知道具体编码实现。</summary>
public interface IVideoEncoder
{
    Task InitializeAsync(VideoEncoderOptions options);
    Task EncodeFrameAsync(VideoFrame frame);
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
    public required string OutputFilePath { get; init; }
}

/// <summary>一帧画面（D3D11 纹理句柄或 CPU 位图，随 #12 定稿）。</summary>
public sealed class VideoFrame
{
    public long TimestampTicks { get; init; }
    public IntPtr TextureHandle { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
