namespace ScreenRecorder.Core.Recording;

public sealed class RecordingStateChangedEventArgs : EventArgs
{
    public required RecordingState OldState { get; init; }
    public required RecordingState NewState { get; init; }
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
}
