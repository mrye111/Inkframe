namespace ScreenRecorder.Core.Recording;

/// <summary>
/// 帧节流（§49 掉帧策略的最小形态，#3 Spike 实证必需）：
/// 采集帧率（显示器刷新率，常 60Hz+）高于目标 fps 时，按墙钟抽稀——
/// 期望写入数 = elapsed × fps，达到即丢。否则输出时长会偏离真实时长。
/// </summary>
public sealed class FrameThrottler
{
    private readonly int _targetFps;
    private long _written;

    public FrameThrottler(int targetFps) => _targetFps = targetFps;

    public long Written => Interlocked.Read(ref _written);

    /// <summary>这一帧该不该写？</summary>
    public bool ShouldWrite(TimeSpan elapsed)
    {
        var expected = (long)(elapsed.TotalSeconds * _targetFps);
        if (Interlocked.Read(ref _written) >= expected) return false;
        Interlocked.Increment(ref _written);
        return true;
    }
}
