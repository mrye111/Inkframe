namespace ScreenRecorder.Core.Recording;

/// <summary>
/// 一次录制的会话（§46）：持有统一时钟基线（§47），汇聚采集/编码产出。
/// 具体采集与编码接线随 #10 施工完成，此处先立状态与生命周期骨架。
/// </summary>
public sealed class RecordingSession : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public required RecordingRequest Request { get; init; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    /// <summary>统一时钟基线：音画时间戳均以它为原点（§47/§48）。</summary>
    public long ClockBaselineTicks { get; } = System.Diagnostics.Stopwatch.GetTimestamp();

    public string? OutputFilePath { get; set; }

    /// <summary>暂停累计时长：恢复录制时从时间轴扣除，保证单文件无缝（§22）。</summary>
    public TimeSpan AccumulatedPause { get; private set; }
    private DateTimeOffset? _pauseStartedAt;

    public void MarkPaused() => _pauseStartedAt ??= DateTimeOffset.Now;

    public void MarkResumed()
    {
        if (_pauseStartedAt is { } start)
        {
            AccumulatedPause += DateTimeOffset.Now - start;
            _pauseStartedAt = null;
        }
    }

    public void Dispose()
    {
        // #10：释放采集/编码资源
    }
}
