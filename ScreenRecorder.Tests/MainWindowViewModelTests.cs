using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class FakeManager : IRecordingManager
    {
        public RecordingState State { get; private set; } = RecordingState.Idle;
        public RecordingSession? CurrentSession => null;
        public TimeSpan Elapsed => TimeSpan.FromSeconds(42);
        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
        public event EventHandler<string>? RecordingCompleted;

        public RecordingRequest? LastRequest;

        public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            await Task.Delay(10);   // 不跑真倒计时
            SetState(RecordingState.Recording);
        }
        public Task PauseAsync(CancellationToken ct = default) { SetState(RecordingState.Paused); return Task.CompletedTask; }
        public Task ResumeAsync(CancellationToken ct = default) { SetState(RecordingState.Recording); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) { SetState(RecordingState.Idle); RecordingCompleted?.Invoke(this, "out.mp4"); return Task.CompletedTask; }

        private void SetState(RecordingState next) =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs { OldState = State, NewState = next });
    }

    private sealed class FakeCatalog : IWindowCatalog
    {
        public IReadOnlyList<RecordableWindowInfo> GetRecordableWindows() =>
            [new("ABCD1234", "记事本", "notepad")];
    }

    [Fact]
    public void Default_State_Is_Ready()
    {
        var vm = new MainWindowViewModel(new FakeManager(), new FakeCatalog());
        Assert.Equal(RecordingMode.FullScreen, vm.SelectedMode);
        Assert.Equal("●  Start Record", vm.RecordButtonContent);
        Assert.Equal("Ready to record?", vm.HeroText);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public void Window_Mode_Requires_Selection()
    {
        var vm = new MainWindowViewModel(new FakeManager(), new FakeCatalog());
        vm.SelectModeCommand.Execute("Window");
        Assert.True(vm.IsWindowMode);
        Assert.False(vm.CanStart);   // 未选窗口禁录
        vm.SelectedWindow = vm.Windows[0];
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task Toggle_Starts_With_FullScreen_Request_And_Turns_Stop()
    {
        var manager = new FakeManager();
        var vm = new MainWindowViewModel(manager, new FakeCatalog());
        await vm.RecordToggleCommand.ExecuteAsync(null);
        Assert.Equal(RecordingMode.FullScreen, manager.LastRequest!.Mode);
        Assert.Equal("■  Stop", vm.RecordButtonContent);   // §20 平滑转 Stop
        Assert.Equal("", vm.HeroText);
    }

    [Fact]
    public void ModeCard_Converter_Roundtrip()
    {
        var vm = new MainWindowViewModel(new FakeManager(), new FakeCatalog());
        vm.SelectModeCommand.Execute("Region");
        Assert.Equal(RecordingMode.Region, vm.SelectedMode);
        vm.SelectModeCommand.Execute("FullScreen");
        Assert.Equal(RecordingMode.FullScreen, vm.SelectedMode);
    }
}
