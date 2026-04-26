import { useState, useRef, useCallback, useId } from 'react'
import { createPortal } from 'react-dom'
import { INFO_HINTS, type InfoHintKey } from './infoHints'

export function InfoHint({ label, hintKey }: { label: string; hintKey: InfoHintKey }) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const ref = useRef<HTMLButtonElement>(null);
  const tooltipId = useId();

  const handleEnter = useCallback(() => {
    if (ref.current) {
      const r = ref.current.getBoundingClientRect();
      setPos({ top: r.bottom + 4, left: r.left });
      setOpen(true);
    }
  }, []);

  const handleLeave = useCallback(() => setOpen(false), []);

  return (
    <span className="inline-flex items-center" onMouseEnter={handleEnter} onMouseLeave={handleLeave}>
      <button
        ref={ref}
        type="button"
        tabIndex={-1}
        aria-label={`Info: ${label}`}
        aria-describedby={open ? tooltipId : undefined}
        className="inline-flex h-4 w-4 items-center justify-center rounded-full border border-[var(--surface-border)] bg-[var(--surface-subtle)] text-[10px] font-semibold normal-case text-[var(--text-soft)]"
        onMouseDown={e => e.preventDefault()}
      >
        i
      </button>
      {open && createPortal(
        <div
          id={tooltipId}
          role="tooltip"
          className="fixed z-[9999] max-w-56 rounded-md border border-[var(--surface-border)] bg-[var(--surface)] px-2 py-1 text-[10px] normal-case tracking-normal text-[var(--text-soft)] shadow-[var(--surface-shadow)]"
          style={{ top: `${pos.top}px`, left: `${pos.left}px` }}
        >
          {INFO_HINTS[hintKey]}
        </div>,
        document.body,
      )}
    </span>
  );
}
