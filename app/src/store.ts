import { create } from "zustand";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export type RecordingMode = "fullScreen" | "window" | "region";
export type RecordingState = "idle" | "countdown" | "recording" | "paused" | "stopping";

interface RecordingStore {
  mode: RecordingMode;
  state: RecordingState;
  elapsedSecs: number;
  statusText: string;
  setMode: (m: RecordingMode) => void;
  start: () => Promise<void>;
  togglePause: () => Promise<void>;
  stop: () => Promise<void>;
}

export const useRecordingStore = create<RecordingStore>((set, get) => ({
  mode: "fullScreen",
  state: "idle",
  elapsedSecs: 0,
  statusText: "",

  setMode: (mode) => set({ mode }),

  start: async () => {
    try {
      await invoke("start_recording", { request: { mode: get().mode } });
    } catch (e) {
      set({ statusText: "启动失败：" + String(e) });
    }
  },

  togglePause: async () => {
    const s = get().state;
    try {
      if (s === "recording") await invoke("pause_recording");
      else if (s === "paused") await invoke("resume_recording");
    } catch (e) {
      set({ statusText: String(e) });
    }
  },

  stop: async () => {
    try {
      await invoke("stop_recording");
    } catch (e) {
      set({ statusText: String(e) });
    }
  },
}));

// Rust → 前端事件（状态机事实来源在后端）
export function bindBackendEvents() {
  listen<{ oldState: RecordingState; newState: RecordingState }>(
    "recording-state-changed",
    (e) => useRecordingStore.setState({ state: e.payload.newState })
  );
}
