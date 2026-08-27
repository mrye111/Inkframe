namespace ScreenRecorder.Core.Recording;

/// <summary>磁盘空间不足（§27）：开录前检查，明确提示并阻止启动。</summary>
public sealed class InsufficientDiskSpaceException : Exception
{
    public required string DriveName { get; init; }
    public long FreeBytes { get; init; }
    public long RequiredBytes { get; init; }

    public override string Message =>
        $"磁盘 {DriveName} 剩余 {FreeBytes / 1048576.0:F0} MB，低于最低要求 {RequiredBytes / 1048576.0:F0} MB，已阻止开录（§27）";
}
