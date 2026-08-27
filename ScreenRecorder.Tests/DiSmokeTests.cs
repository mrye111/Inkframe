using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Infrastructure;
using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Tests;

public sealed class DiSmokeTests
{
    private static ServiceProvider BuildProvider() => new ServiceCollection()
        .AddInfrastructure()
        .AddSingleton<IRecordingManager, RecordingManager>()
        .BuildServiceProvider();

    [Fact]
    public void Container_Resolves_Core_Services()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<ConfigService>());
        Assert.NotNull(provider.GetRequiredService<IRecordingManager>());
    }

    [Fact]
    public async Task RecordingManager_StateMachine_Enforces_Transitions()
    {
        using var provider = BuildProvider();
        var manager = provider.GetRequiredService<IRecordingManager>();

        Assert.Equal(RecordingState.Idle, manager.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PauseAsync());
    }

    [Fact]
    public async Task RecordingManager_Full_Lifecycle_Roundtrip()
    {
        using var provider = BuildProvider();
        var manager = provider.GetRequiredService<IRecordingManager>();

        await manager.StartAsync(new RecordingRequest { Mode = RecordingMode.FullScreen });
        Assert.Equal(RecordingState.Recording, manager.State);

        await manager.PauseAsync();
        Assert.Equal(RecordingState.Paused, manager.State);

        await manager.ResumeAsync();
        Assert.Equal(RecordingState.Recording, manager.State);

        await manager.StopAsync();
        Assert.Equal(RecordingState.Idle, manager.State);
        Assert.Null(manager.CurrentSession);
    }
}
