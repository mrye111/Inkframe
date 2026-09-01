//! JSON 配置（§59/§60）：%AppData%/Inkframe/config.json，版本迁移钩子。
//! C# 参考：ScreenRecorder.Infrastructure/Configuration/ConfigService.cs。

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppConfig {
    pub version: u32,
    pub output_directory: String,
    pub video: VideoConfig,
    pub audio: AudioConfig,
    pub cursor: CursorConfig,
    pub hotkeys: HotkeyConfig,
    pub advanced: AdvancedConfig,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VideoConfig {
    pub fps: u32,
    pub quality: String,
    pub encoder: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AudioConfig {
    pub system_audio: bool,
    pub microphone: bool,
    pub system_volume: f64,
    pub microphone_volume: f64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CursorConfig {
    pub record_cursor: bool,
    pub highlight_cursor: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HotkeyConfig {
    pub toggle_recording: String,
    pub toggle_pause: String,
    pub toggle_microphone: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdvancedConfig {
    pub crash_protection: bool,
}

impl Default for AppConfig {
    fn default() -> Self {
        let output = directories::UserDirs::new()
            .and_then(|u| u.video_dir().map(|v| v.join("Inkframe")))
            .unwrap_or_else(|| PathBuf::from("Inkframe"));
        Self {
            version: Self::CURRENT_VERSION,
            output_directory: output.to_string_lossy().into_owned(),
            video: VideoConfig { fps: 30, quality: "标准".into(), encoder: "auto".into() },
            audio: AudioConfig { system_audio: true, microphone: false, system_volume: 1.0, microphone_volume: 1.0 },
            cursor: CursorConfig { record_cursor: true, highlight_cursor: false },
            hotkeys: HotkeyConfig {
                toggle_recording: "Ctrl+Alt+R".into(),
                toggle_pause: "Ctrl+Alt+P".into(),
                toggle_microphone: "Ctrl+Alt+M".into(),
            },
            advanced: AdvancedConfig { crash_protection: true },
        }
    }
}

impl AppConfig {
    pub const CURRENT_VERSION: u32 = 1;

    fn path() -> PathBuf {
        let dir = directories::ProjectDirs::from("com", "inkframe", "Inkframe")
            .map(|p| p.config_dir().to_path_buf())
            .unwrap_or_else(|| PathBuf::from("."));
        std::fs::create_dir_all(&dir).ok();
        dir.join("config.json")
    }

    pub fn load() -> Self {
        let path = Self::path();
        match std::fs::read_to_string(&path) {
            Ok(text) => serde_json::from_str(&text).unwrap_or_else(|_| {
                tracing::warn!("config.json 解析失败，回退默认配置");
                Self::default()
            }),
            Err(_) => {
                let cfg = Self::default();
                cfg.save();
                cfg
            }
        }
    }

    pub fn save(&self) {
        if let Ok(text) = serde_json::to_string_pretty(self) {
            let _ = std::fs::write(Self::path(), text);
        }
    }
}
