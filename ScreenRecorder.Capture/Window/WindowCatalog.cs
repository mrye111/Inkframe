using ScreenRecorder.Capture.Window;
using ScreenRecorder.Core.Services;

namespace ScreenRecorder.Capture.Window;

public sealed class WindowCatalog : IWindowCatalog
{
    public IReadOnlyList<RecordableWindowInfo> GetRecordableWindows() =>
        WindowEnumerator.GetRecordableWindows()
            .Select(w => new RecordableWindowInfo(w.Hwnd.ToString("X"), w.Title, w.ProcessName))
            .ToList();
}
