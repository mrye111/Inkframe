import { getCurrentWindow } from "@tauri-apps/api/window";

/** 自定义标题栏：无边框窗体（decorations:false）的拖拽区 + 最小化/关闭。 */
export function TitleBar() {
  const win = getCurrentWindow();
  return (
    <div
      data-tauri-drag-region
      className="flex h-10 items-center px-4 select-none"
    >
      <span data-tauri-drag-region className="text-xs text-ink-text2 tracking-wide">
        Inkframe
      </span>
      <div className="ml-auto flex items-center gap-1">
        <button
          onClick={() => win.minimize()}
          className="grid h-8 w-10 place-items-center rounded-lg text-ink-text2 transition-colors duration-200 hover:bg-white/10 hover:text-ink-text1"
          aria-label="最小化"
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="5" y1="12" x2="19" y2="12"/></svg>
        </button>
        <button
          onClick={() => win.close()}
          className="grid h-8 w-10 place-items-center rounded-lg text-ink-text2 transition-colors duration-200 hover:bg-ink-red hover:text-white"
          aria-label="关闭"
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>
        </button>
      </div>
    </div>
  );
}
