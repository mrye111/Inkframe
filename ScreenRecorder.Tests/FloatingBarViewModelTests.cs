using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Tests;

public sealed class FloatingBarViewModelTests
{
    private sealed class FakeManager : IRecordingManager
    {
        public RecordingState State { get; private set; } = RecordingState.Recording;
        public RecordingSession? CurrentSession => null;
        public TimeSpan Elapsed => TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(32);
        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
        public event EventHandler<string>? RecordingCompleted { add { } remove { } }
        public bool Paused, Resumed, Stopped;

        public Task StartAsync(RecordingRequest r, CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) { Paused = true; SetState(RecordingState.Paused); return Task.CompletedTask; }
        public Task ResumeAsync(CancellationToken ct = default) { Resumed = true; SetState(RecordingState.Recording); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) { Stopped = true; return Task.CompletedTask; }
        private void SetState(RecordingState next) =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs { OldState = State, NewState = next });
    }

    private sealed class FakeAudio : IAudioCaptureService
    {
        public event EventHandler<AudioBuffer>? BufferReady { add { } remove { } }
        public event EventHandler<string>? DeviceDisconnected { add { } remove { } }
        public event EventHandler<float>? LevelChanged;
        public Task StartAsync(bool s, bool m, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void EmitLevel(float v) => LevelChanged?.Invoke(this, v);
    }

    [Fact]
    public void TimeText_Formats_HHMMSS()
    {
        var vm = new FloatingBarViewModel(new FakeManager(), new FakeAudio());
        vm.TickElapsed();
        Assert.Equal("00:04:32", vm.TimeText);
    }

    [Fact]
    public async Task Pause_Toggles_State_And_Icon()
    {
        var manager = new FakeManager();
        var vm = new FloatingBarViewModel(manager, new FakeAudio());
        Assert.Equal("❚❚", vm.PauseButtonContent);

        await vm.TogglePauseCommand.ExecuteAsync(null);
        Assert.True(manager.Paused);
        Assert.True(vm.IsPaused);
        Assert.Equal("▶", vm.PauseButtonContent);
    }

    [Fact]
    public void VuBars_Scale_With_Level()
    {
        var audio = new FakeAudio();
        var vm = new FloatingBarViewModel(new FakeManager(), audio);

        audio.EmitLevel(0f);
        Assert.All(vm.VuBars, h => Assert.Equal(3, h));   // 全低

        audio.EmitLevel(1f);
        Assert.All(vm.VuBars, h => Assert.True(h > 14));   // 全高

        audio.EmitLevel(0.5f);
        Assert.True(vm.VuBars[0] > vm.VuBars[4]);   // 前柱高于末柱
    }
}
