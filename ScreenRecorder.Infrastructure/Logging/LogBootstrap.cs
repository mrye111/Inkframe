using Serilog;

namespace ScreenRecorder.Infrastructure.Logging;

/// <summary>
/// Serilog 引导：滚动文件日志写入 %LocalAppData%\Inkframe\Logs（需求文档 §57/§64）。
/// </summary>
public static class LogBootstrap
{
    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inkframe", "Logs");

    public static Serilog.Core.Logger CreateLogger()
    {
        Directory.CreateDirectory(LogDirectory);
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("App", "Inkframe")
            .WriteTo.File(
                Path.Combine(LogDirectory, "inkframe-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
