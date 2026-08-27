namespace ScreenRecorder.UI.Themes;

/// <summary>窗口 Backdrop 类型（DWMWA_SYSTEMBACKDROP_TYPE 枚举值）。</summary>
public enum InkBackdrop
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4
}

/// <summary>一套降级后的材质方案。</summary>
public sealed record GlassPlan(
    InkBackdrop MainWindow,      // Glass 0 基底
    InkBackdrop FloatingBar,     // Glass 3 悬浮条
    bool TryUndocumentedMica,    // Win11 21H2：可试 DWMWA_MICA_EFFECT=1029
    bool OpaqueFallback);        // true = 必须不透明底 + 自绘玻璃层

/// <summary>
/// 系统能力检测与降级矩阵（research/liquid-glass-wpf.md §3.3）。
/// 纯函数便于单测；铁律：任何 backdrop 失败都必须落不透明底（否则隐形窗）。
/// </summary>
public static class GlassCapability
{
    public const int BuildWin11_22H2 = 22621;
    public const int BuildWin11_21H2 = 22000;

    public static GlassPlan Resolve(int buildNumber, bool transparencyEnabled)
    {
        if (!transparencyEnabled)
            // 关闭"透明效果"/高对比/节能：一律不透明 + 自绘层次（自绘层必须在无材质时也成立）
            return new GlassPlan(InkBackdrop.None, InkBackdrop.None, false, true);

        if (buildNumber >= BuildWin11_22H2)
            // 主窗体 Mica Alt（更深、层次稳）；悬浮条 Acrylic（真实透窗模糊）+ DWM 圆角阴影
            return new GlassPlan(InkBackdrop.MicaAlt, InkBackdrop.Acrylic, false, false);

        if (buildNumber >= BuildWin11_21H2)
            // 21H2：试未文档化 Mica，失败落纯色
            return new GlassPlan(InkBackdrop.None, InkBackdrop.None, true, true);

        // Win10：纯色底 + 自绘柔光层（SWCA Acrylic 仅作可选增强，本骨架不启用）
        return new GlassPlan(InkBackdrop.None, InkBackdrop.None, false, true);
    }

    /// <summary>当前系统「透明效果」开关（HKCU Personalize/EnableTransparency）。</summary>
    public static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (key?.GetValue("EnableTransparency") as int?) != 0;
        }
        catch { return true; }   // 读取失败按开启处理，失败路径另有降级兜底
    }

    public static GlassPlan ResolveCurrent() =>
        Resolve(Environment.OSVersion.Version.Build, IsTransparencyEnabled());
}
