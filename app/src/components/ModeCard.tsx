import { motion } from "motion/react";
import type { RecordingMode } from "../store";

interface Props {
  mode: RecordingMode;
  title: string;
  desc: string[];
  selected: boolean;
  onSelect: () => void;
  icon: React.ReactNode;
  index: number;
}

/** 三大录制模式卡（规范 §27 / 原型 prototypes/v1.html#home 还原）。 */
export function ModeCard({ title, desc, selected, onSelect, icon, index }: Props) {
  return (
    <motion.button
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.3 + index * 0.08, duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
      whileHover={{ y: -4 }}
      whileTap={{ scale: 0.985, y: -1 }}
      onClick={onSelect}
      role="radio"
      aria-checked={selected}
      className={`relative h-[222px] w-[230px] rounded-3xl border p-6 text-left outline-none transition-colors duration-300
        ${selected
          ? "border-ink-blue/55 bg-gradient-to-b from-ink-blue/[0.14] to-ink-blue/5 shadow-[0_16px_40px_rgba(94,140,230,0.14),0_0_0_1px_rgba(94,140,230,0.25)]"
          : "border-transparent bg-gradient-to-b from-white/[0.075] to-white/[0.03]"}
      `}
      style={{
        boxShadow: selected
          ? undefined
          : "inset 0 1px 0 rgba(255,255,255,0.10), inset 0 -1px 0 rgba(0,0,0,0.22), 0 12px 32px rgba(0,0,0,0.28)",
      }}
    >
      {/* 选中角标 */}
      <motion.span
        animate={{ opacity: selected ? 1 : 0, scale: selected ? 1 : 0.5 }}
        transition={{ duration: 0.3, ease: [0.34, 1.56, 0.64, 1] }}
        className="absolute right-4 top-4 grid h-[21px] w-[21px] place-items-center rounded-full bg-ink-blue"
      >
        <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="3.4" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
      </motion.span>

      <span className={`grid h-[50px] w-[50px] place-items-center rounded-2xl
        bg-gradient-to-b from-white/10 to-white/5 shadow-[inset_0_1px_0_rgba(255,255,255,0.12)]
        transition-colors duration-300 ${selected ? "text-ink-blue" : "text-ink-text1"}`}>
        {icon}
      </span>
      <span className="mt-auto">
        <span className="block text-[16.5px] font-semibold">{title}</span>
        <span className="mt-1.5 block text-xs leading-relaxed text-ink-text2">
          {desc[0]}<br />{desc[1]}
        </span>
      </span>
    </motion.button>
  );
}
