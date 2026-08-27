using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenRecorder.App.Tray;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.Infrastructure;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;
using Serilog;

namespace ScreenRecorder.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Inkframe.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private TrayService? _tray;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // §53 全局异常兜底：先落盘再死（WinExe 无控制台，静默崩溃无法排障）
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TryLogFatal("AppDomain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            TryLogFatal("Dispatcher", args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        // 单实例（托盘场景下尤为重要，§41）
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Inkframe 已在运行。", "Inkframe",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddInfrastructure();
        builder.Services.AddSingleton<IScreenCaptureService, Capture.Screen.WgcScreenCaptureService>();
        builder.Services.AddSingleton<IWindowCatalog, Capture.Window.WindowCatalog>();
        builder.Services.AddSingleton<IAudioCaptureService, Audio.SystemAudio.WasapiAudioCaptureService>();
        builder.Services.AddTransient<Encoding.FFmpeg.FFmpegProcessEncoder>();
        builder.Services.AddSingleton<Func<IVideoEncoder>>(sp =>
            () => sp.GetRequiredService<Encoding.FFmpeg.FFmpegProcessEncoder>());
        builder.Services.AddSingleton<IRecordingManager, RecordingManager>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<FloatingBarViewModel>();
        builder.Services.AddSingleton<FloatingBarWindow>();
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<Serilog.Core.Logger>();
        Log.Logger = logger;
        Log.Information("Inkframe 启动，日志目录 {LogDirectory}", Infrastructure.Logging.LogBootstrap.LogDirectory);

        _tray = new TrayService(ShowMainWindow, () => Dispatcher.Invoke(Shutdown));

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // §21：录制开始 → 主窗最小化 + 悬浮条接管；结束 → 恢复
        var recordingManager = _host.Services.GetRequiredService<IRecordingManager>();
        var floatingBar = _host.Services.GetRequiredService<FloatingBarWindow>();

        // 开发验证开关：直接展示悬浮条（#16 截图验证用，发布版无害）
        if (Environment.GetEnvironmentVariable("INKFRAME_SHOW_FLOATBAR") == "1")
            floatingBar.ShowAndRun();
        recordingManager.StateChanged += (_, e2) => Dispatcher.Invoke(() =>
        {
            switch (e2.NewState)
            {
                case RecordingState.Recording:
                    if (e2.OldState == RecordingState.Countdown)
                    {
                        mainWindow.WindowState = System.Windows.WindowState.Minimized;
                        floatingBar.ShowAndRun();
                    }
                    break;
                case RecordingState.Idle:
                    floatingBar.HideAndStop();
                    ShowMainWindow();
                    break;
            }
        });
    }

    private static void TryLogFatal(string source, Exception? ex)
    {
        try
        {
            if (ex is not null)
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Infrastructure.Logging.LogBootstrap.LogDirectory, "fatal.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {source}: {ex}\n\n");
        }
        catch { /* 兜底路径不抛 */ }
    }

    private void ShowMainWindow()
    {
        if (_host?.Services.GetService<MainWindow>() is { } window)
        {
            window.Show();
            window.WindowState = System.Windows.WindowState.Normal;
            window.Activate();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _tray?.Dispose();
        _host?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
        Log.Information("Inkframe 退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
