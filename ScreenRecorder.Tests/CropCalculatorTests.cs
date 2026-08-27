using ScreenRecorder.Capture.Screen;
using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.Tests;

public sealed class CropCalculatorTests
{
    // 显示器：虚拟坐标 (0,0) 1920x1080，DPI 96（1x），帧 1920x1080
    [Fact]
    public void Region_1x_Scale_Maps_Directly()
    {
        var crop = CropCalculator.TryCompute(
            new ScreenRect(100, 50, 640, 480), (0, 0, 1920, 1080), 96, 1920, 1080);
        Assert.NotNull(crop);
        Assert.Equal(new CropCalculator.CropRect(100, 50, 640, 480), crop!.Value);
    }

    // 显示器 150% 缩放（DPI 144）：虚拟 1280x720 ↔ 物理 1920x1080
    [Fact]
    public void Region_150_Percent_Scales_To_Physical()
    {
        var crop = CropCalculator.TryCompute(
            new ScreenRect(100, 50, 640, 480), (0, 0, 1280, 720), 144, 1920, 1080);
        Assert.NotNull(crop);
        Assert.Equal(new CropCalculator.CropRect(150, 75, 960, 720), crop!.Value);
    }

    [Fact]
    public void Odd_Size_Rounds_Down_To_Even()
    {
        var crop = CropCalculator.TryCompute(
            new ScreenRect(0, 0, 641, 479), (0, 0, 1920, 1080), 96, 1920, 1080);
        Assert.Equal(640, crop!.Value.Width);
        Assert.Equal(478, crop.Value.Height);
    }

    [Fact]
    public void Center_Outside_Monitor_Returns_Null()
    {
        var crop = CropCalculator.TryCompute(
            new ScreenRect(-3000, 0, 640, 480), (0, 0, 1920, 1080), 96, 1920, 1080);
        Assert.Null(crop);
    }

    [Fact]
    public void Region_Clamped_To_Frame_Edge()
    {
        // 中心在显示器内但右下越界 → 夹紧到帧内
        var crop = CropCalculator.TryCompute(
            new ScreenRect(1500, 900, 600, 300), (0, 0, 1920, 1080), 96, 1920, 1080);
        Assert.NotNull(crop);
        Assert.True(crop!.Value.X + crop.Value.Width <= 1920);
        Assert.True(crop.Value.Y + crop.Value.Height <= 1080);
        Assert.Equal(0, crop.Value.Width % 2);
    }

    // 多显示器：第二屏虚拟坐标 (-1920, 0)
    [Fact]
    public void Second_Monitor_Negative_Virtual_Coords()
    {
        var crop = CropCalculator.TryCompute(
            new ScreenRect(-1800, 100, 640, 480), (-1920, 0, 1920, 1080), 96, 1920, 1080);
        Assert.NotNull(crop);
        Assert.Equal(new CropCalculator.CropRect(120, 100, 640, 480), crop!.Value);
    }
}
