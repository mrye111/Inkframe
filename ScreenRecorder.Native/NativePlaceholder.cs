namespace ScreenRecorder.Native;

/// <summary>
/// Win32/WinRT/COM/D3D11/DXGI/WASAPI 互操作统一收敛层（架构约束：UI/Core 不直接 P/Invoke）。
/// 具体 API 由 CsWin32 源生成（见 NativeMethods.txt），IGraphicsCaptureItemInterop 等 COM 接口随 #12 引入。
/// </summary>
public static class NativePlaceholder
{
    public const string Layer = "ScreenRecorder.Native";
}
