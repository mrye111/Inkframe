using ScreenRecorder.UI.Themes;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 区域选择：VM 定义契约，View 注入覆盖层交互
        viewModel.RegionPicker = () =>
        {
            var overlay = new RegionSelectorOverlay();
            return Task.FromResult(overlay.ShowDialog() == true ? overlay.SelectedRegion : null);
        };

        // Glass 0 基底：SourceInitialized 后 HWND 才可用（调研坑 #8）
        SourceInitialized += (_, _) =>
        {
            var plan = GlassCapability.ResolveCurrent();
            BackdropHelper.Apply(this, plan.MainWindow, plan.TryUndocumentedMica);
            // 失败/降级：保持 XAML 里的不透明 InkBackground1 + 自绘层接管（铁律：不配对不透明就变隐形窗）
        };
    }
}
