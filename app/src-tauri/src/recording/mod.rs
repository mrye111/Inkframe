//! 录制编排核心（RecordingManager 壳）。
//! C# 参考实现：ScreenRecorder.Core/Recording/RecordingManager.cs（已 E2E 验证）。
//! 本骨架只迁状态机与 IPC；采集/编码接线在后续票据完成（spike 路径已验证：spikes/rust-wgc-spike）。

use serde::{Deserialize, Serialize};
use std::sync::Mutex;
use std::time::Instant;

/// 录制状态机（文档 §7.1 / §20-24）。
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RecordingState {
    Idle,
    Countdown,
    Recording,
    Paused,
    Stopping,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RecordingStatus {
    pub state: RecordingState,
    /// 有效录制时长（秒，剔除暂停累计）
    pub elapsed_secs: f64,
    pub output_path: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StartRecordingRequest {
    pub mode: RecordingMode,
    pub target_window: Option<String>,
    pub region: Option<Region>,
}

#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RecordingMode {
    FullScreen,
    Window,
    Region,
}

#[derive(Debug, Clone, Copy, Deserialize)]
pub struct Region {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

/// 录制会话（§46）：统一时钟基线 + 暂停累计（§22）。
struct Session {
    started: Instant,
    pause_started: Option<Instant>,
    accumulated_pause: Duration,
    output_path: String,
}

use std::time::Duration;

pub struct RecordingManager {
    state: RecordingState,
    session: Option<Session>,
}

impl Default for RecordingManager {
    fn default() -> Self {
        Self { state: RecordingState::Idle, session: None }
    }
}

impl RecordingManager {
    pub fn state(&self) -> RecordingState {
        self.state
    }

    pub fn status(&self) -> RecordingStatus {
        let elapsed = self.session.as_ref().map(|s| {
            let wall = s.started.elapsed();
            let pausing = s.pause_started.map(|p| p.elapsed()).unwrap_or_default();
            (wall - s.accumulated_pause - pausing).as_secs_f64()
        }).unwrap_or(0.0);
        RecordingStatus {
            state: self.state,
            elapsed_secs: elapsed,
            output_path: self.session.as_ref().map(|s| s.output_path.clone()),
        }
    }

    pub fn start(&mut self, req: StartRecordingRequest) -> Result<(), String> {
        if self.state != RecordingState::Idle {
            return Err(format!("当前状态 {:?} 不允许开始", self.state));
        }
        // TODO(#后续): 磁盘检查(§27)/命名(§25)/崩溃标记(§56)/采集+编码接线（rust-wgc-spike 路径）
        self.session = Some(Session {
            started: Instant::now(),
            pause_started: None,
            accumulated_pause: Duration::ZERO,
            output_path: String::new(),
        });
        self.state = RecordingState::Countdown;
        Ok(())
    }

    /// 倒计时结束（由倒计时任务调用）。
    pub fn enter_recording(&mut self) {
        if self.state == RecordingState::Countdown {
            self.state = RecordingState::Recording;
        }
    }

    pub fn pause(&mut self) -> Result<(), String> {
        if self.state != RecordingState::Recording {
            return Err(format!("当前状态 {:?} 不允许暂停", self.state));
        }
        if let Some(s) = &mut self.session {
            s.pause_started = Some(Instant::now());
        }
        self.state = RecordingState::Paused;
        Ok(())
    }

    pub fn resume(&mut self) -> Result<(), String> {
        if self.state != RecordingState::Paused {
            return Err(format!("当前状态 {:?} 不允许继续", self.state));
        }
        if let Some(s) = &mut self.session {
            if let Some(p) = s.pause_started.take() {
                s.accumulated_pause += p.elapsed();
            }
        }
        self.state = RecordingState::Recording;
        Ok(())
    }

    pub fn stop(&mut self) -> Result<(), String> {
        if !matches!(self.state, RecordingState::Recording | RecordingState::Paused) {
            return Err(format!("当前状态 {:?} 不允许停止", self.state));
        }
        self.state = RecordingState::Stopping;
        // TODO: flush 编码 → 封装 MP4 → 写历史
        self.session = None;
        self.state = RecordingState::Idle;
        Ok(())
    }
}

/// 全局单例（Tauri state 管理）。
pub type SharedRecordingManager = Mutex<RecordingManager>;
