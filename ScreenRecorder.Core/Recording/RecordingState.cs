namespace ScreenRecorder.Core.Recording;

/// <summary>录制状态机（§7.1 / §20-24）。</summary>
public enum RecordingState
{
    Idle,
    Countdown,   // §20 开始倒计时
    Recording,
    Paused,      // §22 暂停
    Stopping     // §23 停止封装中
}
