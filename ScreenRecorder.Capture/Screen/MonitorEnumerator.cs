using System.Runtime.InteropServices;
using ScreenRecorder.Core.Devices;
using TerraFX.Interop.Windows;

namespace ScreenRecorder.Capture.Screen;

/// <summary>
/// 多显示器枚举（§51）：虚拟坐标系矩形 + Per-Monitor DPI（§52）。
/// 依据 research/wgc-wpf-details.md §4：WGC 帧为物理像素，需 GetDpiForMonitor 换算。
/// </summary>
public static class MonitorEnumerator
{
    public sealed record MonitorRecord(
        string DeviceName, ScreenRectVirtual Bounds, uint Dpi, IntPtr Handle, bool IsPrimary);

    /// <summary>虚拟坐标系矩形（可为负，多显示器左/上排列时）。</summary>
    public readonly record struct ScreenRectVirtual(int X, int Y, int Width, int Height);

    public static unsafe IReadOnlyList<MonitorRecord> GetMonitors()
    {
        var result = new List<MonitorRecord>();
        var callback = new MONITORENUMPROC((hmon, _, rect, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfo(hmon, ref info))
            {
                GetDpiForMonitor(hmon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
                result.Add(new MonitorRecord(
                    info.szDevice.ToString().TrimEnd('\0'),
                    new ScreenRectVirtual(info.rcMonitor.left, info.rcMonitor.top,
                        info.rcMonitor.right - info.rcMonitor.left, info.rcMonitor.bottom - info.rcMonitor.top),
                    dpiX == 0 ? 96 : dpiX,
                    hmon,
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        });
        EnumDisplayMonitors(HDC.NULL, IntPtr.Zero, callback, default);
        return result;
    }

    /// <summary>按设备名找显示器；null/找不到 → 主显示器。</summary>
    public static MonitorRecord Resolve(string? deviceName)
    {
        var all = GetMonitors();
        if (deviceName is not null)
        {
            var hit = all.FirstOrDefault(m => m.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return all.First(m => m.IsPrimary);
    }

    /// <summary>包含指定虚拟坐标点的显示器。</summary>
    public static MonitorRecord FromVirtualPoint(int x, int y)
    {
        var all = GetMonitors();
        return all.FirstOrDefault(m =>
            x >= m.Bounds.X && x < m.Bounds.X + m.Bounds.Width &&
            y >= m.Bounds.Y && y < m.Bounds.Y + m.Bounds.Height) ?? all.First(m => m.IsPrimary);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern BOOL EnumDisplayMonitors(HDC hdc, IntPtr lprcClip, MONITORENUMPROC lpfnEnum, LPARAM dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern BOOL GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("Shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

    private delegate BOOL MONITORENUMPROC(IntPtr hmonitor, HDC hdc, IntPtr lprc, LPARAM lparam);

    private const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private enum MONITOR_DPI_TYPE { MDT_EFFECTIVE_DPI = 0 }
}
