using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Encoding.FFmpeg;

/// <summary>
/// ffmpeg.exe 进程管线编码器（§42 方案 A，V1 默认；最终定案见 issue #6）。
/// 骨架阶段仅立形状，真实管线随 #3 Spike 结论与 #6 决策填充。
/// </summary>
public sealed class FFmpegProcessEncoder : IVideoEncoder
{
    private VideoEncoderOptions? _options;

    public Task InitializeAsync(VideoEncoderOptions options)
    {
        _options = options;
        // TODO(#6): 组装 ffmpeg 命令行（-f rawvideo / d3d11 帧喂入，-c:v h264_nvenc 等硬编映射）
        return Task.CompletedTask;
    }

    public Task EncodeFrameAsync(VideoFrame frame)
    {
        if (_options is null) throw new InvalidOperationException("未初始化");
        // TODO(#3/#6): 帧写入进程 stdin 管道
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;   // TODO: 关闭输入流等待进程排空

    public Task StopAsync() => Task.CompletedTask;    // TODO: 等待 ffmpeg 收尾封装 MP4
}
