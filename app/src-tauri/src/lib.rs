mod config;
mod recording;

use config::AppConfig;
use recording::{RecordingManager, RecordingState, SharedRecordingManager, StartRecordingRequest};
use std::sync::Mutex;
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager, State};

/// 状态变化事件负载（前端 recording-state-changed 订阅）。
#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct StateChangedPayload {
    old_state: RecordingState,
    new_state: RecordingState,
}

fn transition(app: &AppHandle, mgr: &State<SharedRecordingManager>, f: impl FnOnce(&mut RecordingManager) -> Result<(), String>) -> Result<(), String> {
    let mut m = mgr.lock().map_err(|e| e.to_string())?;
    let old = m.state();
    f(&mut m)?;
    let new = m.state();
    if old != new {
        let _ = app.emit("recording-state-changed", StateChangedPayload { old_state: old, new_state: new });
        tracing::info!("state {:?} -> {:?}", old, new);
    }
    Ok(())
}

#[tauri::command]
fn recording_status(mgr: State<SharedRecordingManager>) -> recording::RecordingStatus {
    mgr.lock().map(|m| m.status()).unwrap_or_else(|_| recording::RecordingStatus {
        state: RecordingState::Idle,
        elapsed_secs: 0.0,
        output_path: None,
    })
}

#[tauri::command]
fn start_recording(app: AppHandle, mgr: State<'_, SharedRecordingManager>, request: StartRecordingRequest) -> Result<(), String> {
    transition(&app, &mgr, |m| m.start(request))?;

    // §20 倒计时：3 秒后进入 Recording（可取消在后续票据做）
    let app2 = app.clone();
    tauri::async_runtime::spawn(async move {
        tokio::time::sleep(Duration::from_secs(3)).await;
        if let Some(mgr) = app2.try_state::<SharedRecordingManager>() {
            let _ = transition(&app2, &mgr, |m| {
                m.enter_recording();
                Ok(())
            });
        }
    });
    Ok(())
}

#[tauri::command]
fn pause_recording(app: AppHandle, mgr: State<'_, SharedRecordingManager>) -> Result<(), String> {
    transition(&app, &mgr, |m| m.pause())
}

#[tauri::command]
fn resume_recording(app: AppHandle, mgr: State<'_, SharedRecordingManager>) -> Result<(), String> {
    transition(&app, &mgr, |m| m.resume())
}

#[tauri::command]
fn stop_recording(app: AppHandle, mgr: State<'_, SharedRecordingManager>) -> Result<(), String> {
    transition(&app, &mgr, |m| m.stop())
}

#[tauri::command]
fn get_config(mgr: State<'_, Mutex<AppConfig>>) -> AppConfig {
    mgr.lock().map(|c| c.clone()).unwrap_or_default()
}

#[tauri::command]
fn save_config(mgr: State<'_, Mutex<AppConfig>>, config: AppConfig) {
    if let Ok(mut c) = mgr.lock() {
        *c = config;
        c.save();
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    // tracing → %LocalAppData%/Inkframe/Logs（§57）
    let log_dir = directories::ProjectDirs::from("com", "inkframe", "Inkframe")
        .map(|p| p.cache_dir().join("Logs"))
        .unwrap_or_else(|| std::path::PathBuf::from("."));
    std::fs::create_dir_all(&log_dir).ok();
    let file_appender = tracing_appender::rolling::daily(&log_dir, "inkframe.log");
    tracing_subscriber_init(file_appender);

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .manage(SharedRecordingManager::default())
        .manage(Mutex::new(AppConfig::load()))
        .invoke_handler(tauri::generate_handler![
            recording_status,
            start_recording,
            pause_recording,
            resume_recording,
            stop_recording,
            get_config,
            save_config,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn tracing_subscriber_init(file_appender: tracing_appender::rolling::RollingFileAppender) {
    use tracing_subscriber::fmt::writer::MakeWriterExt;
    let _ = tracing_subscriber::fmt()
        .with_writer(file_appender.and(std::io::stdout))
        .with_ansi(false)
        .try_init();
}
