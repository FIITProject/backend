// Общее для страниц курса «Приглашения», «Преподаватели», «Настройки»: копирование, иконки дизайна, крошки.
import { useEffect, useRef, useState, type ReactNode, type SVGProps } from 'react';
import type { SemesterDto } from '../../api/types';
import { useCrumbs } from '../../layout/AppLayout';
import { semesterName } from '../../lib/format';
import { useToast } from '../../ui/ui';

type P = SVGProps<SVGSVGElement>;
const line = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

export const IconPlus = (p: P) => (
  <svg aria-hidden="true" height="20" width="20" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}><path d="M12 5v14M5 12h14" /></svg>
);
export const IconPencil = (p: P) => (
  <svg aria-hidden="true" height="16" width="16" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}><path d="M4 20l4-1 11-11-3-3L5 16z" /><path d="M14 6l3 3" /></svg>
);
export const IconTick = (p: P) => (
  <svg aria-hidden="true" height="16" width="16" viewBox="0 0 24 24" strokeWidth="2.2" {...line} {...p}><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
);
export const IconDash = (p: P) => (
  <svg aria-hidden="true" height="12" width="12" viewBox="0 0 24 24" strokeWidth="3.2" {...line} {...p}><path d="M6 12h12" /></svg>
);
export const IconTrash = (p: P) => (
  <svg aria-hidden="true" height="18" width="18" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}><path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13" /></svg>
);
export const IconCross = (p: P) => (
  <svg aria-hidden="true" height="18" width="18" viewBox="0 0 24 24" strokeWidth="2" {...line} {...p}><path d="M6 6l12 12M18 6L6 18" /></svg>
);
export const IconSearch = (p: P) => (
  <svg aria-hidden="true" height="18" width="18" viewBox="0 0 24 24" strokeWidth="2" {...line} {...p}><circle cx="11" cy="11" r="7" /><path d="M20 20l-3.5-3.5" /></svg>
);
export const IconKey = (p: P) => (
  <svg aria-hidden="true" height="18" width="18" viewBox="0 0 24 24" strokeWidth="1.9" {...line} {...p}>
    <circle cx="8" cy="15" r="4" /><path d="M11 12l9-9M16 7l3 3M14 9l2 2" />
  </svg>
);

/** Крошки страниц курса: Курсы › Предмет, семестр › Раздел. */
export function useCourseCrumbs(course: { subjectName: string; semester: SemesterDto }, path: string, ...rest: { label: string; to?: string }[]) {
  useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: `${course.subjectName}, ${semesterName(course.semester)}`, to: path }, ...rest]);
}

export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // http без secure context — старый способ
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { return document.execCommand('copy'); } catch { return false; } finally { ta.remove(); }
  }
}

/** Кнопка «Скопировать» из дизайна: на 1,8 с меняет подпись на «Скопировано». */
export function CopyButton({ text, className = 'btn-sm', children = 'Скопировать' }: { text: string; className?: string; children?: ReactNode }) {
  const toast = useToast();
  const [copied, setCopied] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  async function click() {
    if (!(await copyText(text))) {
      toast('Не удалось скопировать — выделите текст вручную', { error: true });
      return;
    }
    setCopied(true);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setCopied(false), 1800);
  }
  return <button className={className} onClick={click} type="button">{copied ? 'Скопировано' : children}</button>;
}

/** «https://t.me/bot?start=X» → «t.me/bot?start=X», как в дизайне. */
export const displayLink = (link: string) => link.replace(/^https?:\/\//, '');

export const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** Выбор подгрупп из дизайна new-invitation: «Выбрать все» (с промежуточным состоянием) и чипы-переключатели. */
export function GroupPicker({ courses, selected, onChange, disabled }: {
  courses: { id: number; subgroupName: string }[]; selected: number[]; onChange: (ids: number[]) => void; disabled?: boolean;
}) {
  const on = courses.filter(c => selected.includes(c.id)).length;
  const checked = on === 0 ? 'false' : on === courses.length ? 'true' : 'mixed';
  const toggle = (id: number) => !disabled && onChange(selected.includes(id) ? selected.filter(x => x !== id) : [...selected, id]);
  return (
    <>
      <button aria-checked={checked} className="select-all" disabled={disabled} onClick={() => onChange(checked === 'true' ? [] : courses.map(c => c.id))}
        role="checkbox" type="button">
        <span className="select-all__box">
          <svg aria-hidden="true" className="select-all__tick" fill="none" height="12" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="3.2" viewBox="0 0 24 24" width="12">
            <path d="M5 12.5l4.5 4.5L19 7.5" />
          </svg>
          <IconDash className="select-all__dash" />
        </span>
        <span className="select-all__label">Выбрать все</span>
      </button>
      <div className="row gap-10 flex-wrap">
        {courses.map(c => {
          const active = selected.includes(c.id);
          return (
            <span aria-pressed={active} className={`group-chip${active ? '' : ' is-off'}`} key={c.id} onClick={() => toggle(c.id)}
              onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(c.id); } }} aria-disabled={disabled} role="button" tabIndex={disabled ? -1 : 0}>
              {c.subgroupName}
            </span>
          );
        })}
      </div>
    </>
  );
}
