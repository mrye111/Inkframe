using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenRecorder.UI.Themes;

/// <summary>
/// DWM Backdrop 启用（research §3.2）。铁律：SetBackdrop 成功才能把 Background 设透明，
/// 失败路径必须不透明（否则全透明隐形窗）。
/// </summary>
public static class BackdropHelper
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;       // Win11 22H2+
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;  // 免费圆角
    private const int DWMWA_MICA_EFFECT = 1029;             // 未文档化（21H2 增强，失败即降级）

    /// <summary>
    /// 按降级计划应用材质。返回实际是否启用了 DWM 材质（false = 调用方保持不透明底）。
    /// </summary>
    public static bool Apply(Window window, InkBackdrop backdrop, bool tryUndocumentedMica = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var applied = false;
        if (backdrop != InkBackdrop.None && Environment.OSVersion.Version.Build >= GlassCapability.BuildWin11_22H2)
        {
            int v = (int)backdrop;
            applied = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, 4) == 0;
        }
        else if (tryUndocumentedMica)
        {
            int v = 1;
            applied = DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref v, 4) == 0;
        }

        if (applied)
        {
            // DWM 免费圆角（悬浮条白嫖圆角阴影，保持 GPU 加速）
            int corner = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            window.Background = Brushes.Transparent;   // 透明像素处 DWM 材质透出
        }
        // 失败：不改 Background，调用方的不透明底 + 自绘玻璃层接管

        return applied;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
