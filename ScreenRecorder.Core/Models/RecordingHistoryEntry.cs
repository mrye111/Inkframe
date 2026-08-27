namespace ScreenRecorder.Core.Models;

/// <summary>录制历史条目（§28）。存储方案随 #8 决策。</summary>
public sealed record RecordingHistoryEntry
{
    public required string FilePath { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ThumbnailPath { get; init; }
    public string? ParametersSummary { get; init; }   // 参数快照：1080P/60FPS/高清…
}
