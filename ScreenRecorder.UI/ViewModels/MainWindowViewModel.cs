using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.UI.ViewModels;

/// <summary>
/// 首页（#15）：模式选择 → 录制/停止。
/// 状态机驱动：Idle/Countdown/Recording/Paused/Stopping → 按钮与文案。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IRecordingManager _recordingManager;
    private readonly IWindowCatalog _windowCatalog;

    /// <summary>区域选择完成回调（由 View 注入区域覆盖层交互，VM 不碰窗口）。</summary>
    public Func<Task<ScreenRect?>>? RegionPicker { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowMode))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private RecordingMode _selectedMode = RecordingMode.FullScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanToggleRecord))]
    [NotifyPropertyChangedFor(nameof(RecordButtonContent))]
    [NotifyPropertyChangedFor(nameof(HeroText))]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanToggleRecord))]
    private RecordableWindowInfo? _selectedWindow;

    public IReadOnlyList<RecordableWindowInfo> Windows => _windowCatalog.GetRecordableWindows();

    public bool IsWindowMode => SelectedMode == RecordingMode.Window;
    public bool IsBusy => State != RecordingState.Idle;
    public bool CanStart => State == RecordingState.Idle
        && (SelectedMode != RecordingMode.Window || SelectedWindow is not null);

    /// <summary>胶囊按钮内容（§20：● Start Record → ■ Stop 平滑变化，不刷新页面）。</summary>
    public string RecordButtonContent => State switch
    {
        RecordingState.Idle => "●  Start Record",
        RecordingState.Countdown => "准备中…",
        RecordingState.Stopping => "正在保存…",
        _ => "■  Stop"
    };

    public string HeroText => State == RecordingState.Idle ? "Ready to record?" : "";

    public MainWindowViewModel(IRecordingManager recordingManager, IWindowCatalog windowCatalog)
    {
        _recordingManager = recordingManager;
        _windowCatalog = windowCatalog;
        _recordingManager.StateChanged += (_, e) => State = e.NewState;
        _recordingManager.RecordingCompleted += (_, path) => StatusText = "已保存：" + path;
    }

    [RelayCommand]
    private void SelectMode(string mode)
    {
        if (IsBusy) return;
        SelectedMode = Enum.Parse<RecordingMode>(mode);
        if (IsWindowMode) OnPropertyChanged(nameof(Windows));   // 刷新窗口列表
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        try
        {
            var request = SelectedMode switch
            {
                RecordingMode.Window => new RecordingRequest
                {
                    Mode = RecordingMode.Window,
                    TargetWindowHandle = SelectedWindow!.HandleHex
                },
                RecordingMode.Region => await BuildRegionRequestAsync(),
                _ => new RecordingRequest { Mode = RecordingMode.FullScreen }
            };
            if (request is null) return;   // 用户取消区域选择

            StatusText = "";
            await Task.Run(() => _recordingManager.StartAsync(request));   // 倒计时 3s 不卡 UI
        }
        catch (InsufficientDiskSpaceException ex)
        {
            StatusText = ex.Message;   // §27 明确提示
        }
        catch (Exception ex)
        {
            StatusText = "启动失败：" + ex.Message;
        }
    }

    private async Task<RecordingRequest?> BuildRegionRequestAsync()
    {
        if (RegionPicker is null) return null;
        var region = await RegionPicker();
        return region is null ? null : new RecordingRequest { Mode = RecordingMode.Region, Region = region.Value };
    }

    /// <summary>录制/停止合一（§20：按钮平滑转 ■ Stop）。倒计时/保存中禁停。</summary>
    [RelayCommand(CanExecute = nameof(CanToggleRecord))]
    private async Task RecordToggleAsync()
    {
        if (State == RecordingState.Idle)
            await StartAsync();
        else
            await _recordingManager.StopAsync();
    }

    public bool CanToggleRecord => State is RecordingState.Idle or RecordingState.Recording or RecordingState.Paused
        && (State != RecordingState.Idle || CanStart);
}
