namespace ScreenRecorder.Infrastructure.Diagnostics;

/// <summary>
/// 崩溃保护标记（§56 骨架）：开录写标记，正常停止删除；
/// 启动时发现残留标记 = 上次录制崩溃，由上层提示恢复（恢复 UX 在雾区，待后续票据）。
/// </summary>
public sealed class SessionMarkerStore
{
    private readonly string _dir;

    public SessionMarkerStore()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Inkframe", "sessions");
        Directory.CreateDirectory(_dir);
    }

    public string MarkerPath(Guid sessionId) => Path.Combine(_dir, sessionId + ".session");

    public void Mark(Guid sessionId, string outputFilePath) =>
        File.WriteAllText(MarkerPath(sessionId), outputFilePath);

    public void Clear(Guid sessionId)
    {
        var path = MarkerPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>启动时调用：返回上次未正常结束的录制输出路径。</summary>
    public IReadOnlyList<string> FindOrphanedOutputs() =>
        Directory.Exists(_dir)
            ? Directory.GetFiles(_dir, "*.session").Select(File.ReadAllText).ToList()
            : [];
}
