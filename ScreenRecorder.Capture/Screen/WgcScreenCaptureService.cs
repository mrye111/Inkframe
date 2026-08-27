using System.Runtime.InteropServices;
using ScreenRecorder.Core.Recording;
using ScreenRecorder.Core.Services;
using ScreenRecorder.Native.Capture;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace ScreenRecorder.Capture.Screen;

/// <summary>
/// WGC 画面采集（#5 调研 + #3 Spike 验证路径）。
/// 本票（#10）范围：主显示器全屏。窗口/区域/光标自绘/多显示器选择见 #12。
/// 线程模型：CreateFreeThreaded 免 DispatcherQueue，FrameArrived 在后台线程触发，
/// 回调内立刻拷入 staging 并回读 CPU（池内仅 2 缓冲，占用过久会丢帧）。
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService, IDisposable
{
    private unsafe ID3D11Device* _device;
    private unsafe ID3D11DeviceContext* _context;
    private unsafe ID3D11Texture2D* _staging;

    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private int _width, _height, _rowBytes;
    private byte[] _frameBuffer = [];
    private volatile bool _running;

    public event EventHandler<VideoFrame>? FrameArrived;

    public (int Width, int Height) CurrentSize => (_width, _height);

    public unsafe Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        if (_running) throw new InvalidOperationException("采集已在进行");

        // #10 只支持全屏（主显示器）；窗口/区域在 #12 落地
        var hmon = MonitorFromPoint(new POINT { x = 0, y = 0 }, MONITOR.MONITOR_DEFAULTTOPRIMARY);

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

        var winrtDevice = WgcInterop.CreateWinRtDevice(_device);
        var item = WgcInterop.CreateForMonitor((IntPtr)hmon.Value);
        var size = item.Size;
        _width = size.Width; _height = size.Height;
        _rowBytes = _width * 4;
        _frameBuffer = new byte[_rowBytes * _height];

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        _session = _framePool.CreateCaptureSession(item);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            _session.IsCursorCaptureEnabled = false;   // 光标自绘在 #12

        ID3D11Texture2D* staging;
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_width,
            Height = (uint)_height,
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

        _framePool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _running = false;
        _session?.Dispose();
        _framePool?.Dispose();
        _session = null;
        _framePool = null;
        return Task.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        if (!_running) return;
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        PumpAndRaise(frame.Surface);
    }

    private unsafe void PumpAndRaise(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        var tex = WgcInterop.GetTexture2D(surface);
        if (tex == null) return;
        _context->CopyResource((ID3D11Resource*)_staging, (ID3D11Resource*)tex);
        tex->Release();

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (!_context->Map((ID3D11Resource*)_staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED)
            return;

        var src = (byte*)mapped.pData;
        for (int y = 0; y < _height; y++)
            Marshal.Copy((IntPtr)(src + y * (int)mapped.RowPitch), _frameBuffer, y * _rowBytes, _rowBytes);
        _context->Unmap((ID3D11Resource*)_staging, 0);

        // 注意：_frameBuffer 是复用缓冲，订阅方必须同步消费（进程编码器满足此约束）
        FrameArrived?.Invoke(this, new VideoFrame
        {
            TimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp(),
            Width = _width,
            Height = _height,
            PixelData = _frameBuffer
        });
    }

    public unsafe void Dispose()
    {
        StopAsync().Wait();
        if (_staging != null) { _staging->Release(); _staging = null; }
        if (_context != null) { _context->Release(); _context = null; }
        if (_device != null) { _device->Release(); _device = null; }
    }
}
