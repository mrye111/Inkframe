# FFmpeg.AutoGen（方案 B）迁移成本预研（Issue #14）

> 背景：V1 定案 ffmpeg.exe 进程管线（issue #6，IVideoEncoder：InitializeAsync/EncodeFrameAsync/FlushAsync/StopAsync，帧为 D3D11 纹理）。本报告评估 V2 迁往 FFmpeg.AutoGen 直调 DLL 的成本、收益与触发条件。调研日期：2026-08，全部为官方一手来源（GitHub 仓库 / NuGet / FFmpeg 源码头文件）。

## 结论摘要

**结论一句话：FFmpeg.AutoGen 当前维护活跃、原生支持 net8.0、绑定与 FFmpeg 版本同步（9.0.1 ↔ FFmpeg 9.0.1），技术上是 V2 唯一现实选项；D3D11 纹理免 CPU 回读路径在 FFmpeg 官方架构上成立（nvenc 原生接受 AV_PIX_FMT_D3D11 输入帧，且 hwdevice 允许注入自建 ID3D11Device，可与采集共用设备实现真零拷贝）；IVideoEncoder 可保持四方法同形，仅需扩展设备注入与纹理子资源两个参数；LGPL 合规成本可控（用 LGPL 构建 + 硬件编码器即可绕开 GPL），但进程方案在合规与崩溃隔离上仍更省心。迁移估算 15~25 人日，建议只在触发明确性能判据（见 §6）后启动，先做 3~5 人日 Spike 验证零拷贝链路。**

## 各维度事实

### 1. 维护活跃度与 .NET 8 兼容性

| 维度 | 事实 | 来源 |
|---|---|---|
| 仓库 | Ruslan-B/FFmpeg.AutoGen，1607 stars / 361 forks / 未归档 / open issues 10 | GitHub API（2026-08 实测） |
| 提交活跃度 | 2026-08-16~08-22 连续提交（v9.0.1.1 bump、Windows struct layout 修复、mp4 muxing 示例、net8.0 shim 文案等），最近 push 2026-08-22 | GitHub commits API |
| 版本策略 | NuGet 包版本与所绑定 FFmpeg 版本同步：包 9.0.1 目标 FFmpeg 9.0.1；NuGet 最新 9.0.1.1（FFmpeg.AutoGen 与 FFmpeg.AutoGen.Bindings.DynamicallyLoaded 同步发版） | 官方 README / NuGet v3 API |
| .NET 8 兼容 | csproj TargetFrameworks = `net8.0;netstandard2.1;netstandard2.0`，**原生 net8.0 目标**，Inkframe（.NET 8）直接可用 | FFmpeg.AutoGen.csproj |
| 绑定完整度 | 由 CppSharp 从 FFmpeg 头文件自动生成 unsafe 绑定，覆盖全量 FFmpeg API（含 hwcontext/hwframe 全套），"exposes it with minimum modifications" | 官方 README |
| 许可证 | 项目本身已从 LGPL 改为 **MIT**（README 重要公告）；FFmpeg 二进制保持其原始 GPL/LGPL 许可 | 官方 README / LICENSE.txt |
| ⚠️ 风险 | README 顶部公告：项目正转向 "**semi-managed model**"（半托管模式），承诺现有包无破坏性变更；官方支持非常有限，how-to 问题导向 StackOverflow / FFmpeg.AutoGen.Questions 仓库 | 官方 README 公告 |
| Windows DLL 来源 | 官方建议 x64 DLL 取自 gyan.dev 构建，`ffmpeg.RootPath` 指定加载路径；示例工程演示路径配置 | 官方 README |

**评估**：库本身可用性无虞（活跃、net8.0 原生、MIT、版本跟得上 FFmpeg 主线）；主要风险是半托管转型 + 低官方支持——出问题要靠 C 侧资料自救（FFmpeg C 示例可直接转写为 C# 绑定调用，社区有 stjeong/ffmpeg_autogen_cs 转写示例集）。

### 2. IVideoEncoder 接口同形可行性

现有接口（ScreenRecorder.Core/Services/IVideoEncoder.cs）四方法与 DLL 路线一一对应：

| IVideoEncoder | FFmpeg.AutoGen 侧对应 |
|---|---|
| InitializeAsync(options) | avformat_alloc_output_context2 + avcodec_find_encoder_by_name（h264_nvenc / h264_qsv / h264_amf / libx264）+ avcodec_open2 + **av_hwdevice_ctx 初始化（注入采集设备）** + avformat_write_header |
| EncodeFrameAsync(frame) | 把 ID3D11Texture2D 包成 AVFrame（data[0]=纹理指针，data[1]=子资源索引）→ 设 pts → avcodec_send_frame / avcodec_receive_packet 循环 → av_interleaved_write_frame |
| FlushAsync() | avcodec_send_frame(NULL) 排空 + av_write_trailer |
| StopAsync() | 释放 codec/format/hw 上下文，等价于进程版的进程收尾 |

需要扩展的点（量小、且 VideoFrame 已预留 `TextureHandle` 并注释指向 #14）：

1. **设备注入（最关键）**：零拷贝要求编码与采集共用同一 ID3D11Device。FFmpeg 侧 `AVD3D11VADeviceContext.device` 头文件明确 "Must be set by the user"——即允许把我们自建的采集设备塞进 hwdevice 上下文。因此 InitializeAsync 需要能拿到共享设备（建议：VideoEncoderOptions 增加 `ID3D11Device 句柄`，或由 Encoding 层自建设备并回传给 Capture 层——二选一，Spike 定）。
2. **帧元数据**：VideoFrame 需补 **子资源索引**（texture array index，D3D11 AVFrame 的 data[1]）与像素格式/DXGI format；TextureHandle 已有。
3. **时间戳精度**：DLL 路线反而**更强**——VideoFrame.TimestampTicks 已有，用 av_rescale_q 直接换算到流的 time_base 写 AVFrame.pts，精度完全自控（进程版只能靠 -use_wallclock_as_timestamps 或帧计数近似）。VideoEncoderOptions 可能需补 TimeBase/码率/码控模式字段，属记录类型加字段，非接口方法变更。
4. 硬件编码器选择（auto/nvenc/qsv/amf/software）已在 options 里，DLL 版只是从命令行参数变成 avcodec_find_encoder_by_name 映射，逻辑同构。

**结论：四方法签名可完全同形，两种实现可在 DI 层互换；需要扩展的是 options/frame 的字段（设备句柄、子资源索引、格式、timebase），不破坏接口形状。**

### 3. D3D11 纹理免 CPU 回读路径（AVHWDeviceContext D3D11VA）

已在 FFmpeg 源码层逐项验证（非二手资料）：

| 环节 | 事实 | 源码证据 |
|---|---|---|
| 注入自建 D3D11 设备 | av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA) → 设 `AVD3D11VADeviceContext.device` = 采集用 ID3D11Device（"Must be set by the user"）→ av_hwdevice_ctx_init。同设备 = 纹理零拷贝跨上下文可用 | libavutil/hwcontext_d3d11va.h |
| 帧池 | AVHWFramesContext：format=AV_PIX_FMT_D3D11、sw_format=AV_PIX_FMT_NV12 → av_hwframe_ctx_init | 同上 + doc/examples |
| 包装采集帧为 AVFrame | AVFrame.format=AV_PIX_FMT_D3D11，data[0]=ID3D11Texture2D*，data[1]=(intptr_t)子资源索引，hw_frames_ctx 持引用 | D3D11 hwframe 约定（nvenc.c 按此读取） |
| **nvenc 直接吃 D3D11 帧** | nvenc.c 声明支持 AV_PIX_FMT_D3D11 输入（与 CUDA 并列）；pix_fmt==D3D11 时直接从 frames_ctx/device_ctx 的 hwctx 取 ID3D11Device 并 AddRef，按 data[0]/data[1] 注册输入纹理——**全程 GPU 侧，无 CPU 回读** | libavcodec/nvenc.c（pix fmts 表、IS_HWACCEL 宏、d3d11_device_hwctx 取设备分支） |
| qsv/amf | 用 hwmap/derive_device 把 D3D11VA 设备派生为 QSV/AMF 设备（GPU 侧映射，同样免 CPU）；nvenc 不可用时的降级链 | FFmpeg hwcontext 文档（hwdownload/hwmap/derive 机制） |

**收益估算（推断值，须 Spike 实测标定）**：V1 进程管线每帧成本 = GPU→CPU 回读（1080p BGRA ≈ 8.3MB/帧，60fps ≈ 500MB/s 的 Map/staging + PCIe 流量）→ 管道写入（又一次拷贝 + IPC/进程切换）→ ffmpeg 内 swscale BGRA→NV12 CPU 转换 → 才到 nvenc。零拷贝路径把前三项全部消掉，帧预处理 CPU 开销从毫秒级降到 ~0，预计可解出：1080p60 下采集+编码总 CPU 占用显著下降、4K60 从"勉强/掉帧"变为可持续、低功耗设备（核显本）录制时风扇/掉帧改善。数量级（每帧省 3~8ms CPU、500MB/s 内存带宽）为推断，**不构成承诺值**。

### 4. LGPL 合规差异

| 维度 | V1 独立进程（ffmpeg.exe） | V2 DLL 动态链接 |
|---|---|---|
| 绑定库本身 | — | FFmpeg.AutoGen 已是 MIT，无合规负担 |
| FFmpeg 构建选择 | gyan.dev full 构建含 x264 等 = **GPL**；但作为独立进程分发，应用与 ffmpeg.exe 属"mere aggregation"，GPL 不传染应用代码（仅需随分发提供 ffmpeg 源码获取指引） | 动态链接构成 LGPL §6 的"work that uses the Library"：**必须**用 LGPL 配置构建（--enable-lgpl、不含 x264/GPL 组件），否则 GPL 组件经动态链接把整个应用拖入 GPL |
| 义务清单 | 附 FFmpeg 许可证文本 + 源码指引；几乎零额外动作 | LGPL §6 全套：附许可证声明、保证用户可替换/重链 DLL（不可静态链接、不可加锁）、提供 FFmpeg 对应源码 offer |
| 软编兜底 | 可直接用 x264（GPL 约束止于 ffmpeg.exe） | 不能用 x264（GPL）；兜底方案：硬件编码器优先 + OpenH264（BSD，Cisco）或 Windows MFT H.264 作软编退路 |
| 崩溃隔离 | 编码崩溃不拖垮主进程（天然优势，非合规项但与工程权衡相关） | 本机代码崩溃 = 进程崩溃，需自己加守护/转储 |

**合规策略建议**：V2 若落地，用 **LGPL 配置的 FFmpeg 构建**（硬件编码器 nvenc/qsv/amf 均不需要 GPL 组件，正好与零拷贝路径同向）+ 分发时附 LGPL 声明与源码 offer + 保证 DLL 可替换。合规成本一次性约 1~2 人日，不是迁移的阻碍项，但比进程方案多一份持续注意义务。

### 5. 迁移工作量估算（人日）

| 阶段 | 内容 | 估算 |
|---|---|---|
| Spike（先行门槛） | FFmpeg.AutoGen + 注入采集设备 + 包装 D3D11 纹理直喂 h264_nvenc 全链路 PoC；验证同设备零拷贝、qsv/amf derive 降级、实测性能对比 | 3~5 |
| 编码器实现 | FFmpegAutoGenEncoder : IVideoEncoder 四方法 + muxing（MP4）+ pts/time_base 换算 + 硬编降级链 + 错误路径 | 6~9 |
| 接口扩展 | VideoFrame/VideoEncoderOptions 字段扩展 + 设备注入通道 + 双实现 DI 切换 | 1~2 |
| 测试与加固 | 长时录制稳定性、崩溃恢复、分辨率/帧率矩阵、与进程版输出一致性 | 4~6 |
| 合规与打包 | LGPL 构建引入、声明与源码 offer、安装包体积/结构 | 1~2 |
| **合计** | | **15~25 人日** |

风险项：FFmpeg C API 学习曲线与 unsafe 指针调试成本集中在 Spike；半托管转型期的库支持风险靠"版本锁死 + 自托管 DLL"对冲。

### 6. 触发迁移的性能判据（满足任一即启动 Spike）

1. 1080p60 录制时采集+编码链路 CPU 占用持续 >15%（中端机型），或每帧预处理耗时 >3ms；
2. 4K 录制无法稳定 60fps / 出现因回读带宽导致的规律性掉帧；
3. 需要进程管线给不了的能力：精确逐帧时间戳控制、GPU 侧预编码滤镜（缩放/光标合成后不落地）、无进程重启的暂停续录、编码启动延迟 <500ms；
4. stdin 管道吞吐成为实测瓶颈（500MB/s 级流量下丢帧）。

当前 V1 未出现上述任一情况 → **不迁移，保持进程管线**，本报告归档为 V2 备料。

## 迁移决策矩阵

| 维度 | V1 进程管线（现状） | V2 FFmpeg.AutoGen DLL | 判定 |
|---|---|---|---|
| 帧路径 | GPU→CPU 回读→管道→CPU 转码→硬编 | GPU 内零拷贝直喂 nvenc（同设备） | V2 显著优 |
| CPU/带宽开销 | 高（~500MB/s @1080p60 + 多次拷贝） | 极低 | V2 优 |
| 接口形状 | IVideoEncoder 四方法 | 同形，仅扩展 options/frame 字段 | 持平 |
| 时间戳控制 | 间接（CLI 参数/帧计数） | 完全自控（av_rescale_q + pts） | V2 优 |
| 崩溃隔离 | 编码进程独立，崩溃不伤主程序 | 同进程，native 崩溃即全崩 | V1 优 |
| 可调试性 | 命令行可见、日志现成、CLI 可复现 | unsafe 指针 + C API，调试门槛高 | V1 优 |
| 合规负担 | 近零（mere aggregation） | LGPL §6 义务 + 需 LGPL 构建 + 软编不能 x264 | V1 优 |
| 部署 | 一个 ffmpeg.exe（体积大但简单） | 一组 DLL + RootPath 管理（可用 DynamicallyLoaded 包改善替换性） | 持平偏 V1 |
| 库风险 | 无第三方绑定依赖 | 半托管转型 + 低官方支持 | V1 优 |
| 迁移成本 | 0 | 15~25 人日 | V1 优 |
| **总评** | **默认留守** | **触发判据命中后启动** | 见 §6 |

## 参考链接

- FFmpeg.AutoGen 仓库（README / 许可证公告 / 生成器说明）：https://github.com/Ruslan-B/FFmpeg.AutoGen
- FFmpeg.AutoGen.csproj（TargetFrameworks net8.0）：https://github.com/Ruslan-B/FFmpeg.AutoGen/blob/master/FFmpeg.AutoGen/FFmpeg.AutoGen.csproj
- NuGet：FFmpeg.AutoGen / FFmpeg.AutoGen.Bindings.DynamicallyLoaded（最新 9.0.1.1）：https://www.nuget.org/packages/FFmpeg.AutoGen/
- FFmpeg.AutoGen.Questions（官方支持渠道）：https://github.com/Ruslan-B/FFmpeg.AutoGen.Questions
- C# 转写示例集（stjeong/ffmpeg_autogen_cs）：https://github.com/stjeong/ffmpeg_autogen_cs
- FFmpeg hwcontext_d3d11va.h（AVD3D11VADeviceContext，device 由用户注入）：https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hwcontext_d3d11va.h
- FFmpeg nvenc.c（AV_PIX_FMT_D3D11 输入支持、从 hwctx 取 ID3D11Device）：https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/nvenc.c
- FFmpeg 官方文档（hwcontext / hwframe API）：https://www.ffmpeg.org/documentation.html
- FFmpeg License/Legal（GPL/LGPL 边界）：https://www.ffmpeg.org/legal.html
- gyan.dev FFmpeg Windows 构建（官方推荐 DLL 来源；注意 full 构建为 GPL）：https://www.gyan.dev/ffmpeg/builds/
