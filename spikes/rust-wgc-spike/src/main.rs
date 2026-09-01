//! Rust WGC Spike（issue #20）：windows-rs 版 WGC→H.264→MP4 最危险链路验证
//! C# 参照：spikes/WgcFfmpegSpike/Program.cs（坑表注释全部适用）

use std::io::Write;
use std::process::{Command, Stdio};
use std::sync::mpsc;
use std::time::{Duration, Instant};

use windows::core::{factory, Interface, Ref};
use windows::Foundation::TypedEventHandler;
use windows::Graphics::Capture::{Direct3D11CaptureFramePool, GraphicsCaptureItem};
use windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;
use windows::Graphics::DirectX::DirectXPixelFormat;
use windows::Graphics::SizeInt32;
use windows::Win32::Foundation::{HMODULE, POINT};
use windows::Win32::Graphics::Direct3D::{D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL, D3D_FEATURE_LEVEL_11_0};
use windows::Win32::Graphics::Direct3D11::*;
use windows::Win32::System::WinRT::Direct3D11::{CreateDirect3D11DeviceFromDXGIDevice, IDirect3DDxgiInterfaceAccess};
use windows::Win32::Graphics::Dxgi::Common::*;
use windows::Win32::Graphics::Gdi::{MonitorFromPoint, MONITOR_DEFAULTTOPRIMARY};
use windows::Win32::System::WinRT::Graphics::Capture::IGraphicsCaptureItemInterop;

const TARGET_FPS: u32 = 30;
const FRAME_BUFFERS: usize = 4;

fn main() -> windows::core::Result<()> {
    let args: Vec<String> = std::env::args().collect();
    let pick = |flag: &str| -> Option<String> {
        args.iter().position(|a| a == flag).and_then(|i| args.get(i + 1)).cloned()
    };
    let seconds: u32 = pick("--seconds").and_then(|v| v.parse().ok()).unwrap_or(30);
    let out_file = pick("--out").unwrap_or_else(|| "spike_rust.mp4".to_string());

    let encoder = probe_encoder().expect("回退链全部失败：无可用 H.264 编码器");
    println!("[spike] encoder = {}", encoder);

    unsafe { run(seconds, &out_file, &encoder) }
}

/// 编码器运行时试编码探测（-h 探测是假阳性，C# spike 实证）
fn probe_encoder() -> Option<&'static str> {
    for enc in ["h264_nvenc", "h264_qsv", "h264_amf", "libopenh264", "libx264"] {
        let ok = Command::new("ffmpeg")
            .args(["-hide_banner", "-loglevel", "error", "-f", "lavfi",
                   "-i", "testsrc2=size=64x64:duration=0.1:rate=10",
                   "-c:v", enc, "-f", "null", "-"])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .map(|s| s.success())
            .unwrap_or(false);
        if ok { return Some(enc); }
        println!("[spike] encoder {} runtime probe FAILED, fallback...", enc);
    }
    None
}

unsafe fn run(seconds: u32, out_file: &str, encoder: &str) -> windows::core::Result<()> {
    // ---- 主显示器 ----
    let hmon = MonitorFromPoint(POINT { x: 0, y: 0 }, MONITOR_DEFAULTTOPRIMARY);
    println!("[spike] HMONITOR = {:?}", hmon);

    // ---- D3D11 设备（BGRA 支持是 WGC 硬要求） ----
    let mut device: Option<ID3D11Device> = None;
    let mut context: Option<ID3D11DeviceContext> = None;
    let mut fl = D3D_FEATURE_LEVEL_11_0;
    let levels = [D3D_FEATURE_LEVEL_11_0];
    D3D11CreateDevice(
        None,
        D3D_DRIVER_TYPE_HARDWARE,
        HMODULE::default(),
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        Some(&levels[..]),
        D3D11_SDK_VERSION,
        Some(&mut device),
        Some(&mut fl),
        Some(&mut context),
    )?;
    let device = device.unwrap();
    let context = context.unwrap();
    println!("[spike] D3D11 FL = {:?}", fl);

    // ---- WinRT IDirect3DDevice ----
    let dxgi: windows::Win32::Graphics::Dxgi::IDXGIDevice = device.cast()?;
    let inspectable = CreateDirect3D11DeviceFromDXGIDevice(&dxgi)?;
    let winrt_device: IDirect3DDevice = inspectable.cast()?;

    // ---- GraphicsCaptureItem（监视器）----
    let interop: IGraphicsCaptureItemInterop =
        factory::<GraphicsCaptureItem, IGraphicsCaptureItemInterop>()?;
    let item: GraphicsCaptureItem = interop.CreateForMonitor(hmon)?;
    let size: SizeInt32 = item.Size()?;
    let (width, height) = (size.Width as u32, size.Height as u32);
    println!("[spike] capture size = {}x{} (物理像素)", width, height);

    // ---- FramePool（FreeThreaded）+ Session ----
    let pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
        &winrt_device,
        DirectXPixelFormat::B8G8R8A8UIntNormalized,
        2,
        size,
    )?;
    let session = pool.CreateCaptureSession(&item)?;
    let _ = session.SetIsCursorCaptureEnabled(false);
    println!("[spike] cursor capture disabled");

    // ---- staging 纹理 ----
    let desc = D3D11_TEXTURE2D_DESC {
        Width: width,
        Height: height,
        MipLevels: 1,
        ArraySize: 1,
        Format: DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc: DXGI_SAMPLE_DESC { Count: 1, Quality: 0 },
        Usage: D3D11_USAGE_STAGING,
        BindFlags: 0,
        CPUAccessFlags: D3D11_CPU_ACCESS_READ.0 as u32,
        MiscFlags: 0,
    };
    let mut staging: Option<ID3D11Texture2D> = None;
    device.CreateTexture2D(&desc, None, Some(&mut staging))?;
    let staging = staging.unwrap();

    // ---- ffmpeg 进程管线 ----
    let ffmpeg_args = [
        "-y", "-hide_banner", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "bgra",
        "-s", &format!("{}x{}", width, height),
        "-r", &TARGET_FPS.to_string(),
        "-i", "pipe:0",
        "-c:v", encoder,
        "-pix_fmt", "yuv420p",
        out_file,
    ];
    let mut ffmpeg = Command::new("ffmpeg")
        .args(ffmpeg_args)
        .stdin(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .expect("ffmpeg 启动失败");
    let mut stdin = ffmpeg.stdin.take().unwrap();

    // ---- 帧通道 + 缓冲池 ----
    let (frame_tx, frame_rx) = mpsc::channel::<Vec<u8>>();
    let (pool_tx, pool_rx) = mpsc::channel::<Vec<u8>>();
    let frame_bytes = (width * height * 4) as usize;
    for _ in 0..FRAME_BUFFERS {
        let _ = pool_tx.send(vec![0u8; frame_bytes]);
    }

    let writer = std::thread::spawn(move || {
        let mut written = 0u64;
        while let Ok(buf) = frame_rx.recv() {
            if stdin.write_all(&buf).is_err() { break; }
            written += 1;
            let _ = pool_tx.send(buf);   // 回收缓冲
        }
        drop(stdin);
        written
    });

    // ---- 采集回调 ----
    let arrived = std::sync::Arc::new(std::sync::atomic::AtomicU64::new(0));
    let written_cnt = std::sync::Arc::new(std::sync::atomic::AtomicU64::new(0));
    let start = Instant::now();
    let done = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));

    {
        let arrived = arrived.clone();
        let written_cnt = written_cnt.clone();
        let done_in = done.clone();
        let frame_tx_in = frame_tx.clone();
        let handler = TypedEventHandler::new(
            move |pool_ref: Ref<'_, Direct3D11CaptureFramePool>, _args: Ref<'_, windows::core::IInspectable>| {
                let pool = pool_ref.ok()?;
                if done_in.load(std::sync::atomic::Ordering::Relaxed) { return Ok(()); }
                let frame = pool.TryGetNextFrame()?;
                arrived.fetch_add(1, std::sync::atomic::Ordering::Relaxed);

                // 墙钟抽稀到目标 fps（§49 最小形态，C# spike 实证必需）
                let expected = (start.elapsed().as_secs_f64() * TARGET_FPS as f64) as u64;
                if written_cnt.load(std::sync::atomic::Ordering::Relaxed) >= expected {
                    return Ok(());
                }

                // 帧 → staging → CPU 缓冲 → 通道
                let surface = frame.Surface()?;
                let access: IDirect3DDxgiInterfaceAccess = surface.cast()?;
                let tex: ID3D11Texture2D = access.GetInterface()?;
                context.CopyResource(&staging, &tex);

                let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
                context.Map(&staging, 0, D3D11_MAP_READ, 0, Some(&mut mapped))?;
                let mut buf = pool_rx.recv().unwrap_or_else(|_| vec![0u8; frame_bytes]);
                let row_bytes = (width * 4) as usize;
                let src = mapped.pData as *const u8;
                for y in 0..height as usize {
                    std::ptr::copy_nonoverlapping(
                        src.add(y * mapped.RowPitch as usize),
                        buf.as_mut_ptr().add(y * row_bytes),
                        row_bytes,
                    );
                }
                context.Unmap(&staging, 0);

                if frame_tx_in.send(buf).is_ok() {
                    written_cnt.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
                }
                Ok(())
            },
        );
        let token = pool.FrameArrived(&handler)?;

        session.StartCapture()?;
        println!("[spike] recording {}s ...", seconds);
        std::thread::sleep(Duration::from_secs(seconds as u64));
        done.store(true, std::sync::atomic::Ordering::Relaxed);
        std::thread::sleep(Duration::from_millis(200));   // 让在途帧落完
        pool.RemoveFrameArrived(token)?;   // 关键：先摘 handler → frame_tx_in 随闭包释放
        drop(handler);
    }

    session.Close()?;
    pool.Close()?;
    drop(frame_tx);   // 两个 sender 全断 → 写线程收 EOF → 关闭 stdin → ffmpeg 收尾封装
    let writer_written = writer.join().unwrap_or(0);
    let status = ffmpeg.wait().expect("ffmpeg wait 失败");

    let wall = seconds as f64;
    let a = arrived.load(std::sync::atomic::Ordering::Relaxed);
    println!(
        "[spike] wall = {:.2}s, arrived = {}, written = {}, effective fps = {:.1}",
        wall, a, writer_written, writer_written as f64 / wall
    );
    println!("[spike] ffmpeg exit = {:?}, output = {}", status.code(), out_file);

    let probe = Command::new("ffprobe")
        .args(["-v", "error", "-select_streams", "v:0",
               "-show_entries", "stream=codec_name,width,height,avg_frame_rate",
               "-show_entries", "format=duration",
               "-of", "default=noprint_wrappers=1", out_file])
        .output();
    if let Ok(o) = probe {
        println!("----- ffprobe -----\n{}", String::from_utf8_lossy(&o.stdout));
    }
    Ok(())
}
