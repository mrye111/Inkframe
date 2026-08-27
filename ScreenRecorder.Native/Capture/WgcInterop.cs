using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenRecorder.Native.Capture;

/// <summary>
/// WGC 互操作（#3 Spike 验证 + #5 调研初始化链）：
/// RoGetActivationFactory → IGraphicsCaptureItemInterop → GraphicsCaptureItem；
/// IDirect3DSurface → ID3D11Texture2D 走 MarshalInspectable.FromManaged 真实原生指针
///（Marshal.GetIUnknownForObject 拿到的是 RCW 包装，QI 自定义接口会 E_NOINTERFACE）。
/// </summary>
public static unsafe class WgcInterop
{
    private static readonly Guid IidInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");  // IGraphicsCaptureItemInterop
    private static readonly Guid IidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");      // IGraphicsCaptureItem
    private static readonly Guid IidDxgiAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"); // IDirect3DDxgiInterfaceAccess
    private static readonly Guid IidTexture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");  // ID3D11Texture2D

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmonitor)
    {
        var factory = GetInteropFactory();
        try
        {
            var vtbl = *(void***)factory;
            var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[4];
            IntPtr itemPtr;
            var iidItem = IidItem;
            Check(createForMonitor(factory, hmonitor, &iidItem, &itemPtr), "CreateForMonitor");
            var item = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            Marshal.Release(itemPtr);
            return item;
        }
        finally { Marshal.Release(factory); }
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        var factory = GetInteropFactory();
        try
        {
            var vtbl = *(void***)factory;
            var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[3];
            IntPtr itemPtr;
            var iidItem = IidItem;
            Check(createForWindow(factory, hwnd, &iidItem, &itemPtr), "CreateForWindow");
            var item = WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            Marshal.Release(itemPtr);
            return item;
        }
        finally { Marshal.Release(factory); }
    }

    /// <summary>IDirect3DSurface → ID3D11Texture2D*。调用方负责 Release。</summary>
    public static ID3D11Texture2D* GetTexture2D(IDirect3DSurface surface)
    {
        var punk = WinRT.MarshalInspectable<IDirect3DSurface>.FromManaged(surface); // 真实原生指针，持一次引用
        try
        {
            var vtbl = *(void***)punk;
            var qi = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[0];
            IntPtr access;
            var iidAccess = IidDxgiAccess;
            if (qi(punk, &iidAccess, &access) != 0) return null;
            try
            {
                var avtbl = *(void***)access;
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)avtbl[3];
                IntPtr tex;
                var iidTex = IidTexture2D;
                return getInterface(access, &iidTex, &tex) == 0 ? (ID3D11Texture2D*)tex : null;
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(punk); }
    }

    /// <summary>D3D11 设备 → WinRT IDirect3DDevice（BGRA 支持是 WGC 硬要求）。</summary>
    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device* device)
    {
        IDXGIDevice* dxgiDevice;
        var iidDxgi = TerraFX.Interop.Windows.IID.IID_IDXGIDevice;
        Check(device->QueryInterface(&iidDxgi, (void**)&dxgiDevice), "QI IDXGIDevice");
        IntPtr inspectable;
        Check(CreateDirect3D11DeviceFromDXGIDevice((IntPtr)dxgiDevice, out inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
        dxgiDevice->Release();
        var winrt = WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);
        return winrt;
    }

    private static IntPtr GetInteropFactory()
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Check(WindowsCreateString(className, className.Length, out var hstring), "WindowsCreateString");
        try
        {
            var iid = IidInterop;
            Check(RoGetActivationFactory(hstring, ref iid, out var factory), "RoGetActivationFactory");
            return factory;
        }
        finally { WindowsDeleteString(hstring); }
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} failed: 0x{hr:X8}", hr);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}
