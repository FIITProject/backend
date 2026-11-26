// Общие куски страниц «Студенты», «Заявки» и «Сводная»: выпадающий фильтр-чекбоксы (filter-menu.js дизайна),
// крошки курса и иконки, которых нет в ui/icons.tsx.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useCourse } from '../course/CourseLayout';
import { useCrumbs } from '../layout/AppLayout';
import { semesterName } from '../lib/format';

export interface FilterOption { value: string; label: string }

/** Открыта ли выпадашка; закрывается кликом мимо и Escape. */
export function useDropdown() {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('click', onClick); document.removeEventListener('keydown', onKey); };
  }, [open]);
  return { open, setOpen, ref };
}

/**
 * Фильтр-выпадашка с чекбоксами: все отмечены — ограничения нет (selected = null).
 * variant 'th' — кнопка-воронка в шапке таблицы, 'pill' — кнопка-пилюля с подписью.
 */
export function CheckFilter({ label, options, selected, onChange, variant = 'th', pillText }: {
  label: string;
  options: FilterOption[];
  selected: Set<string> | null;
  onChange: (next: Set<string> | null) => void;
  variant?: 'th' | 'pill';
  pillText?: ReactNode;
}) {
  const { open, setOpen, ref } = useDropdown();

  const isOn = (v: string) => !selected || selected.has(v);
  const filtered = selected !== null;
  const toggle = (v: string) => {
    const next = new Set(selected ?? options.map(o => o.value));
    if (next.has(v)) next.delete(v); else next.add(v);
    onChange(next.size === options.length ? null : next);
  };

  return (
    <span className="dropdown-anchor" ref={ref}>
      {variant === 'th'
        ? (
          <button aria-expanded={open} aria-haspopup="true" aria-label={label} className={`filter-btn${filtered ? ' is-filtered' : ''}`}
            onClick={() => setOpen(o => !o)} title={label} type="button">
            <IconFunnel />
          </button>
        )
        : (
          <button aria-expanded={open} aria-haspopup="true" aria-label={label}
            className={`card p-11-18 rounded-999 fw-800 nowrap filter-pill${filtered ? ' is-filtered' : ''}`}
            onClick={() => setOpen(o => !o)} type="button">
            {pillText} ▾
          </button>
        )}
      {open && (
        <div className={`dropdown dropdown__list${variant === 'pill' ? ' dropdown--end' : ''}`}>
          {options.length === 0 && <div className="dropdown__option text-muted">Нет вариантов</div>}
          {options.map(o => (
            <label className="dropdown__option" key={o.value}>
              <input checked={isOn(o.value)} onChange={() => toggle(o.value)} type="checkbox" />{o.label}
            </label>
          ))}
        </div>
      )}
    </span>
  );
}

/** Крошки «Курсы › Предмет, семестр › Раздел». */
export function useCourseCrumbs(label: string, parent?: { label: string; to: string }) {
  const { course, path } = useCourse();
  useCrumbs([
    { label: course.isArchived ? 'Архив' : 'Курсы', to: course.isArchived ? '/archive' : '/courses' },
    { label: `${course.subjectName}, ${semesterName(course.semester)}`, to: path },
    ...(parent ? [parent] : []),
    { label },
  ]);
}

/** Поиск без учёта регистра и «ё». */
export const norm = (s: string) => s.toLowerCase().replace(/ё/g, 'е');

export const IconFunnel = () => (
  <svg aria-hidden="true" fill="none" height="15" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" viewBox="0 0 24 24" width="15">
    <path d="M4 5h16l-6 8v6l-4-2v-4z" />
  </svg>
);

export const IconSearch = () => (
  <svg aria-hidden="true" fill="none" height="20" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.9" viewBox="0 0 24 24" width="20">
    <circle cx="11" cy="11" r="6" />
    <path d="M20 20l-4-4" />
  </svg>
);
