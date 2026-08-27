using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;

namespace ScreenRecorder.Capture.Cursor;

/// <summary>
/// 光标自绘叠加（§30，调研结论：关闭 WGC 自带光标后合成阶段自绘）。
/// 实现：采集帧缓冲本身就是 32bpp top-down DIB Section——回读直接写入 DIB 像素位，
/// 光标用 DrawIconEx 画进同一 DC，编码器从 DIB 位读走，全程零额外拷贝。
/// 高亮：录制红（#FF453A）空心圆环。
/// </summary>
public sealed unsafe class CursorOverlay : IDisposable
{
    private readonly IntPtr _hdc;
    private readonly IntPtr _dib;
    private readonly void* _bits;
    private readonly IntPtr _highlightPen;
    private readonly int _width, _height;
    private int _monitorOriginX, _monitorOriginY;   // 显示器虚拟坐标原点
    private double _scale = 1.0;                     // DIP→物理像素
    private int _cropX, _cropY;                      // 区域模式裁剪偏移（物理像素）
    private bool _enabled, _highlight;

    public CursorOverlay(int width, int height)
    {
        _width = width; _height = height;
        _hdc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,   // 负值 = top-down，与帧行序一致
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0     // BI_RGB
            }
        };
        void* bits;
        _dib = CreateDIBSection(_hdc, ref bmi, 0, &bits, IntPtr.Zero, 0);
        _bits = bits;
        SelectObject(_hdc, _dib);
        _highlightPen = CreatePen(0, 4, 0x003A45FF);   // #FF453A（BGR）
    }

    public void* Bits => _bits;
    public int ByteCount => _width * _height * 4;

    public void Configure(int monitorOriginX, int monitorOriginY, double scale, int cropX, int cropY, bool enabled, bool highlight)
    {
        _monitorOriginX = monitorOriginX; _monitorOriginY = monitorOriginY;
        _scale = scale; _cropX = cropX; _cropY = cropY;
        _enabled = enabled; _highlight = highlight;
    }

    /// <summary>在帧缓冲（已拷入 DIB）上叠加光标。输出坐标系 = 裁剪后帧。</summary>
    public void Draw()
    {
        if (!_enabled) return;
        var ci = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING) return;

        // 虚拟坐标 → 显示器本地物理 → 裁剪后帧坐标
        var x = (int)((ci.ptScreenPos.x - _monitorOriginX) * _scale) - _cropX;
        var y = (int)((ci.ptScreenPos.y - _monitorOriginY) * _scale) - _cropY;
        if (x < -64 || y < -64 || x > _width + 64 || y > _height + 64) return;   // 光标不在画面

        if (_highlight)
        {
            var oldPen = SelectObject(_hdc, _highlightPen);
            var oldBrush = SelectObject(_hdc, GetStockObject(5));   // NULL_BRUSH
            Ellipse(_hdc, x - 24, y - 24, x + 24, y + 24);
            SelectObject(_hdc, oldPen);
            SelectObject(_hdc, oldBrush);
        }
        DrawIconEx(_hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
    }

    private const int CURSOR_SHOWING = 0x1;
    private const int DI_NORMAL = 0x3;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public uint cbSize; public uint flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, void** ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int iStyle, int cWidth, int color);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    public void Dispose()
    {
        DeleteObject(_highlightPen);
        DeleteObject(_dib);
        DeleteDC(_hdc);
    }
}
