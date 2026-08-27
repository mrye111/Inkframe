namespace ScreenRecorder.Encoding.FFmpeg;

/// <summary>机器指纹：GPU 型号 + 驱动版本 + OS 版本。变化即重探编码器（#6 决策）。</summary>
public static class MachineFingerprint
{
    public static string Current =>
        string.Join("|", OsVersion, CpuName, GpuDescription);

    private static string OsVersion => Environment.OSVersion.Version.ToString();

    private static string CpuName =>
        Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? $"cpu-{Environment.ProcessorCount}";

    private static string GpuDescription
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                var desc = key?.GetValue("DriverDesc")?.ToString() ?? "unknown-gpu";
                var ver = key?.GetValue("DriverVersion")?.ToString() ?? "unknown-driver";
                return desc + "/" + ver;
            }
            catch { return "unknown-gpu"; }
        }
    }
}
