// Общее для страниц заданий (course-task, new-task, task-page): коды заданий, баллы, фильтр-меню, иконки.
import { useEffect, useRef, useState, type ReactNode, type SVGProps } from 'react';
import type { SubtaskDto } from '../../api/types';
import { plural, score } from '../../lib/format';

// ── Коды заданий ─────────────────────────────────────────────────────
// В API у ДЗ нет «кода» (ДЗ-1, ДР-2, Г-3): если название так начинается, код показываем отдельно.

export interface TaskTitle {
  /** «ДЗ-1» или null, если название не начинается с кода. */
  code: string | null;
  /** «ДЗ» — для фильтра и цвета. */
  prefix: string | null;
  /** Название без кода (или всё название). */
  name: string;
}

const CODE_RE = /^([A-ZА-ЯЁ]{1,4})-?(\d+)(?:[\s.:—–-]+(.*))?$/u;

export function parseTitle(title: string): TaskTitle {
  const m = CODE_RE.exec(title.trim());
  if (!m) return { code: null, prefix: null, name: title };
  return { code: `${m[1]}-${m[2]}`, prefix: m[1], name: m[3]?.trim() || title };
}

/** Цвет метки кода: по порядку префикса среди всех префиксов курса, как в дизайне (task-tag--1..3). */
export function tagClass(prefix: string | null, prefixes: string[]): string {
  const i = prefix ? Math.max(0, prefixes.indexOf(prefix)) : 0;
  return `task-tag task-tag--${(i % 3) + 1}`;
}

/** Префиксы кодов в порядке первого появления. */
export function codePrefixes(titles: string[]): string[] {
  const result: string[] = [];
  for (const t of titles) {
    const p = parseTitle(t).prefix;
    if (p && !result.includes(p)) result.push(p);
  }
  return result;
}

// ── Баллы ─────────────────────────────────────────────────────────────
// Отдельного макс. балла у ДЗ в API нет — это сумма maxScore подзадач.

export function maxPoints(subtasks: SubtaskDto[]): number | null {
  const scores = subtasks.map(s => s.maxScore).filter((x): x is number => x !== null);
  return scores.length ? scores.reduce((a, b) => a + b, 0) : null;
}

/** «3 балла», «2,5 балла» */
export function pointsText(n: number): string {
  const word = Number.isInteger(n) ? plural(n, 'балл', 'балла', 'баллов') : 'балла';
  return `${score(n).replace('.', ',')} ${word}`;
}

// ── Фильтр-меню (filter-menu.js дизайна) ─────────────────────────────

/** Закрывает выпадающее меню по клику снаружи и Escape. */
export function useDismiss<T extends HTMLElement>(open: boolean, close: () => void) {
  const ref = useRef<T>(null);
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) close(); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('click', onClick); document.removeEventListener('keydown', onKey); };
  }, [open, close]);
  return ref;
}

export interface FilterOption { value: string; label: string }

/**
 * Меню фильтра с галочками. В режиме «values» все галочки = без ограничения;
 * в режиме «flag» отмеченные флаги — обязательны.
 */
export function FilterMenu({ trigger, options, checked, onChange, end, list }: {
  trigger: (props: { open: boolean; toggle: (e: React.MouseEvent) => void }) => ReactNode;
  options: FilterOption[];
  checked: string[];
  onChange: (checked: string[]) => void;
  end?: boolean;
  list?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const ref = useDismiss<HTMLSpanElement>(open, () => setOpen(false));
  const toggle = (value: string) =>
    onChange(checked.includes(value) ? checked.filter(v => v !== value) : [...checked, value]);
  return (
    <span className="dropdown-anchor" ref={ref}>
      {trigger({ open, toggle: e => { e.stopPropagation(); setOpen(o => !o); } })}
      {open && (
        <div className={`dropdown${list ? ' dropdown__list' : ''}${end ? ' dropdown--end' : ''}`}>
          {options.map(o => (
            <label className="dropdown__option" key={o.value}>
              <input checked={checked.includes(o.value)} onChange={() => toggle(o.value)} type="checkbox" />{o.label}
            </label>
          ))}
        </div>
      )}
    </span>
  );
}

/** Подходит ли значение под фильтр «values»: все отмечены (или вариантов нет) — без ограничения. */
export const passes = (checked: string[], all: string[], value: string) =>
  checked.length === all.length || checked.includes(value);

// ── Иконки дизайна ───────────────────────────────────────────────────

type P = SVGProps<SVGSVGElement>;
const line = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

export const IconGo = (p: P) => (
  <svg aria-hidden="true" height="14" width="14" viewBox="0 0 24 24" strokeWidth="2.2" {...line} {...p}>
    <path d="M7 17L17 7M8 7h9v9" />
  </svg>
);

export const IconEdit = (p: P) => (
  <svg aria-hidden="true" height="14" width="14" viewBox="0 0 24 24" strokeWidth="2.2" {...line} {...p}>
    <path d="M4 20l4-1 11-11-3-3L5 16z" />
    <path d="M14 6l3 3" />
  </svg>
);

export const IconChevronUp = (p: P) => (
  <svg aria-hidden="true" height="16" width="16" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <path d="M6 15l6-6 6 6" />
  </svg>
);

export const IconFilter = (p: P) => (
  <svg aria-hidden="true" height="15" width="15" viewBox="0 0 24 24" strokeWidth="2" {...line} {...p}>
    <path d="M4 5h16l-6 8v6l-4-2v-4z" />
  </svg>
);

export const IconRemove = (p: P) => (
  <svg aria-hidden="true" height="16" width="16" viewBox="0 0 24 24" strokeWidth="2.2" {...line} {...p}>
    <path d="M6 6l12 12M18 6L6 18" />
  </svg>
);

export const IconTick = (p: P) => (
  <svg aria-hidden="true" height="16" width="16" viewBox="0 0 24 24" strokeWidth="2.4" {...line} {...p}>
    <path d="M5 12.5l4.5 4.5L19 7.5" />
  </svg>
);

export const IconDash = (p: P) => (
  <svg aria-hidden="true" height="12" width="12" viewBox="0 0 24 24" strokeWidth="3.2" {...line} {...p}>
    <path d="M6 12h12" />
  </svg>
);
