using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.UI.ViewModels;

/// <summary>
/// 悬浮录制条（#16，§21/§31-35）：时长、录制红点状态、暂停/继续、停止、实时音量。
/// 动画只允许 Opacity/Transform（录制期 GPU 预算规则，research §3.4）。
/// </summary>
public partial class FloatingBarViewModel : ObservableObject
{
    private readonly IRecordingManager _recordingManager;
    private readonly IAudioCaptureService _audio;

    [ObservableProperty]
    private string _timeText = "00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(PauseButtonContent))]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private float _level;

    /// <summary>5 根音量柱高度（§35 实时反馈；随 Level 重算，末柱阈值最高）。</summary>
    [ObservableProperty]
    private double[] _vuBars = [3, 3, 3, 3, 3];

    partial void OnLevelChanged(float value)
    {
        var bars = new double[5];
        for (var i = 0; i < 5; i++)
        {
            var threshold = (i + 1) / 5.0;
            var fill = Math.Clamp((value - (threshold - 0.2)) / 0.2, 0, 1);
            bars[i] = 3 + fill * 13;   // 3..16px
        }
        VuBars = bars;
    }

    public bool IsPaused => State == RecordingState.Paused;

    /// <summary>暂停图标 ⇄ 播放图标（暂停态琥珀色由 View 绑定 IsPaused 换色）。</summary>
    public string PauseButtonContent => IsPaused ? "▶" : "❚❚";

    public FloatingBarViewModel(IRecordingManager recordingManager, IAudioCaptureService audio)
    {
        _recordingManager = recordingManager;
        _audio = audio;
        State = _recordingManager.State;   // 初始同步（悬浮条可能在录制中途创建）
        _recordingManager.StateChanged += (_, e) => State = e.NewState;
        _audio.LevelChanged += (_, v) => Level = v;
    }

    /// <summary>由 View 的 DispatcherTimer 每 500ms 调用（计时显示不走后台线程）。</summary>
    public void TickElapsed()
    {
        var e = _recordingManager.Elapsed;
        TimeText = $"{(int)e.TotalHours:D2}:{e.Minutes:D2}:{e.Seconds:D2}";
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (State == RecordingState.Recording)
            await _recordingManager.PauseAsync();
        else if (State == RecordingState.Paused)
            await _recordingManager.ResumeAsync();
    }

    [RelayCommand]
    private async Task StopAsync() => await _recordingManager.StopAsync();
}
