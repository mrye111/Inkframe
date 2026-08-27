using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Core.Recording;

/// <summary>录制编排状态机骨架（§7.1）。采集/编码接线在 #10 完成。</summary>
public sealed class RecordingManager : IRecordingManager
{
    private readonly ConfigService _config;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public RecordingSession? CurrentSession { get; private set; }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    public RecordingManager(ConfigService config) => _config = config;

    public Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        EnsureState(RecordingState.Idle);
        CurrentSession = new RecordingSession { Request = request };
        TransitionTo(RecordingState.Countdown);
        // #10：倒计时（§20）→ 启动采集/编码 → Recording
        TransitionTo(RecordingState.Recording);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        EnsureState(RecordingState.Recording);
        CurrentSession?.MarkPaused();
        TransitionTo(RecordingState.Paused);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken ct = default)
    {
        EnsureState(RecordingState.Paused);
        CurrentSession?.MarkResumed();
        TransitionTo(RecordingState.Recording);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (State is not (RecordingState.Recording or RecordingState.Paused))
            throw new InvalidOperationException($"当前状态 {State} 不允许停止");
        TransitionTo(RecordingState.Stopping);
        // #10：Flush 编码 → 封装 MP4 → 写录制历史
        CurrentSession?.Dispose();
        CurrentSession = null;
        TransitionTo(RecordingState.Idle);
        return Task.CompletedTask;
    }

    private void EnsureState(RecordingState expected)
    {
        if (State != expected)
            throw new InvalidOperationException($"期望状态 {expected}，当前 {State}");
    }

    private void TransitionTo(RecordingState next)
    {
        var old = State;
        State = next;
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs { OldState = old, NewState = next });
    }
}
