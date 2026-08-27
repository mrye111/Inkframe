using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenRecorder.UI.Themes;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

/// <summary>
/// 悬浮录制条（§21/§31）：标准小窗体（非 AllowsTransparency）+ Topmost + Acrylic。
/// 位置：主屏幕底部居中上方；可拖拽。
/// </summary>
public partial class FloatingBarWindow : Window
{
    private readonly FloatingBarViewModel _viewModel;
    private readonly DispatcherTimer _elapsedTimer;

    public FloatingBarWindow(FloatingBarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // 底部居中（主屏幕工作区）
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Height - Height - 76;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (_, _) => _viewModel.TickElapsed();

        SourceInitialized += (_, _) =>
        {
            var plan = GlassCapability.ResolveCurrent();
            BackdropHelper.Apply(this, plan.FloatingBar);   // Acrylic；失败则半透明底接管
        };
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!_elapsedTimer.IsEnabled) _elapsedTimer.Start();
    }

    public void ShowAndRun()
    {
        Show();
        _elapsedTimer.Start();
    }

    public void HideAndStop()
    {
        _elapsedTimer.Stop();
        Hide();
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e) => DragMove();
}
