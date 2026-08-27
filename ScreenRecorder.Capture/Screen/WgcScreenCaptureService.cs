using System.Runtime.InteropServices;
using ScreenRecorder.Capture.Cursor;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.Native.Capture;
using TerraFX.Interop.DirectX;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace ScreenRecorder.Capture.Screen;

/// <summary>
/// WGC 画面采集（#12）：全屏 / 窗口 / 区域三模式 + 多显示器 + DPI + 光标自绘。
/// 路径依据 research/wgc-wpf-details.md 与 #3 Spike 验证。
///
/// 帧管线：WGC 帧 → staging（GPU→CPU 回读）→ 裁剪行拷贝进 DIB 位 → 光标叠加 →
/// 编码器直接从 DIB 位读取（零多余拷贝）。
///
/// 已知取舍：裁剪在 CPU 行拷贝阶段完成（进程管线本就需回读，等价于 GPU 侧
/// CopySubresourceRegion 的成本，省一次纹理中转）；SizeChanged 时丢帧至尺寸稳定
/// （编码器不支持中途改尺寸，见 §53 异常处理，细化留性能验证阶段）。
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService, IDisposable
{
    private unsafe ID3D11Device* _device;
    private unsafe ID3D11DeviceContext* _context;
    private unsafe ID3D11Texture2D* _staging;

    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private CursorOverlay? _overlay;
    private RecordingRequest _request = null!;

    private int _frameWidth, _frameHeight;        // WGC 帧物理尺寸
    private int _outWidth, _outHeight;            // 输出尺寸（区域=裁剪后）
    private CropCalculator.CropRect? _crop;
    private MonitorEnumerator.MonitorRecord? _monitor;
    private volatile bool _running;

    public event EventHandler<VideoFrame>? FrameArrived;

    public (int Width, int Height) CurrentSize => (_outWidth, _outHeight);

    public Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        if (_running) throw new InvalidOperationException("采集已在进行");
        _request = request;

        unsafe { InitDevice(); }

        _item = request.Mode switch
        {
            RecordingMode.Window => CreateWindowItem(request),
            RecordingMode.Region => CreateRegionMonitorItem(request),
            _ => CreateFullScreenItem(request)
        };
        _frameWidth = _item.Size.Width;
        _frameHeight = _item.Size.Height;

        // 区域模式：计算裁剪矩形（虚拟坐标 → 物理像素，偶数对齐）
        if (request.Mode == RecordingMode.Region && request.Region is { } region && _monitor is not null)
        {
            _crop = CropCalculator.TryCompute(region,
                (_monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height),
                _monitor.Dpi, _frameWidth, _frameHeight)
                ?? throw new ArgumentException("区域不在目标显示器内或尺寸过小", nameof(request));
            _outWidth = _crop.Value.Width;
            _outHeight = _crop.Value.Height;
        }
        else
        {
            _outWidth = _frameWidth & ~1;
            _outHeight = _frameHeight & ~1;
        }

        _overlay = new CursorOverlay(_outWidth, _outHeight);
        var scale = (_monitor?.Dpi ?? 96) / 96.0;
        _overlay.Configure(
            _monitor?.Bounds.X ?? 0, _monitor?.Bounds.Y ?? 0, scale,
            _crop?.X ?? 0, _crop?.Y ?? 0,
            request.RecordCursor, request.HighlightCursor);

        unsafe { CreateStagingAndSession(); }
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _running = false;
        if (_session is not null) _session.Dispose();
        if (_framePool is not null) _framePool.Dispose();
        _session = null;
        _framePool = null;
        return Task.CompletedTask;
    }

    // ---------- 采集目标解析 ----------

    private GraphicsCaptureItem CreateFullScreenItem(RecordingRequest request)
    {
        _monitor = MonitorEnumerator.Resolve(request.TargetMonitor);
        return WgcInterop.CreateForMonitor(_monitor.Handle);
    }

    private GraphicsCaptureItem CreateRegionMonitorItem(RecordingRequest request)
    {
        var region = request.Region ?? throw new ArgumentException("区域模式必须提供 Region", nameof(request));
        _monitor = MonitorEnumerator.FromVirtualPoint(
            region.X + region.Width / 2, region.Y + region.Height / 2);
        return WgcInterop.CreateForMonitor(_monitor.Handle);
    }

    private GraphicsCaptureItem CreateWindowItem(RecordingRequest request)
    {
        if (request.TargetWindowHandle is null)
            throw new ArgumentException("窗口模式必须提供 TargetWindowHandle", nameof(request));
        var hwnd = (IntPtr)Convert.ToInt64(request.TargetWindowHandle, 16);

        // 光标坐标系需要宿主显示器：取窗口中心所在显示器
        GetWindowRect(hwnd, out var rect);
        _monitor = MonitorEnumerator.FromVirtualPoint(
            (rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
        return WgcInterop.CreateForWindow(hwnd);
    }

    // ---------- 原生初始化 ----------

    private unsafe void InitDevice()
    {
        ID3D11Device* device;
        D3D_FEATURE_LEVEL fl;
        var levels = stackalloc D3D_FEATURE_LEVEL[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 };
        var hr = D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, HMODULE.NULL,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            levels, 1, (uint)TerraFX.Interop.DirectX.D3D11.D3D11_SDK_VERSION, &device, &fl, null);
        WgcInterop.Check(hr.Value, "D3D11CreateDevice");
        _device = device;
        ID3D11DeviceContext* context;
        _device->GetImmediateContext(&context);
        _context = context;
        _winrtDevice = WgcInterop.CreateWinRtDevice(_device);
    }

    private unsafe void CreateStagingAndSession()
    {
        ID3D11Texture2D* staging;
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_frameWidth,
            Height = (uint)_frameHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };
        WgcInterop.Check(_device->CreateTexture2D(&desc, null, &staging).Value, "CreateTexture2D(staging)");
        _staging = staging;

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item!.Size);
        _session = _framePool.CreateCaptureSession(_item);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            _session.IsCursorCaptureEnabled = false;   // 自绘光标（调研结论）

        _framePool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
    }

    // ---------- 帧泵 ----------

    /// <summary>帧尺寸变化时重建 FramePool（22621 投影无 SizeChanged 事件，帧回调检测替代）。</summary>
    private void RebuildPool(Windows.Graphics.SizeInt32 newSize)
    {
        if (!_running) return;
        _framePool?.Dispose();
        _frameWidth = newSize.Width;
        _frameHeight = newSize.Height;
        // 输出尺寸不变（编码器固定）；新帧小于裁剪需求时 PumpAndRaise 的边界检查丢帧
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
        _framePool.FrameArrived += OnFrameArrived;
        _session = _framePool.CreateCaptureSession(_item!);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        if (!_running) return;
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        // 尺寸变化（窗口缩放/显示器热插拔，§53）：重建池并丢帧至稳定；编码器尺寸固定
        if (frame.ContentSize.Width != _frameWidth || frame.ContentSize.Height != _frameHeight)
        {
            RebuildPool(frame.ContentSize);
            return;
        }
        unsafe { PumpAndRaise(frame.Surface); }
    }

    private unsafe void PumpAndRaise(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        if ((_crop?.X ?? 0) + _outWidth > _frameWidth || (_crop?.Y ?? 0) + _outHeight > _frameHeight)
            return;   // 尺寸变化后裁剪矩形越界：丢帧
        var tex = WgcInterop.GetTexture2D(surface);
        if (tex == null) return;
        _context->CopyResource((ID3D11Resource*)_staging, (ID3D11Resource*)tex);
        tex->Release();

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (!_context->Map((ID3D11Resource*)_staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED)
            return;

        // 裁剪行拷贝：直接写入 DIB 位（光标画布 = 编码源）
        var cropX = _crop?.X ?? 0;
        var cropY = _crop?.Y ?? 0;
        var srcRowPitch = (int)mapped.RowPitch;
        var dstRowBytes = _outWidth * 4;
        var dst = (byte*)_overlay!.Bits;
        var src = (byte*)mapped.pData;
        for (var y = 0; y < _outHeight; y++)
        {
            Buffer.MemoryCopy(
                src + (cropY + y) * srcRowPitch + cropX * 4,
                dst + y * dstRowBytes,
                dstRowBytes, dstRowBytes);
        }
        _context->Unmap((ID3D11Resource*)_staging, 0);

        _overlay.Draw();   // §30 光标/高亮合成

        FrameArrived?.Invoke(this, new VideoFrame
        {
            TimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp(),
            Width = _outWidth,
            Height = _outHeight,
            PixelPtr = (IntPtr)_overlay.Bits,
            PixelByteCount = _overlay.ByteCount
        });
    }

    public unsafe void Dispose()
    {
        StopAsync().Wait();
        _overlay?.Dispose();
        if (_staging != null) { _staging->Release(); _staging = null; }
        if (_context != null) { _context->Release(); _context = null; }
        if (_device != null) { _device->Release(); _device = null; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
