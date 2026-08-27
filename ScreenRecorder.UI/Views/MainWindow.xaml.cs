using ScreenRecorder.UI.Themes;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Glass 0 基底：SourceInitialized 后 HWND 才可用（调研坑 #8）
        SourceInitialized += (_, _) =>
        {
            var plan = GlassCapability.ResolveCurrent();
            var applied = BackdropHelper.Apply(this, plan.MainWindow, plan.TryUndocumentedMica);
            Title = $"Inkframe [backdrop={plan.MainWindow}, applied={applied}]";   // #13 验证标记，页面票据落地时移除
            // 失败/降级：保持 XAML 里的不透明 InkBackground1 + 自绘层接管（铁律：不配对不透明就变隐形窗）
        };
    }
}
