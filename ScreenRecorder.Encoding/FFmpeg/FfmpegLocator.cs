namespace ScreenRecorder.Encoding.FFmpeg;

/// <summary>
/// ffmpeg 二进制定位（#6 决策：只用随包捆绑版本，不读系统 PATH）。
/// 期望布局：<AppBase>/ffmpeg/ffmpeg.exe（LGPL 构建随安装包分发）。
/// </summary>
public sealed class FfmpegLocator
{
    private readonly string _baseDir;

    public FfmpegLocator(string? baseDir = null) =>
        _baseDir = baseDir ?? AppContext.BaseDirectory;

    public string FfmpegPath => Path.Combine(_baseDir, "ffmpeg", "ffmpeg.exe");
    public string FfprobePath => Path.Combine(_baseDir, "ffmpeg", "ffprobe.exe");

    public bool IsBundled => File.Exists(FfmpegPath);

    /// <summary>开发期回退：仓库根 spikes 环境/系统 PATH 均不保证，仅供本地开发验证用。</summary>
    public string ResolveForDevelopment()
    {
        if (IsBundled) return FfmpegPath;
        // 开发环境：允许 PATH 兜底并显式记日志（发布版绝不到这里，由安装包保证捆绑存在）
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("未找到 ffmpeg：发布版应随包捆绑于 <App>/ffmpeg/ffmpeg.exe", FfmpegPath);
    }
}
