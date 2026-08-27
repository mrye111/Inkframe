using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
