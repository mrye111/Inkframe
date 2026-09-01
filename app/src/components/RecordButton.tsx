import { motion } from "motion/react";
import type { RecordingState } from "../store";

/** 玻璃胶囊录制主按钮（§20-21：● Start Record ⇄ ■ Stop 平滑变化，不刷新页面）。 */
export function RecordButton({ state, onClick }: { state: RecordingState; onClick: () => void }) {
  const recording = state === "recording" || state === "paused";
  const busy = state === "countdown" || state === "stopping";
  return (
    <motion.button
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.56, duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
      whileHover={{ y: -1 }}
      whileTap={{ scale: 0.96 }}
      onClick={onClick}
      disabled={busy}
      className={`flex h-14 items-center gap-3 rounded-[22px] border px-9 text-[15px] font-medium
        outline-none transition-colors duration-300 disabled:opacity-60
        ${recording
          ? "border-ink-red/35 bg-gradient-to-b from-ink-red/[0.16] to-ink-red/[0.07]"
          : "border-transparent bg-gradient-to-b from-white/10 to-white/5"}
      `}
      style={{
        boxShadow: "inset 0 1px 0 rgba(255,255,255,0.14), 0 8px 24px rgba(0,0,0,0.30)",
      }}
    >
      <motion.span
        animate={{
          borderRadius: recording ? "3px" : "50%",
          opacity: recording ? [1, 0.45] : 1,
        }}
        transition={recording
          ? { opacity: { duration: 1.6, repeat: Infinity, ease: "easeInOut" }, borderRadius: { duration: 0.3 } }
          : { borderRadius: { duration: 0.3 } }}
        className="h-3 w-3 bg-ink-red"
        style={{ boxShadow: "0 0 12px rgba(255,69,58,0.55)" }}
      />
      {state === "idle" && "Start Record"}
      {state === "countdown" && "准备中…"}
      {state === "stopping" && "正在保存…"}
      {recording && "Stop"}
    </motion.button>
  );
}
