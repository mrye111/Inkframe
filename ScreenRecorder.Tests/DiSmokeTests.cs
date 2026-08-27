using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Infrastructure;
using ScreenRecorder.Infrastructure.Configuration;
using ScreenRecorder.Infrastructure.Diagnostics;

namespace ScreenRecorder.Tests;

public sealed class DiSmokeTests
{
    [Fact]
    public void Container_Resolves_Infrastructure_Services()
    {
        using var provider = new ServiceCollection()
            .AddInfrastructure()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ConfigService>());
        Assert.NotNull(provider.GetRequiredService<SessionMarkerStore>());
    }
}
