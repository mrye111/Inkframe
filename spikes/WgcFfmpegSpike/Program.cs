// WgcFfmpegSpike — 最危险链路验证（issue #3）
// WGC 全屏采集 → D3D11 staging → BGRA rawvideo 管道 → ffmpeg.exe → H.264/MP4
// 用法: dotnet run -- [--seconds 30] [--out spike.mp4] [--encoder auto]
using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using static TerraFX.Interop.DirectX.D3D11;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.IID;
using static TerraFX.Interop.Windows.Windows;

return await new SpikeApp().RunAsync(args);

internal sealed class SpikeApp
{
    private unsafe ID3D11Device* _device;
    private unsafe ID3D11DeviceContext* _context;
    private unsafe ID3D11Texture2D* _staging;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;

    private int _width, _height, _rowBytes;
    private byte[] _frameBuffer = [];
    private Process? _ffmpeg;
    private const int TargetFps = 30;
    private Stopwatch? _sw;
    private volatile bool _done;
    private int _mapFailLogged;
    private long _framesArrived, _framesWritten;

    public async Task<int> RunAsync(string[] args)
    {
        int seconds = 30;
        string outFile = Path.GetFullPath("spike.mp4");
        string encoderPref = "auto";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--seconds") seconds = int.Parse(args[i + 1]);
            if (args[i] == "--out") outFile = Path.GetFullPath(args[i + 1]);
            if (args[i] == "--encoder") encoderPref = args[i + 1];
        }

        var encoder = ProbeEncoder(encoderPref);
        if (encoder is null) { Console.Error.WriteLine("无可用 H.264 编码器"); return 1; }
        Console.WriteLine("[spike] encoder = " + encoder);

        InitCapture();
        Console.WriteLine("[spike] capture size = " + _width + "x" + _height + " (物理像素)");

        StartFfmpeg(encoder, outFile);

        _framePool!.FrameArrived += OnFrameArrived;
        _session!.StartCapture();
        Console.WriteLine("[spike] recording " + seconds + "s ...");

        _sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        _done = true;
        _sw!.Stop();

        _session!.Dispose();
        _framePool!.Dispose();
        _ffmpeg!.StandardInput.Close();
        _ffmpeg.WaitForExit(30000);

        CleanupNative();

        var wall = _sw!.Elapsed.TotalSeconds;
        Console.WriteLine("[spike] wall = " + wall.ToString("F2") + "s, arrived = " + _framesArrived +
            ", written = " + _framesWritten + ", lost = " + (_framesArrived - _framesWritten) +
            ", effective fps = " + (_framesWritten / wall).ToString("F1"));
        Console.WriteLine("[spike] ffmpeg exit = " + _ffmpeg.ExitCode + ", output = " + outFile);

        var ffprobe = Process.Start(new ProcessStartInfo("ffprobe",
            "-v error -select_streams v:0 -show_entries stream=codec_name,width,height,avg_frame_rate -show_entries format=duration -of default=noprint_wrappers=1 \"" + outFile + "\"")
        { RedirectStandardOutput = true, UseShellExecute = false })!;
        Console.WriteLine("----- ffprobe -----");
        Console.WriteLine(await ffprobe.StandardOutput.ReadToEndAsync());

        return _ffmpeg.ExitCode == 0 && _framesWritten > 0 ? 0 : 2;
    }

    private static string? ProbeEncoder(string pref)
    {
        string[] candidates = pref == "auto" ? new[] { "h264_nvenc", "h264_qsv", "h264_amf", "libx264" } : new[] { pref };
        foreach (var c in candidates)
        {
            // 运行时试编码：-h 只证明帮助存在，不代表驱动支持（NVENC API 版本 / QSV 运行时都可能缺席）
            var probe = Process.Start(new ProcessStartInfo("ffmpeg",
                "-hide_banner -loglevel error -f lavfi -i testsrc2=size=64x64:duration=0.1:rate=10 -c:v " + c + " -f null -")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = false, RedirectStandardError = false });
            if (probe is null || !probe.WaitForExit(15000)) { try { probe?.Kill(); } catch { } continue; }
            if (probe.ExitCode == 0) return c;
            Console.WriteLine("[spike] encoder " + c + " runtime probe FAILED (exit " + probe.ExitCode + "), fallback...");
        }
        return null;
    }

    private unsafe void InitCapture()
    {
        var hmon = MonitorFromPoint(new POINT { x = 0, y = 0 }, MONITOR.MONITOR_DEFAULTTOPRIMARY);
        Console.WriteLine("[spike] HMONITOR = 0x" + ((IntPtr)hmon.Value).ToString("X"));

        // D3D11 设备（BGRA 支持是 WGC 硬要求）
        ID3D11Device* device;
        D3D_FEATURE_LEVEL fl;
        var levels = stackalloc D3D_FEATURE_LEVEL[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 };
        Interop.Check(D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, HMODULE.NULL,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            levels, 1, (uint)D3D11_SDK_VERSION, &device, &fl, null), "D3D11CreateDevice");
        _device = device;
        ID3D11DeviceContext* context;
        _device->GetImmediateContext(&context);
        _context = context;
        Console.WriteLine("[spike] D3D11 FL = " + fl);

        IDXGIDevice* dxgiDevice;
        Guid iidDxgi = IID_IDXGIDevice;
        Interop.Check(_device->QueryInterface(&iidDxgi, (void**)&dxgiDevice), "QI IDXGIDevice");

        IntPtr inspectable;
        Interop.Check(Interop.CreateDirect3D11DeviceFromDXGIDevice((IntPtr)dxgiDevice, out inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
        dxgiDevice->Release();
        _winrtDevice = WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);

        var item = Interop.CreateCaptureItemForMonitor(hmon);
        var size = item.Size;
        _width = size.Width; _height = size.Height;
        _rowBytes = _width * 4;
        _frameBuffer = new byte[_rowBytes * _height];

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        _session = _framePool.CreateCaptureSession(item);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            _session.IsCursorCaptureEnabled = false;   // 光标自绘（#12），spike 关闭
        Console.WriteLine("[spike] cursor capture disabled");

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
        Interop.Check(_device->CreateTexture2D(&desc, null, &staging), "CreateTexture2D(staging)");
        _staging = staging;
    }

    private void StartFfmpeg(string encoder, string outFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        var ffmpegArgs = "-y -f rawvideo -pix_fmt bgra -s " + _width + "x" + _height + " -r 30 -i - " +
            (encoder == "libx264" ? "-c:v libx264 -preset veryfast " : "-c:v " + encoder + " ") +
            "-pix_fmt yuv420p \"" + outFile + "\"";
        _ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg", ffmpegArgs)
        { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var ffLog = Path.ChangeExtension(outFile, ".ffmpeg.log");
        _ = Task.Run(async () =>
        {
            var text = await _ffmpeg.StandardError.ReadToEndAsync();
            await File.WriteAllTextAsync(ffLog, text);
        });
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        if (_done) return;
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        Interlocked.Increment(ref _framesArrived);

        // 抽稀到目标 fps：墙钟 elapsed * fps 决定应写帧数，多余帧直接丢（§49 掉帧策略的最小形态）
        var expected = (long)(_sw!.Elapsed.TotalSeconds * TargetFps);
        if (Interlocked.Read(ref _framesWritten) >= expected) return;

        PumpFrame(frame.Surface);
    }

    private unsafe void CleanupNative()
    {
        _staging->Release();
        _context->Release();
        _device->Release();
    }

    private unsafe void PumpFrame(IDirect3DSurface surface)
    {
        var tex = Interop.GetD3D11Texture2D(surface);
        if (tex == null) return;
        _context->CopyResource((ID3D11Resource*)_staging, (ID3D11Resource*)tex);
        tex->Release();

        D3D11_MAPPED_SUBRESOURCE mapped;
        var mapHr = _context->Map((ID3D11Resource*)_staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        if (mapHr.FAILED && Interlocked.CompareExchange(ref _mapFailLogged, 1, 0) == 0)
            Console.WriteLine("[spike][diag] Map failed: 0x" + mapHr.Value.ToString("X8"));
        if (mapHr.SUCCEEDED)
        {
            var src = (byte*)mapped.pData;
            for (int y = 0; y < _height; y++)
                Marshal.Copy((IntPtr)(src + y * (int)mapped.RowPitch), _frameBuffer, y * _rowBytes, _rowBytes);
            _context->Unmap((ID3D11Resource*)_staging, 0);

            _ffmpeg!.StandardInput.BaseStream.Write(_frameBuffer, 0, _frameBuffer.Length);
            Interlocked.Increment(ref _framesWritten);
        }
    }
}

internal static unsafe class Interop
{
    public static void Check(HRESULT hr, string what)
    {
        if (hr.FAILED) throw new COMException(what + " failed: 0x" + hr.Value.ToString("X8"));
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    /// <summary>IGraphicsCaptureItemInterop: RoGetActivationFactory → CreateForMonitor（research §1 初始化链）。</summary>
    public static GraphicsCaptureItem CreateCaptureItemForMonitor(HMONITOR hmon)
    {
        Guid iidInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");   // IGraphicsCaptureItemInterop
        Guid iidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");       // IGraphicsCaptureItem
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Check(WindowsCreateString(className, className.Length, out IntPtr hstring), "WindowsCreateString");
        IntPtr factory;
        try
        {
            Check(RoGetActivationFactory(hstring, ref iidInterop, out factory), "RoGetActivationFactory");
        }
        finally
        {
            WindowsDeleteString(hstring);
        }

        // vtable: slot0-2 = IUnknown, slot3 = CreateForWindow, slot4 = CreateForMonitor
        var vtbl = *(void***)factory;
        var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[4];
        IntPtr itemPtr;
        Check(createForMonitor(factory, (IntPtr)hmon.Value, &iidItem, &itemPtr), "CreateForMonitor");
        Marshal.Release(factory);

        var item = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return item;
    }

    private static readonly Guid IidI3dDxgiAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"); // IDirect3DDxgiInterfaceAccess
    private static readonly Guid IidTexture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");     // ID3D11Texture2D

    /// <summary>IDirect3DSurface → ID3D11Texture2D*（手工 QueryInterface + GetInterface）。调用方负责 Release。</summary>
    private static int _diagLogged;

    public static ID3D11Texture2D* GetD3D11Texture2D(IDirect3DSurface surface)
    {
        // Marshal.GetIUnknownForObject 拿到的是 RCW 包装（QI 自定义接口会 E_NOINTERFACE），
        // FromManaged(...).ThisPtr 才是底层 WinRT 对象的真实原生指针。
        var punk = WinRT.MarshalInspectable<IDirect3DSurface>.FromManaged(surface); // IntPtr，持有一次引用
        try
        {
            var vtbl = *(void***)punk;
            var qi = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[0];
            IntPtr access;
            var iidAccess = IidI3dDxgiAccess;
            var hrQi = qi(punk, &iidAccess, &access);
            if (hrQi != 0)
            {
                if (Interlocked.CompareExchange(ref _diagLogged, 1, 0) == 0)
                    Console.WriteLine("[spike][diag] QI IDirect3DDxgiInterfaceAccess failed: 0x" + hrQi.ToString("X8"));
                return null;
            }
            try
            {
                var avtbl = *(void***)access;
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)avtbl[3];
                IntPtr tex;
                var iidTex = IidTexture2D;
                var hrGet = getInterface(access, &iidTex, &tex);
                if (hrGet != 0)
                {
                    if (Interlocked.CompareExchange(ref _diagLogged, 2, 0) == 0 || _diagLogged == 1)
                    {
                        _diagLogged = 2;
                        Console.WriteLine("[spike][diag] GetInterface(ID3D11Texture2D) failed: 0x" + hrGet.ToString("X8"));
                    }
                    return null;
                }
                return (ID3D11Texture2D*)tex;
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(punk); }
    }
}
