namespace ScreenRecorder.Core.Recording;

/// <summary>输出文件命名（§25）：Inkframe_yyyyMMdd_HHmmss.mp4，重名追加序号。</summary>
public static class RecordingOutputNamer
{
    public static string NextPath(string directory, DateTimeOffset now)
    {
        Directory.CreateDirectory(directory);
        var baseName = $"Inkframe_{now:yyyyMMdd_HHmmss}";
        var path = Path.Combine(directory, baseName + ".mp4");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{baseName}_{i}.mp4");
        return path;
    }
}
