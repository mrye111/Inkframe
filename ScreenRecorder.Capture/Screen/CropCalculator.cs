using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.Capture.Screen;

/// <summary>
/// 区域裁剪计算（§11）：虚拟坐标矩形 → 显示器本地物理像素矩形。
/// WGC 帧为物理像素（research §4），按显示器 DPI 缩放；H.264/yuv420p 要求偶数尺寸。
/// 纯函数，供单测覆盖。
/// </summary>
public static class CropCalculator
{
    public readonly record struct CropRect(int X, int Y, int Width, int Height);

    /// <param name="region">虚拟坐标系矩形</param>
    /// <param name="monitorBounds">目标显示器虚拟坐标边界</param>
    /// <param name="monitorDpi">显示器有效 DPI（96 = 100%）</param>
    /// <param name="frameWidth/Height">WGC 帧物理尺寸</param>
    public static CropRect? TryCompute(
        ScreenRect region,
        (int X, int Y, int Width, int Height) monitorBounds,
        uint monitorDpi, int frameWidth, int frameHeight)
    {
        // 区域中心点必须落在该显示器内
        var cx = region.X + region.Width / 2;
        var cy = region.Y + region.Height / 2;
        if (cx < monitorBounds.X || cx >= monitorBounds.X + monitorBounds.Width ||
            cy < monitorBounds.Y || cy >= monitorBounds.Y + monitorBounds.Height)
            return null;

        var scale = monitorDpi / 96.0;
        // 虚拟坐标 → 显示器本地 DIP → 物理像素
        var localX = (int)Math.Round((region.X - monitorBounds.X) * scale);
        var localY = (int)Math.Round((region.Y - monitorBounds.Y) * scale);
        var w = (int)Math.Round(region.Width * scale);
        var h = (int)Math.Round(region.Height * scale);

        // 夹紧到帧边界
        localX = Math.Clamp(localX, 0, frameWidth - 2);
        localY = Math.Clamp(localY, 0, frameHeight - 2);
        w = Math.Min(w, frameWidth - localX);
        h = Math.Min(h, frameHeight - localY);

        // yuv420p 要求偶数
        w &= ~1;
        h &= ~1;
        if (w < 2 || h < 2) return null;
        return new CropRect(localX, localY, w, h);
    }
}
