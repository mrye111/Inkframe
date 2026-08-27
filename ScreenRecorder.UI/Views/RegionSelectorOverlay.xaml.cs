using System.Windows;
using System.Windows.Input;
using ScreenRecorder.Core.Recording;

namespace ScreenRecorder.UI.Views;

/// <summary>区域选择覆盖层（§11）：拖拽框选，返回虚拟坐标矩形（PerMonitorV2 下即物理像素基准换算交给 CropCalculator）。</summary>
public partial class RegionSelectorOverlay : Window
{
    private Point _start;
    private bool _dragging;

    public ScreenRect? SelectedRegion { get; private set; }

    public RegionSelectorOverlay()
    {
        InitializeComponent();
        // 覆盖全部显示器（虚拟坐标系，§51）
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root) + new Vector(Left, Top);   // 转虚拟坐标
        _dragging = true;
        RubberBand.Visibility = Visibility.Visible;
        Root.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = e.GetPosition(Root) + new Vector(Left, Top);
        UpdateBand(current);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleaseMouseCapture();

        var current = e.GetPosition(Root) + new Vector(Left, Top);
        var rect = Normalize(_start, current);
        if (rect.Width >= 32 && rect.Height >= 32)   // 太小视为误触
        {
            SelectedRegion = rect;
            DialogResult = true;
        }
        Close();
    }

    private void UpdateBand(Point current)
    {
        var rect = Normalize(_start, current);
        RubberBand.Margin = new Thickness(rect.X - Left, rect.Y - Top, 0, 0);
        RubberBand.Width = rect.Width;
        RubberBand.Height = rect.Height;

        SizeLabel.Visibility = Visibility.Visible;
        SizeLabel.Margin = new Thickness(rect.X - Left, rect.Y - Top + rect.Height + 8, 0, 0);
        SizeText.Text = $"{rect.Width} × {rect.Height}";
    }

    private static ScreenRect Normalize(Point a, Point b) => new(
        (int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y),
        (int)Math.Abs(a.X - b.X), (int)Math.Abs(a.Y - b.Y));

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();   // SelectedRegion 保持 null = 取消
    }
}
