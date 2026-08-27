namespace ScreenRecorder.Core.Services;

/// <summary>可录制窗口目录（§10）：供窗口模式选择面板使用。</summary>
public interface IWindowCatalog
{
    IReadOnlyList<RecordableWindowInfo> GetRecordableWindows();
}

public sealed record RecordableWindowInfo(string HandleHex, string Title, string ProcessName);
