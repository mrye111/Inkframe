# Windows.Graphics.Capture 在 WPF 的落地细节调研（Issue #5）

> 对应需求文档 §44/§51/§52（采集层）与 §30（鼠标高亮）。目标运行时：Windows 10 1803+（WGC 可用下限），重点支持 Windows 10 22H2 / Windows 11。

## 结论摘要

**推荐方案一句话：在 WPF/.NET 8 中通过 IGraphicsCaptureItemInterop（CreateForWindow/CreateForMonitor）创建 GraphicsCaptureItem，配合 Direct3D11CaptureFramePool.CreateFreeThreaded + 专用工作线程采集帧；区域录制用"整屏采集 + CopySubresourceRegion 帧内裁剪"；关闭 WGC 自带光标（IsCursorCaptureEnabled=false）后在合成阶段自绘光标与点击涟漪；进程以 Per-Monitor V2 DPI 感知清单启动。**

## 事实清单

### 1. WGC 在 WPF/.NET 8 的初始化链

| 步骤 | 事实 | 备注 |
|---|---|---|
| 可用性 | WGC 是 WinRT API，桌面（非 UWP/非打包）进程自 **Windows 10 1803 (17134)** 起可用，无需 MSIX 打包身份 | Microsoft Learn 屏幕采集文档明确支持 Win32 |
| HWND/HMONITOR → GraphicsCaptureItem | 使用 COM 互操作接口 **IGraphicsCaptureItemInterop**（windows.graphics.capture.interop.h，IID 3628E81B-3CAC-4C60-B7F4-23CE0E0C3356）：先取该工厂（WindowsRuntimeMarshal.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem")），再调 CreateForWindow(hwnd, IID, out item) / CreateForMonitor(hmon, IID, out item) | 该路径**不需要** GraphicsCapturePicker、不需要用户交互授权；C# 侧用 CsWin32（Windows.Win32 包）或手写 P/Invoke |
| 替代新 API | Windows 11 24H2+ 另有 GraphicsCaptureItem.TryCreateFromWindowId/DisplayId（基于 WindowId/DisplayId） | 适合做下限之上的增强，非必须 |
| D3D 设备桥接 | 自建 ID3D11Device（D3D11CreateDevice，BgraSupport flag），通过 **CreateDirect3D11DeviceFromDXGIDevice** 得到 WinRT IDirect3DDevice | 采集线程与编码线程可共享同一 D3D11 device + multithread 保护 |
| FramePool | **Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size)** | 关键：FreeThreaded 版本**不依赖 DispatcherQueue**，FrameArrived 在线程池线程回调——避免在 WPF 进程里创建 DispatcherQueueController 的整套麻烦；官方文档推荐桌面进程用此路径 |
| Session | session = framePool.CreateCaptureSession(item) → 设置光标/边框属性 → session.StartCapture() | session 与 pool 必须保持强引用，Dispose 即停止 |
| 帧回调线程模型 | FrameArrived 在后台线程触发；回调内**必须立刻** TryGetNextFrame() 并把 frame.Surface 内容 CopyFromTexture/CopySubresourceRegion 到自己的纹理（池内仅 2 缓冲，占用过久会丢帧）；UI 展示再 marshal 到 Dispatcher | 推荐：专用采集线程消费 FrameArrived → 拷入环形纹理队列 → 编码线程；UI 只拿预览缩略 |
| 取帧像素 | frame.Surface 是 WinRT IDirect3DSurface，经 **IDirect3DDxgiInterfaceAccess** 取回 ID3D11Texture2D | robmikh/Win32CaptureSample 为标准参照实现 |
| 尺寸变化 | 订阅 item.SizeChanged：重建 FramePool（先 Dispose 旧 pool 再 CreateFreeThreaded） | 窗口缩放、显示器热插拔都会触发 |

**结论：WPF 落地不需要 UWP 容器、不需要 DispatcherQueue。链路为：EnumWindows/EnumDisplayMonitors → HWND/HMONITOR → IGraphicsCaptureItemInterop → GraphicsCaptureItem → D3D11 device + CreateDirect3D11DeviceFromDXGIDevice → CreateFreeThreaded → CreateCaptureSession → StartCapture。**

### 2. 窗口枚举 → GraphicsCaptureItem 的可靠路径

| 过滤规则 | API | 说明 |
|---|---|---|
| 顶层窗口枚举 | EnumWindows | 只枚举顶层 HWND |
| 可见性 | IsWindowVisible + GetWindowLong(GWL_STYLE) & WS_VISIBLE | 双保险 |
| Cloaked（UWP/虚拟桌面） | DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &value, 4)，value != 0 则排除 | **必须**，否则枚举出一堆不可见的 UWP 壳窗口（ApplicationFrameHost 子内容） |
| 有标题 | GetWindowTextLength(hwnd) > 0 | 排掉无标题后台窗 |
| 工具窗 | GetWindowLong(GWL_EXSTYLE) & WS_EX_TOOLWINDOW 排除 | 排除浮动工具条等 |
| 排除自身 | 与本进程 PID / 本窗口 HWND 比较排除 | 防自录 |
| 显示器枚举 | EnumDisplayMonitors + GetMonitorInfo（MONITORINFOEXW 取 rcMonitor + szDevice） | rcMonitor 是**虚拟坐标系**坐标 |
| HWND → Item | IGraphicsCaptureItemInterop::CreateForWindow | 官方唯一可靠路径；不要用 GraphicsCapturePicker（需用户点选，不适合预设窗口录制） |
| HMONITOR → Item | IGraphicsCaptureItemInterop::CreateForMonitor | 全屏/区域模式使用 |

UWP 应用窗口注意点：真实内容进程挂在 ApplicationFrameHost 下，若需要进程名/图标需经 GetWindowThreadProcessId + 子窗口链修正；但采集只需把壳 HWND 传给 CreateForWindow 即可正常录制。

### 3. 自定义区域录制

| 方案 | 结论 | 说明 |
|---|---|---|
| **整屏采集 + 帧内裁剪（推荐）** | ✅ 采用 | WGC **没有区域裁剪 API**（GraphicsCaptureItem 只有整体 Size，CaptureSession 无 clip 参数）。做法：CreateForMonitor(区域所在 HMONITOR) 全屏采集，FrameArrived 回调里用 **CopySubresourceRegion** 把区域子矩形从帧纹理拷到目标尺寸纹理（纯 GPU，零 CPU 成本），再送编码器。虚拟坐标 → 监视器局部物理像素坐标的换算在选区时一次性完成 |
| Magnification API | ❌ 仅备选 | 基于 GDI 的老 API，DPI 行为差、无法采集受保护/独占全屏 DX 内容、性能差；仅在极端兼容场景考虑，本项目不建议 |
| 采窗口再裁窗口内子区域 | ✅ 同法 | 窗口模式下同样用 CopySubresourceRegion 裁剪，窗口外区域天然不存在 |

跨屏选区：选区横跨多显示器时，需为每个相交的 HMONITOR 各建一个采集会话，各自裁剪后拼接（或 UI 限制选区在单屏内——**建议首版限制单屏**）。

### 4. 多显示器坐标系与 DPI

- **虚拟坐标系**：GetSystemMetrics(SM_XVIRTUALSCREEN/SM_YVIRTUALSCREEN) 原点可为负（主屏左侧/上方有副屏时）。所有显示器矩形（rcMonitor）都在该坐标系内。
- **WGC 帧是物理像素**：HMONITOR 采集帧尺寸 = 该监视器物理分辨率，与该屏 DPI 缩放无关；WPF 的选区 UI 坐标是 DPI 缩放后的逻辑坐标。**必须做逻辑→物理换算**：物理 = 逻辑 × (dpi / 96)，dpi 用 GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, ...)。
- **Per-Monitor V2 清单（推荐方式）**：app.manifest 中加入：
  ```xml
  <asmv3:application>
    <asmv3:windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    </asmv3:windowsSettings>
  </asmv3:application>
  ```
  清单优于运行时 API（SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)），后者必须在创建任何窗口前调用且与清单混用易出错。.NET 8 WPF 对 PerMonitorV2 支持良好，但窗口 Left/Top 仍是 DPI 缩放坐标，定位选区覆盖窗时需注意（参见 dotnet/wpf#3105）。
- 显示器热插拔/缩放变更：监听 WM_DISPLAYCHANGE / WM_DPICHANGED，刷新监视器列表并联动 SizeChanged 重建逻辑。

### 5. 光标与鼠标高亮（§30）

| 事实 | 说明 |
|---|---|
| GraphicsCaptureSession.IsCursorCaptureEnabled | 控制 WGC 是否在帧中合成系统光标；**Windows 10 1903+** 才有此属性，必须用 ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession","IsCursorCaptureEnabled") 守护后赋值，低版本跳过（默认即捕获光标） |
| 推荐：关闭 WGC 光标，自绘合成 | 设置 IsCursorCaptureEnabled = false，在合成/编码前自绘光标纹理。好处：可做高亮圆环、点击涟漪，光标在任意缩放裁剪下位置精确可控 |
| 自绘合成路径 | 每帧 GetCursorInfo（取形状 + 屏幕坐标）→ 形状变化时缓存 DrawIconEx 到 RGBA 位图 → 坐标换算到帧物理像素 → 用 D2D/着色器把光标纹理 + 高亮圆环 + 涟漪动画合成到帧纹理上 |
| 点击事件 | 低级鼠标钩子 WH_MOUSE_LL（或 GetAsyncKeyState 轮询）记录按下/抬起时间戳，涟漪动画在合成时按录制时间轴回放（若允许后期调整时间轴，涟漪事件应存元数据而非逐帧烘焙） |
| 方案对比 | WGC 自带光标：零成本但无法高亮/定制；自绘：成本略增但满足 §30 全部需求。**选自绘** |

## 推荐实现路径

1. **进程配置**：app.manifest 声明 PerMonitorV2 + dpiAware true/pm；csproj 引用 Microsoft.Windows.CsWin32（生成 WGC/D3D11 互操作）或手写互操作。
2. **枚举层**：EnumWindows（Visible && !Cloaked && 有标题 && !ToolWindow && !自身）构建窗口列表；EnumDisplayMonitors + MONITORINFOEXW 构建显示器列表（含虚拟坐标矩形 + DPI）。
3. **采集初始化**：HWND/HMONITOR → IGraphicsCaptureItemInterop → GraphicsCaptureItem → D3D11CreateDevice + CreateDirect3D11DeviceFromDXGIDevice → Direct3D11CaptureFramePool.CreateFreeThreaded(..., 2, item.Size) → CreateCaptureSession → IsCursorCaptureEnabled=false（守护）→ StartCapture()。
4. **帧管线**：FrameArrived（后台线程）→ TryGetNextFrame → IDirect3DDxgiInterfaceAccess 取纹理 → **区域模式：CopySubresourceRegion 裁剪** → 拷入自有环形缓冲（2~3 张纹理）→ 编码线程（MF/H.264）消费；UI 预览另走低频缩略分支。
5. **光标合成**：合成阶段叠加高亮圆环 + 光标纹理 + 涟漪（按录制时间轴），全部在帧物理像素坐标系下进行。
6. **生命周期**：订阅 SizeChanged 重建 pool；显示器/DPI 变化刷新枚举；录制结束按序 Dispose session → pool → 纹理 → device。
7. **首版边界**：区域选区限制在单显示器内；HDR 按 SDR 处理（见坑）。

## 已知坑与风险清单

| 坑 | 影响 | 缓解 |
|---|---|---|
| **黄色边框**（Windows 11 隐私提示） | 采集窗口/屏幕时系统画黄框，老版本无法关闭 | Win11 **24H2+** 可用 GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.ProgrammaticAccess) 后设 session.IsBorderRequired=false；低版本接受黄框存在（竞品同此限制）。注意 24H2 上 IsBorderRequired 有 crash 上报（robmikh/Win32CaptureSample#85），需 try/catch + 版本守护 |
| **受保护/DRM 内容** | 帧中该区域为黑色（Netflix、播放器 DRM 通道） | 无解，文档化为预期行为；可检测全黑帧提示用户 |
| **管理员权限窗口** | 非提权采集进程用 CreateForWindow 采集提权窗口失败；UAC 同意弹窗（安全桌面）无法采集 | 常规监视器采集不受影响；UAC 弹窗期间采集会黑/暂停——文档化；不要求软件提权运行 |
| **HDR 输出** | FramePool 用 B8G8R8A8_UNorm 时 HDR 内容被截断/色调映射失真 | 首版按 SDR 处理并文档化；后续可探测 HDR 监视器并申请浮点格式 pool |
| **FrameArrived 占用过久丢帧** | 池仅 2 缓冲，回调里做重活直接丢帧 | 回调内只做 GPU 拷贝，编码/落盘全部异步 |
| **最小化窗口** | 好消息：WGC 可正常采集最小化窗口（优于 PrintWindow），但部分应用最小化后停渲染 | 个别应用画面静止属应用行为，文档化 |
| **Win10 1803~1809 无 IsCursorCaptureEnabled** | 属性不存在时赋值抛异常 | ApiInformation.IsPropertyPresent 守护 |
| **DPI 坐标换算错误** | 选区偏移、光标位置错位 | 一切换算集中在单一坐标模块，单元测试覆盖负原点虚拟坐标 |
| **SizeChanged 竞态** | 重建 pool 时帧回调仍在旧 pool 上触发 | 加锁/串行化重建；Dispose 前先停 session |
| **GraphicsCapturePicker 体验** | 需用户点选、需 IInitializeWithWindow | 不走 Picker，全走 interop 编程路径 |

## 参考链接

- [Screen capture（Microsoft Learn 官方 WGC 指南）](https://learn.microsoft.com/windows/uwp/audio-video-camera/screen-capture)
- [IGraphicsCaptureItemInterop（HWND/HMONITOR → GraphicsCaptureItem）](https://learn.microsoft.com/windows/win32/api/windows.graphics.capture.interop/nn-windows-graphics-capture-interop-igraphicscaptureiteminterop)
- [Direct3D11CaptureFramePool.CreateFreeThreaded](https://learn.microsoft.com/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)
- [GraphicsCaptureSession（IsCursorCaptureEnabled / IsBorderRequired）](https://learn.microsoft.com/uwp/api/windows.graphics.capture.graphicscapturesession)
- [GraphicsCaptureAccessKind（24H2 去黄框）](https://learn.microsoft.com/uwp/api/windows.graphics.capture.graphicscaptureaccesskind)
- [CreateDispatcherQueueController（备选：非 FreeThreaded 路径）](https://learn.microsoft.com/windows/win32/api/dispatcherqueue/nf-dispatcherqueue-createdispatcherqueuecontroller)
- [Setting the default DPI awareness for a process（PerMonitorV2 清单）](https://learn.microsoft.com/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)
- [DwmGetWindowAttribute / DWMWA_CLOAKED](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute)
- [EnumDisplayMonitors](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)
- [robmikh/Win32CaptureSample（WGC 桌面采集参照实现）](https://github.com/robmikh/Win32CaptureSample)
- [dotnet/wpf#3105（WPF PerMonitorV2 窗口定位问题）](https://github.com/dotnet/wpf/issues/3105)

