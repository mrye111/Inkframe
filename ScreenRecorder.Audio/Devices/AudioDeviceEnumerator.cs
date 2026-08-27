using NAudio.CoreAudioApi;
using ScreenRecorder.Core.Devices;

namespace ScreenRecorder.Audio.Devices;

/// <summary>音频设备枚举与默认检测（§61），供设置页选择音源。</summary>
public sealed class AudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetMicrophones()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultId(enumerator);
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))
            .ToList();
    }

    private static string? SafeDefaultId(MMDeviceEnumerator enumerator)
    {
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID; }
        catch { return null; }
    }
}
