using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IRecordingManager _recordingManager;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private RecordingMode _selectedMode = RecordingMode.FullScreen;

    public MainWindowViewModel(IRecordingManager recordingManager)
    {
        _recordingManager = recordingManager;
        _recordingManager.StateChanged += (_, e) => StatusText = e.NewState.ToString();
    }
}
