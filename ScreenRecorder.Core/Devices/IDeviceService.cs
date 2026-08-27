namespace ScreenRecorder.Core.Devices;

/// <summary>设备枚举与自动检测（§61）。</summary>
public interface IDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();
    IReadOnlyList<MonitorInfo> GetMonitors();
    bool HasHardwareEncoder { get; }   // NVENC/QSV/AMF 任一可用（§55 回退依据）
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
public sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, uint Dpi);
