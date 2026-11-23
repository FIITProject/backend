import { useRef, useState } from 'react';

const CIRCUMFERENCE = 2 * Math.PI * 76;
const TICKS = Array.from({ length: 10 }, (_, i) => {
  const a = (i / 10) * 2 * Math.PI;
  return { x1: 95 + 90 * Math.sin(a), y1: 95 - 90 * Math.cos(a), x2: 95 + 94 * Math.sin(a), y2: 95 - 94 * Math.cos(a) };
});

export const fmtScore = (v: number) => String(Math.round(v * 100) / 100).replace('.', ',');

/**
 * Кольцо оценки из performance-check.html. value — итог (сумма или ручной), max — сумма максимумов подзадач
 * (null — максимум неизвестен: тянуть ползунок нельзя, только ввести число).
 * Перетаскивание и ввод числа задают итог вручную (totalOverride).
 */
export function ScoreDial({ value, max, readOnly, onChange }: {
  value: number | null; max: number | null; readOnly: boolean; onChange: (v: number | null) => void;
}) {
  const svg = useRef<SVGSVGElement>(null);
  const dragging = useRef(false);
  const [editing, setEditing] = useState<string | null>(null);

  const v = value ?? 0;
  const frac = max && max > 0 ? Math.min(1, Math.max(0, v / max)) : 0;
  const angle = frac * 2 * Math.PI;
  const text = value === null ? '—' : fmtScore(value);
  const canDrag = !readOnly && !!max && max > 0;

  function pointerToScore(e: React.PointerEvent) {
    const r = svg.current!.getBoundingClientRect();
    const dx = e.clientX - (r.left + r.width / 2);
    const dy = e.clientY - (r.top + r.height / 2);
    const turn = (Math.atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
    let s = Math.round(turn * max! * 2) / 2;
    if (v > max! * 0.7 && s < max! * 0.3) s = max!;
    if (v < max! * 0.3 && s > max! * 0.7) s = 0;
    return s;
  }

  function commit(raw: string) {
    setEditing(null);
    const t = raw.trim().replace(',', '.');
    if (t === '') return onChange(null);
    const n = parseFloat(t);
    if (!isNaN(n) && n >= 0) onChange(Math.round(n * 100) / 100);
  }

  return (
    <div className="score-dial">
      <svg aria-label={`Оценка ${text}${max ? ` из ${fmtScore(max)}` : ''}${canDrag ? ': потяните ползунок по кольцу' : ''}`}
        className="score-dial__svg" height="190" ref={svg} viewBox="0 0 190 190" width="190"
        style={canDrag ? undefined : { cursor: 'default' }}
        onPointerDown={e => {
          if (!canDrag || (e.target as Element).closest('[data-dial-edit]')) return;
          dragging.current = true;
          svg.current!.setPointerCapture(e.pointerId);
          onChange(pointerToScore(e));
        }}
        onPointerMove={e => { if (dragging.current) onChange(pointerToScore(e)); }}
        onPointerUp={() => { dragging.current = false; }}
        onPointerCancel={() => { dragging.current = false; }}>
        <defs>
          <linearGradient id="rg" x1="0" x2="1" y1="0" y2="1">
            <stop offset="0" stopColor="#8271E8" />
            <stop offset=".6" stopColor="#D894D4" />
            <stop offset="1" stopColor="#F6BC9E" />
          </linearGradient>
          <filter height="200%" id="kn" width="200%" x="-50%" y="-50%">
            <feDropShadow dx="0" dy="3" floodColor="#8271E8" floodOpacity=".35" stdDeviation="3" />
          </filter>
        </defs>
        {TICKS.map((t, i) => (
          <line key={i} stroke="#D2C6F5" strokeLinecap="round" strokeWidth="2" x1={t.x1.toFixed(1)} x2={t.x2.toFixed(1)} y1={t.y1.toFixed(1)} y2={t.y2.toFixed(1)} />
        ))}
        <circle cx="95" cy="95" fill="none" r="76" stroke="#EAE3FF" strokeWidth="14" />
        {frac > 0 && (
          <circle cx="95" cy="95" fill="none" r="76" stroke="url(#rg)" strokeDasharray={`${(frac * CIRCUMFERENCE).toFixed(1)} ${CIRCUMFERENCE.toFixed(1)}`}
            strokeLinecap="round" strokeWidth="14" transform="rotate(-90 95 95)" />
        )}
        {max !== null && max > 0 && (
          <circle cx={(95 + 76 * Math.sin(angle)).toFixed(2)} cy={(95 - 76 * Math.cos(angle)).toFixed(2)} fill="#fff" filter="url(#kn)" r="12" stroke="url(#rg)" strokeWidth="4" />
        )}
        <text fill="#241A4A" fontFamily="Unbounded,Montserrat,sans-serif" fontSize={text.length > 2 ? 36 : 46} fontWeight="800" textAnchor="middle" x="95" y="108"
          style={editing !== null ? { visibility: 'hidden' } : undefined}>{text}</text>
        <text fill="#6E6890" fontSize="14" textAnchor="middle" x="95" y="132">{max ? `из ${fmtScore(max)}` : 'итог'}</text>
        {!readOnly && (
          <rect className="score-dial__edit" data-dial-edit fill="transparent" height="56" width="86" x="52" y="68"
            onClick={() => setEditing(value === null ? '' : fmtScore(value))} />
        )}
      </svg>
      {editing !== null && (
        <input aria-label="Оценка" autoFocus className="score-dial__input" inputMode="decimal" maxLength={5} value={editing}
          onBlur={e => commit(e.target.value)}
          onChange={e => setEditing(e.target.value)}
          onFocus={e => e.target.select()}
          onKeyDown={e => {
            if (e.key === 'Enter') commit((e.target as HTMLInputElement).value);
            if (e.key === 'Escape') setEditing(null);
          }} />
      )}
    </div>
  );
}
