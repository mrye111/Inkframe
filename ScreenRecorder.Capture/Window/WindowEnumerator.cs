using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.Windows;

namespace ScreenRecorder.Capture.Window;

/// <summary>
/// 可录制窗口枚举（§10）。过滤链依据 research/wgc-wpf-details.md §2：
/// 可见 + 非 Cloaked（UWP 壳）+ 有标题 + 非工具窗 + 排除自身进程。
/// </summary>
public static class WindowEnumerator
{
    public sealed record RecordableWindow(IntPtr Hwnd, string Title, string ProcessName);

    public static IReadOnlyList<RecordableWindow> GetRecordableWindows()
    {
        var result = new List<RecordableWindow>();
        var ownPid = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (IsCloaked(hwnd)) return true;
            if (GetWindowTextLength(hwnd) <= 0) return true;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == ownPid) return true;   // 防自录

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var processName = ProcessNameOf(pid);
            result.Add(new RecordableWindow(hwnd, title, processName));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        // DWMWA_CLOAKED：排除虚拟桌面/UWP 壳窗口（调研坑表必查项）
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static string ProcessNameOf(uint pid)
    {
        try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
        catch { return ""; }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);
}
