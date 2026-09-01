import { useEffect } from "react";
import { motion } from "motion/react";
import { bindBackendEvents, useRecordingStore, type RecordingMode } from "./store";
import { TitleBar } from "./components/TitleBar";
import { ModeCard } from "./components/ModeCard";
import { RecordButton } from "./components/RecordButton";

const ICONS: Record<RecordingMode, React.ReactNode> = {
  fullScreen: (
    <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
  ),
  window: (
    <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="4" width="18" height="16" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><circle cx="6.5" cy="6.5" r="0.7" fill="currentColor"/><circle cx="9.5" cy="6.5" r="0.7" fill="currentColor"/></svg>
  ),
  region: (
    <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="M6 2v14a2 2 0 0 0 2 2h14"/><path d="M2 6h14a2 2 0 0 1 2 2v14"/></svg>
  ),
};

const MODE_LABEL: Record<RecordingMode, string> = { fullScreen: "全屏", window: "窗口", region: "区域" };

export default function App() {
  const { mode, state, statusText, setMode, start, stop } = useRecordingStore();

  useEffect(() => {
    bindBackendEvents();
  }, []);

  const idle = state === "idle";
  const recording = state === "recording" || state === "paused";

  return (
    <div className="flex h-full flex-col">
      <TitleBar />

      {/* 环境光（柔光理念：光来自上方） */}
      <div className="pointer-events-none absolute inset-0 -z-10">
        <div className="absolute left-1/2 top-[-30%] h-[700px] w-[1200px] -translate-x-1/2 rounded-full"
          style={{ background: "radial-gradient(ellipse at center, rgba(94,140,230,0.055) 0%, transparent 70%)" }} />
      </div>

      <main className="flex flex-1 flex-col items-center px-12 pb-10">
        <motion.h1
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1, duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
          className="mt-8 text-[36px] font-semibold tracking-tight"
          style={{
            background: "linear-gradient(180deg, #FFF, #C8C9D0)",
            WebkitBackgroundClip: "text",
            backgroundClip: "text",
            WebkitTextFillColor: "transparent",
          }}
        >
          {idle ? "Ready to record?" : ""}
        </motion.h1>
        <motion.p
          initial={{ opacity: 0 }}
          animate={{ opacity: idle ? 1 : 0 }}
          className="mt-2.5 text-[13px] text-ink-text2"
        >
          选择一种方式，剩下的交给 Inkframe
        </motion.p>

        <div className="mt-11 flex gap-6">
          <ModeCard mode="fullScreen" title="全屏录制" desc={["完整记录整个显示器", "适合教程与演示"]}
            selected={mode === "fullScreen"} onSelect={() => setMode("fullScreen")} icon={ICONS.fullScreen} index={0} />
          <ModeCard mode="window" title="窗口录制" desc={["只锁定一个应用窗口", "其他内容不会入镜"]}
            selected={mode === "window"} onSelect={() => setMode("window")} icon={ICONS.window} index={1} />
          <ModeCard mode="region" title="区域录制" desc={["自由框选任意范围", "精确到像素"]}
            selected={mode === "region"} onSelect={() => setMode("region")} icon={ICONS.region} index={2} />
        </div>

        <RecordButton state={state} onClick={() => (idle ? start() : recording ? stop() : undefined)} />

        <p className="mt-4 text-[11.5px] tracking-wide text-ink-text3">
          {statusText || `${MODE_LABEL[mode]} · 1080P · 30 FPS · 系统声音 + 麦克风`}
        </p>
      </main>
    </div>
  );
}
