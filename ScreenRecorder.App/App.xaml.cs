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
        builder.Services.AddSingleton<IRecordingManager, RecordingManager>();
        builder.Services.AddSingleton<IScreenCaptureService, Capture.Screen.WgcScreenCaptureService>();
        builder.Services.AddSingleton<IAudioCaptureService, Audio.SystemAudio.WasapiAudioCaptureService>();
        builder.Services.AddSingleton<IVideoEncoder, Encoding.FFmpeg.FFmpegProcessEncoder>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<Serilog.Core.Logger>();
        Log.Logger = logger;
        Log.Information("Inkframe 启动，日志目录 {LogDirectory}", Infrastructure.Logging.LogBootstrap.LogDirectory);

        _tray = new TrayService(ShowMainWindow, () => Dispatcher.Invoke(Shutdown));

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
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
