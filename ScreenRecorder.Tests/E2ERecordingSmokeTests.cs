using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Audio.SystemAudio;
using ScreenRecorder.Capture.Screen;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.Encoding.FFmpeg;
using ScreenRecorder.Infrastructure;
using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Tests;

/// <summary>
/// 端到端冒烟（#10 里程碑：「能录出第一个正式 MP4」）。
/// 默认跳过：设置环境变量 INKFRAME_E2E=1 后运行，真实走 WGC 采集 + ffmpeg 进程管线。
/// </summary>
public sealed class E2ERecordingSmokeTests
{
    [Fact]
    public async Task Records_First_Real_Mp4()
    {
        if (Environment.GetEnvironmentVariable("INKFRAME_E2E") != "1")
            return;   // 未开启：静默跳过（不污染常规测试）

        var outputDir = Path.Combine(Path.GetTempPath(), "inkframe-e2e-" + Guid.NewGuid().ToString("N"));
        var provider = new ServiceCollection()
            .AddInfrastructure()
            .AddSingleton<IScreenCaptureService, WgcScreenCaptureService>()
            .AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>()
            .AddTransient<FFmpegProcessEncoder>()
            .AddSingleton<Func<IVideoEncoder>>(sp => () => sp.GetRequiredService<FFmpegProcessEncoder>())
            .AddSingleton<IRecordingManager, RecordingManager>()
            .BuildServiceProvider();

        // 输出目录指向临时目录（不碰用户真实配置）
        var config = provider.GetRequiredService<ConfigService>();
        config.Current.OutputDirectory = outputDir;
        config.Save();

        var manager = provider.GetRequiredService<IRecordingManager>();
        string? completed = null;
        manager.RecordingCompleted += (_, path) => completed = path;

        await manager.StartAsync(new RecordingRequest { Mode = RecordingMode.FullScreen });
        await Task.Delay(5000);        // 录 5 秒
        await manager.PauseAsync();    // 暂停 1 秒（验证 §22 时间轴剔除）
        await Task.Delay(1000);
        await manager.ResumeAsync();
        await Task.Delay(3000);        // 再录 3 秒
        await manager.StopAsync();

        Assert.NotNull(completed);
        Assert.True(File.Exists(completed), "输出文件应存在");
        Assert.True(new FileInfo(completed).Length > 10_000, "输出文件应有实际内容");

        // 崩溃标记已清除（§56）
        Assert.Empty(provider.GetRequiredService<ScreenRecorder.Infrastructure.Diagnostics.SessionMarkerStore>()
            .FindOrphanedOutputs().Where(p => p == completed));

        Console.WriteLine("E2E output: " + completed);
    }
}
