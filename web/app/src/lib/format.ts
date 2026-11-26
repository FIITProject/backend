import type { CellStatus, EnrollmentStatus, InviteStatus, SemesterDto, SemesterTerm } from '../api/types';

// Время показываем в часовом поясе Екатеринбурга (UTC+5, без летнего времени) — Н-5 ТЗ.
const OFFSET_MS = 5 * 60 * 60 * 1000;
const pad = (n: number) => String(n).padStart(2, '0');

function local(iso: string) {
  return new Date(new Date(iso).getTime() + OFFSET_MS);
}

/** «05.10.2026 14:30» */
export function dateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = local(iso);
  return `${pad(d.getUTCDate())}.${pad(d.getUTCMonth() + 1)}.${d.getUTCFullYear()} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

/** «05.10 14:30» */
export function shortDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = local(iso);
  return `${pad(d.getUTCDate())}.${pad(d.getUTCMonth() + 1)} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

/** «05.10.2026» */
export function date(iso: string | null | undefined): string {
  return iso ? dateTime(iso).slice(0, 10) : '—';
}

/** Значение для <input type="datetime-local"> во времени Екатеринбурга. */
export function toInput(iso: string): string {
  const d = local(iso);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

/** Из <input type="datetime-local"> (время Екатеринбурга) в ISO UTC. */
export function fromInput(value: string): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value);
  if (!m) return null;
  const utc = Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5]) - OFFSET_MS;
  return new Date(utc).toISOString();
}

/** Дедлайн по умолчанию — через неделю в 23:59 по Екатеринбургу. */
export function defaultDeadline(): string {
  const d = local(new Date().toISOString());
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 7, 23, 59) - OFFSET_MS).toISOString();
}

export function score(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : String(Math.round(value * 100) / 100);
}

/** «1 балл», «3 балла», «5 баллов» */
export function plural(n: number, one: string, few: string, many: string): string {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 10 || m100 >= 20)) return few;
  return many;
}

export const points = (n: number) => `${score(n)} ${plural(Math.floor(n) === n ? n : 2, 'балл', 'балла', 'баллов')}`;

export const termName = (t: SemesterTerm) => (t === 'Autumn' ? 'осень' : 'весна');
/** «Осень 2026» — как в дизайне. */
export const semesterName = (s: SemesterDto) => `${s.term === 'Autumn' ? 'Осень' : 'Весна'} ${s.year}`;
/** Учебный год семестра: осень 2026 и весна 2027 → «2026/27» (плашка в шапке курса). */
export const academicYear = (s: SemesterDto) => {
  const start = s.term === 'Autumn' ? s.year : s.year - 1;
  return `${start}/${String(start + 1).slice(-2)}`;
};
export const semesterKey = (s: SemesterDto) => `${s.year}-${s.term}`;
export const sameSemester = (a: SemesterDto, b: SemesterDto) => a.year === b.year && a.term === b.term;

export function parseSemesterKey(key: string | undefined): SemesterDto | null {
  const m = /^(\d{4})-(Autumn|Spring)$/.exec(key ?? '');
  return m ? { year: +m[1], term: m[2] as SemesterTerm } : null;
}

/** «Иванов Иван Иванович» → «Иванов И. И.» */
export function shortName(full: string): string {
  const [last, ...rest] = full.split(' ').filter(Boolean);
  return rest.length ? `${last} ${rest.map(p => `${p[0]}.`).join(' ')}` : full;
}

/** «Иванов Иван» → «ИИ» — для аватара. */
export function initials(full: string): string {
  return full.split(' ').filter(Boolean).slice(0, 2).map(p => p[0].toUpperCase()).join('');
}

export const cellStatusName: Record<CellStatus, string> = {
  NotAssigned: 'не назначено',
  NotSubmitted: 'не сдано',
  Overdue: 'просрочено',
  OnReview: 'на проверке',
  Reviewed: 'проверено',
  ReviewedWithUnsentChanges: 'есть неотправленные правки',
};

export const enrollmentStatusName: Record<EnrollmentStatus, string> = {
  Pending: 'ожидает подтверждения',
  Approved: 'подтверждён',
  Rejected: 'отклонена',
  Removed: 'удалён',
};

export const inviteStatusName: Record<InviteStatus, string> = {
  Active: 'активно',
  Expired: 'истекло',
  Closed: 'закрыто',
};
