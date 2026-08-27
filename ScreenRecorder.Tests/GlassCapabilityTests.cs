using ScreenRecorder.UI.Themes;

namespace ScreenRecorder.Tests;

public sealed class GlassCapabilityTests
{
    [Fact]
    public void Win11_22H2_Gets_MicaAlt_And_Acrylic()
    {
        var plan = GlassCapability.Resolve(22631, transparencyEnabled: true);
        Assert.Equal(InkBackdrop.MicaAlt, plan.MainWindow);
        Assert.Equal(InkBackdrop.Acrylic, plan.FloatingBar);
        Assert.False(plan.OpaqueFallback);
    }

    [Fact]
    public void Win11_21H2_Tries_Undocumented_Mica_With_Opaque_Ready()
    {
        var plan = GlassCapability.Resolve(22000, transparencyEnabled: true);
        Assert.Equal(InkBackdrop.None, plan.MainWindow);
        Assert.True(plan.TryUndocumentedMica);
        Assert.True(plan.OpaqueFallback);   // 自绘层必须能独立成立
    }

    [Fact]
    public void Win10_Falls_Back_To_Opaque()
    {
        var plan = GlassCapability.Resolve(19045, transparencyEnabled: true);
        Assert.Equal(InkBackdrop.None, plan.MainWindow);
        Assert.True(plan.OpaqueFallback);
        Assert.False(plan.TryUndocumentedMica);
    }

    [Fact]
    public void Transparency_Off_Always_Opaque()
    {
        var plan = GlassCapability.Resolve(26100, transparencyEnabled: false);
        Assert.Equal(InkBackdrop.None, plan.MainWindow);
        Assert.Equal(InkBackdrop.None, plan.FloatingBar);
        Assert.True(plan.OpaqueFallback);
    }
}
