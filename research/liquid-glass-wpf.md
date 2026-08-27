# WPF 液态玻璃（Liquid Glass）视觉技术路线调研

> Issue: mrye111/Inkframe#4 ｜ 分支: research/liquid-glass-wpf
> 调研依据：Microsoft Learn 官方文档、各第三方库 GitHub 官方仓库（链接见文末）
> 约束场景：录屏软件——主窗体常最小化；悬浮条需置顶+透明+圆角+阴影；录制（硬件编码 + WGC 采集）期间 UI 不可与采集抢 GPU

---

## 一、结论摘要

**推荐方案（一句话）**：主窗体用 DWM 官方 Backdrop（`DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`，Win11 22H2+ 主窗体 Mica Alt、弹层 Transient/Acrylic，Win10/低版本降级纯色+自绘柔光），Glass 1–3 层用纯 WPF 半透明 Border + 静态渐变/快照笔刷自绘，悬浮条用"标准小窗体 + DWM 免费圆角阴影 + Acrylic backdrop"（**禁止主窗体 AllowsTransparency、禁止大面积实时模糊 Shader**），第三方库只借鉴其 Backdrop P/Invoke 管道、不引入其 Fluent 样式体系，XAML Islands/WinUI 3 混用路线排除。

核心判断逻辑：
1. 真正的"液态玻璃"需要**窗口背后像素的实时采样**。WPF 渲染管线（ShaderEffect）拿不到窗口背后的桌面像素，唯一低成本提供者就是 DWM 的材质合成（Mica/Acrylic）——自绘无法替代，只能伪造。
2. DWM 材质由合成器承担开销，对应用进程 GPU 占用≈0，**天然满足"录制时不抢 GPU"**；自绘实时模糊与 WGC 采集共用 3D 引擎，录制时必冲突。
3. 设计规范禁止"Fluent 模板感"，而 Mica/Acrylic 只是**底层材质**，其上 Glass 1–3 分层、液态边缘、柔光全部自绘，材质选择与"去模板感"不矛盾。

---

## 二、路线 / 事实对比

| # | 路线 | 实现方式 | 系统要求 | GPU/CPU 代价 | "液态感"上限 | 录制时 GPU 争用 | 模板感风险 | 结论 |
|---|------|---------|---------|-------------|------------|--------------|----------|------|
| 1 | **Mica**（DWMSBT_MAINWINDOW=2） | `DwmSetWindowAttribute` + WPF `Background=Transparent` | Win11 22H2 (22621+) | ≈0（DWM 合成，壁纸一次性采样） | 低：静态材质，非实时透窗 | 无 | 无（仅底色材质） | ✅ 主窗体基底候选 |
| 2 | **Mica Alt / Tabbed**（DWMSBT_TABBEDWINDOW=4） | 同上 | Win11 22H2+ | ≈0 | 低：比 Mica 更深、层次更稳 | 无 | 无 | ✅ **主窗体基底推荐**（配合 Glass 分层更沉） |
| 3 | **Transient / Acrylic**（DWMSBT_TRANSIENTWINDOW=3） | 同上 | Win11 22H2+ | 低（DWM 实时模糊背后内容） | **高：真实透窗模糊** | 极小（DWM 承担） | 无 | ✅ 悬浮条/弹出层推荐 |
| 4 | Win10 Acrylic | 未文档化 `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND` | Win10 1809+ | 低（DWM 承担） | 高 | 极小 | 无 | ⚠️ 仅作 Win10 可选增强，需降级预案（未文档化 API，随时可变） |
| 5 | 自绘实时模糊（BlurEffect / 自定义 ShaderEffect） | WPF Effect，HLSL PS 2.0/3.0 | 无（需 Render Tier 2） | **高：随 半径×面积 增长，逐帧全量重绘** | 低：Shader 只能采样元素自身，**拿不到窗口后桌面像素** | **严重：与 WGC 采集/编码预览共用 3D 引擎** | 无 | ❌ 排除（仅允许小面积、非录制态点缀） |
| 6 | VisualBrush / 快照伪造玻璃 | 预截背景 → 静态模糊贴图 + 半透明叠加 + 高光渐变 | 无 | 一次性成本，逐帧≈0 | 中：静态"假液态"，柔光/边缘可控 | 无（录制期禁用重采样） | 无 | ✅ Glass 1–3 卡片层推荐 |
| 7 | AllowsTransparency 分层窗口 | `Window.AllowsTransparency=True` | 无 | **高：该窗体强制软件渲染，失去 GPU 加速**；有 D3D airspace 问题；无 DWM 圆角阴影 | 中（任意形状） | 有（CPU 光栅化抢主线程） | 无 | ⚠️ 仅小面积悬浮条可接受；**主窗体禁止** |
| 8 | 第三方库：**Wpf.Ui**（lepoco/wpfui，MIT） | `FluentWindow` + `WindowBackdropType`（None/MainWindow/Tabbed/Transient） | .NET Framework 4.6.2+ / .NET 6/8；材质仍需 Win11 22H2+ | 同 #1–3（内部就是同一组 P/Invoke） | 材质同左 | 无 | **高：控件样式为 Fluent 体系** | ⚠️ 只参考/复制其 Backdrop 管道（MIT 允许），不采用其样式 |
| 9 | 第三方库：**ModernWpf** | 移植 WinUI/UWP 控件；Acrylic 依赖未文档化 API + muxc 移植 | .NET Framework 为主 | 同 #4 | 同 #4 | 极小 | **高** | ❌ 仓库近年活跃度低，样式天花板低，排除 |
| 10 | 第三方库：**HandyControl** | `BlurWindow`（SetWindowCompositionAttribute 模糊） | Win10 1809+ 才有真效果 | 低 | 中 | 极小 | 高（自有设计语言） | ❌ 设计语言不符，排除 |
| 11 | **XAML Islands / WinUI 3 混用** | WPF 承载 UWP(WinUI2) 控件：`Microsoft.Toolkit.Win32.UI.*` | 工具链停留在 .NET Core 3.x 时代，已停止演进 | — | — | — | — | ❌ **排除**：官方仅支持 WinUI 2 (UWP) 控件嵌入；**WinUI 3 不支持以 XAML Islands 嵌入 WPF**（Windows App SDK 官方讨论确认无支持路径） |

### 关键事实摘录（均有官方出处）

- `DWMWA_SYSTEMBACKDROP_TYPE`（属性值 38）枚举：`DWMSBT_NONE=1`、`DWMSBT_MAINWINDOW=2`（Mica）、`DWMSBT_TRANSIENTWINDOW=3`（Acrylic）、`DWMSBT_TABBEDWINDOW=4`（Mica Alt），**要求 Windows 11 build 22621（22H2）起**（Microsoft Learn, DWM_SYSTEMBACKDROP_TYPE）。
- Mica 是**壁纸采样**材质：只对桌面壁纸做一次采样模糊，不实时采样窗口之间内容；窗口失焦时自动回退为纯色（fallback color），省电模式/关闭"透明效果"时自动降级（Microsoft Learn, Mica material）。→ **Mica 本身不是"液态"，液态感必须靠上层自绘补。**
- Acrylic（Transient）是 DWM 对窗口背后内容的**实时模糊**，但开销在合成器（dwm.exe / CIM）侧，对应用进程几乎零成本。
- WPF 中启用方式：**不开 AllowsTransparency**，仅 `Window.Background = Brushes.Transparent`，待 `HwndSource` 创建后（`SourceInitialized`）对 HWND 调 `DwmSetWindowAttribute`；透明像素处 DWM 材质透出。
- WPF `BlurEffect` 属 GPU 渲染（Tier 2），但代价随模糊半径与面积增长，**动画模糊半径会每帧全量重绘**；旧 `BitmapEffect` 已废弃且为 CPU 渲染（Microsoft Learn, WPF 性能优化文档）。
- `AllowsTransparency=True` 的窗体是分层窗口（layered window）：WPF 对其走**软件渲染**，且存在 D3D/视频 airspace 冲突、无 DWM 圆角与阴影（林德熙对 WPF 源码的分析 + MS 文档一致结论）。
- Win11 上**标准非 AllowsTransparency 窗体**可由 DWM 自动提供圆角（`DWMWA_WINDOW_CORNER_PREFERENCE`）与阴影——悬浮条白嫖系统级圆角阴影且保持 GPU 加速。
- 录屏管线的 GPU 分布：NVENC/QuickSync/AMF 编码走**固定功能硬件单元**（不占 3D 引擎），但 WGC/DDA 采集的纹理拷贝走 3D 引擎；DWM 材质合成开销极小；**大面积实时自绘模糊是唯一会与采集正面争 3D 引擎的 UI 行为**。

---

## 三、推荐方案详述

### 3.1 材质分层映射（对齐设计规范 Glass 0–3）

| 设计层级 | 技术实现 | 说明 |
|---------|---------|------|
| **Glass 0（窗口基底）** | DWM Backdrop：Win11 22H2+ 用 **Mica Alt（Tabbed）**；Win10/降级环境用不透明深浅色底 | 提供空间纵深底色，零 GPU 成本 |
| **Glass 1（主内容面板）** | 自绘：`Border` + 8–12% 白/黑半透明填充 + 1px 内描边高光（上缘亮、下缘暗） | 静态画笔，Render 层零重绘 |
| **Glass 2（悬浮卡片/弹层）** | 自绘 + **快照伪造**：面板弹出时对 Glass 0/壁纸做一次截图 → 静态模糊贴图作底，叠加半透明与高光渐变 | "液态感"主要来自此层的柔光与边缘折射感，均为静态资源 |
| **Glass 3（悬浮条/Toast）** | **标准小窗体**（非 AllowsTransparency）+ Topmost + `DWMSBT_TRANSIENTWINDOW`（Acrylic）+ DWM 免费圆角阴影；Win10 降级 AllowsTransparency 小窗（面积小，软件渲染代价可忽略） | 真实透窗模糊；置顶、透明、圆角、阴影四要素齐备 |

### 3.2 Backdrop 启用代码骨架（约 40 行，自行持有，不依赖第三方库）

```csharp
public static class BackdropHelper
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;   // Win11 22H2+
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public enum Backdrop { None = 1, Mica = 2, Acrylic = 3, MicaAlt = 4 }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool TrySetBackdrop(Window window, Backdrop type)
    {
        // 要求 Win11 22H2 (build 22621)+，否则由调用方走降级路径
        if (Environment.OSVersion.Version.Build < 22621) return false;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int v = (int)type;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, 4) != 0) return false;
        int corner = 2; // DWMWCP_ROUND：悬浮条顺便拿免费圆角
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        return true; // 成功后必须把 Window.Background 设为 Transparent 才透得出
    }
}
```

调用时机：`SourceInitialized` 之后；设置成功后 `window.Background = Brushes.Transparent`。
（Wpf.Ui 的 `WindowBackdrop` 内部即同一组调用，MIT 许可，可直接对照其 lepoco/wpfui 源码校验参数细节。）

### 3.3 系统能力检测与降级矩阵

| 环境 | Glass 0 | Glass 3（悬浮条） |
|------|--------|------------------|
| Win11 22H2+（22621+） | Mica Alt | Acrylic + DWM 圆角阴影 |
| Win11 21H2（22000） | 降级：纯色深底 + 自绘柔光层（或试未文档化 `DWMWA_MICA_EFFECT=1029`，失败即纯色） | AllowsTransparency 小窗 + 自绘半透明 |
| Win10 1809+ | 纯色 + 自绘柔光层（可选未文档化 Acrylic 增强，失败即纯色） | AllowsTransparency 小窗（可选 SWCA Acrylic 增强） |
| 关闭"透明效果"/高对比/节能模式 | 一律不透明深浅色底 + 自绘层次（DWM 会自动忽略 backdrop，须监听 `WM_SETTINGCHANGE`/SystemParameters 切降级样式） | 同左 |

**铁律**：任何 backdrop 调用失败都必须落到"不透明底 + 自绘玻璃层"，否则透明背景窗口会变"全透明隐形窗"。

### 3.4 录制期 GPU 预算规则（写进 UI 规范）

1. 录制中：主窗体通常最小化（零开销）；悬浮条仅 Acrylic（DWM 承担），**不重建、不重采样**。
2. 任何状态下禁止：动画 BlurEffect 半径、大面积（>200×200px）实时模糊 Shader、`AllowsTransparency` 主窗体。
3. UI 动画只允许 Opacity / Translate / Scale（Render-only，不触发 Layout，不重新光栅化效果层）。
4. Glass 2 的快照采样只允许在"非录制态"或弹出瞬间做一次，录制开始前 200ms 内不做采样。

---

## 四、风险与坑清单

1. **未文档化 API 风险**：Win10 Acrylic（`SetWindowCompositionAttribute`）与 Win11 21H2 Mica（`DWMWA_MICA_EFFECT=1029`）均无官方契约，Windows 更新可能改变行为——仅作增强，必须有纯色降级。
2. **Mica 预期管理**：Mica 采样的是壁纸而非窗口后实时内容，"隔着主窗体看到背后窗口流动"做不到；液态感要靠 Glass 2 快照层与柔光动画伪造。设计评审需先对齐此认知。
3. **失焦/省电自动降级**：Mica 在窗口失焦、节能模式、关闭透明效果时回退纯色，UI 不能只靠材质撑层次，自绘层必须在无材质时也成立。
4. **AllowsTransparency 三连坑**：软件渲染（大窗体掉帧）、D3D/视频 airspace 冲突、无 DWM 阴影圆角——主窗体禁用；悬浮条只在 Win10 降级路径使用且面积要小。
5. **BlurEffect 成本曲线**：代价≈半径²×面积；全窗模糊在核显上可直接打满 3D 引擎。禁动画半径，禁大面积。
6. **ShaderEffect 拿不到窗后像素**：WPF 像素着色器只能采样元素自身渲染结果，做"折射桌面"需 WGC/Magnification 截图——与录屏采集管线同源，录制时必冲突，排除。
7. **透明背景 + backdrop 失败 = 隐形窗**：设置 `Background=Transparent` 与 backdrop 成功必须成对出现，失败路径要回退不透明色。
8. **调用时机**：HWND 未创建（`SourceInitialized` 前）调 `DwmSetWindowAttribute` 无效；`WindowInteropHelper.EnsureHandle()` 兜底。
9. **第三方库模板感**：Wpf.Ui/ModernWpf/HandyControl 的控件样式会直接把 Fluent/自有设计语言带进产品，违反设计规范"禁止 Fluent 模板感"——可借鉴实现，不可引入样式；若引入 Wpf.Ui 仅用 `FluentWindow` 外壳，须全量覆写 ControlTemplate，性价比不如自写 40 行 BackdropHelper。
10. **XAML Islands 死路**：官方仅支持 WinUI 2 (UWP) 控件嵌入 WPF 且工具链已停更；WinUI 3 不支持嵌入 WPF。不要在此方向投入。
11. **多 DPI / 多显示器**：悬浮条跨屏移动时 DWM 按所在屏合成，Acrylic 无额外成本；但 AllowsTransparency 降级路径下跨 DPI 缩放会有位图模糊，需 `PerMonitorV2` 声明。
12. **RDP/远程桌面与虚拟机**：DWM 材质可能整体不可用，自动化测试与远程调试环境要按降级矩阵验收。

---

## 五、参考链接

**Microsoft 官方文档**
- Mica material（材质语义、壁纸采样、失焦回退）：https://learn.microsoft.com/windows/apps/design/style/mica
- Materials in Windows apps（Mica/Acrylic 总览）：https://learn.microsoft.com/windows/apps/design/style/materials
- DWM_SYSTEMBACKDROP_TYPE 枚举（值与 22621 要求）：https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type
- DwmSetWindowAttribute：https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute
- BlurEffect 类：https://learn.microsoft.com/dotnet/api/system.windows.media.effects.blureffect
- WPF 性能优化（硬件加速/Render Tier/Effect 代价）：https://learn.microsoft.com/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware
- XAML Islands（仅 WinUI 2/UWP 控件嵌入）：https://learn.microsoft.com/windows/apps/desktop/modernize/xaml-islands
- Windows App SDK 讨论：WinUI 3 控件放入 WPF 无支持路径：https://github.com/microsoft/WindowsAppSDK/discussions/3778

**第三方库官方仓库**
- Wpf.Ui（lepoco/wpfui，MIT，FluentWindow/WindowBackdrop 实现参考）：https://github.com/lepoco/wpfui
- ModernWpf：https://github.com/Kinnara/ModernWpf
- HandyControl：https://github.com/HandyOrg/HandyControl

**补充（社区源码分析，与官方文档结论一致）**
- WPF AllowsTransparency 软件渲染原理分析（林德熙）：https://cloud.tencent.com.cn/developer/article/1739122

---

*后续票据建议：① BackdropHelper + 系统检测/降级矩阵（基础设施）；② Glass 1–3 自绘控件库（LiquidPanel/LiquidCard）；③ 悬浮条窗体（Acrylic + 圆角 + Topmost）；④ 录制态 UI 动画降级开关。*
