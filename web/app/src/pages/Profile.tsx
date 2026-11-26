import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import { auth } from '../api/client';
import { useMe, useSession } from '../auth/session';
import { useCrumbs } from '../layout/AppLayout';
import { date, dateTime } from '../lib/format';
import { getNotifyPrefs, setNotifyPrefs, type NotifyPrefs } from '../lib/notifyPrefs';
import css from '../styles/pages/profile.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { useLoad, useToast } from '../ui/ui';

const line = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round', strokeLinejoin: 'round', strokeWidth: 1.9 } as const;

const IconPerson = () => (
  <svg aria-hidden="true" height="18" viewBox="0 0 24 24" width="18" {...line}><circle cx="12" cy="8" r="4" /><path d="M4 21c1-4 4-6 8-6s7 2 8 6" /></svg>
);
const IconCopy = () => (
  <svg aria-hidden="true" height="16" viewBox="0 0 24 24" width="16" {...line}><rect height="12" rx="3" width="12" x="8" y="8" /><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2" /></svg>
);
const IconCalendar = () => (
  <svg aria-hidden="true" height="18" viewBox="0 0 24 24" width="18" {...line}><rect height="15" rx="3" width="16" x="4" y="5" /><path d="M4 10h16M9 3v4M15 3v4" /></svg>
);
const IconClock = () => (
  <svg aria-hidden="true" height="18" viewBox="0 0 24 24" width="18" {...line}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>
);
const IconLogout = () => (
  <svg aria-hidden="true" height="18" viewBox="0 0 24 24" width="18" {...line}><path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4M15 8l4 4-4 4M19 12H9" /></svg>
);

/** Срок действия текущего JWT (claim exp) — для строки «Текущий сеанс». */
function sessionExpiry(): string | null {
  try {
    const payload = auth.token?.split('.')[1];
    if (!payload) return null;
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '=')));
    return typeof json.exp === 'number' ? new Date(json.exp * 1000).toISOString() : null;
  } catch {
    return null;
  }
}

/** profile.html — данные преподавателя, смена пароля и выход. */
export default function Profile() {
  const grid = useFitProfile();
  useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: 'Профиль' }]);
  const me = useMe();
  const { logout } = useSession();
  const toast = useToast();
  // Дата добавления и кто добавил — из списка преподавателей; если не загрузился, строку просто не показываем
  const teacher = useLoad(() => api.teachers().then(list => list.find(t => t.id === me.id) ?? null).catch(() => null), [me.id]).data;
  const expires = sessionExpiry();

  async function copyLogin() {
    try {
      await navigator.clipboard.writeText(me.login);
      toast('Логин скопирован');
    } catch {
      toast('Не удалось скопировать', { error: true });
    }
  }

  return (
    <div className="d-grid gap-24 items-stretch d-grid--x1" ref={grid}>
      <PageStyle css={css} />
      <div className="profile-title">
        <h1>Профиль</h1>
        <div className="text-muted fs-17 mt-8">Ваши данные и настройки аккаунта</div>
      </div>
      <div className="card p-0 overflow-hidden d-flex flex-col profile-card">
        <div className="hero profile-banner rounded-0" />
        <div className="p-24-32-32 d-flex flex-col items-start flex-1">
          <h2 className="fs-28">{me.fullName}</h2>
          <span className="chip chip--brand mt-10 bg-transparent chip--x1">Преподаватель</span>
          <div className="w-full m-18-0-20 profile-data">
            <div className="row p-10-0 gap-12 row--x1">
              <span className="icon-btn w-36 h-36 bg-tint-brand-row border-0"><IconPerson /></span>
              <span className="text-muted profile-data__label">Логин</span>
              <b className="profile-data__login">{me.login}</b>
              <span className="spacer" />
              <button aria-label="Скопировать" className="icon-btn w-36 h-36" onClick={copyLogin} title="Скопировать" type="button"><IconCopy /></button>
            </div>
            {teacher && (
              <div className="row p-10-0 gap-12 row--x1">
                <span className="icon-btn w-36 h-36 bg-tint-brand-row border-0"><IconCalendar /></span>
                <span className="text-muted profile-data__label">Добавлен(а)</span>
                <b>{date(teacher.createdAt)}</b>
                {teacher.addedByName && <span className="text-muted fs-13">· {teacher.addedByName}</span>}
                <span className="spacer" />
              </div>
            )}
            <div className="row p-10-0 gap-12 row--x1">
              <span className="icon-btn w-36 h-36 bg-tint-brand-row border-0"><IconClock /></span>
              <span className="text-muted profile-data__label">Часовой пояс</span>
              <b>Екатеринбург, UTC+5</b>
              <span className="spacer" />
            </div>
          </div>
          <button className="btn-outline w-full mt-auto h-52" onClick={logout} type="button"><IconLogout />Выйти из аккаунта</button>
        </div>
      </div>
      <div className="d-flex flex-col gap-24 profile-side">
        <div className="card card-section gap-4">
          <h2 className="mb-10">Безопасность</h2>
          <div className="row p-9-0 row--x1">
            <div className="flex-1"><b>Пароль</b>
              <div className="text-muted fs-13 mt-2">После смены пароля остальные сеансы завершатся</div>
            </div>
            <Link className="btn-outline h-42" to="/profile/password">Сменить пароль</Link>
          </div>
          {expires && (
            <div className="row p-9-0 row--x1">
              <div className="flex-1"><b>Текущий сеанс</b>
                <div className="text-muted fs-13 mt-2">Действует до {dateTime(expires)}</div>
              </div>
            </div>
          )}
        </div>
        <NotifyCard />
      </div>
    </div>
  );
}

const notifyRows: { key: keyof NotifyPrefs; title: string; hint: string }[] = [
  { key: 'applications', title: 'Новые заявки студентов', hint: 'Когда студент просится на ваш курс' },
  { key: 'reviews', title: 'Работы на проверку', hint: 'Новые сдачи по заданиям ваших курсов' },
  { key: 'resubmissions', title: 'Пересдачи', hint: 'Когда студент присылает новую версию' },
  { key: 'overdue', title: 'Просроченные задания', hint: 'Сводка по студентам, не сдавшим вовремя' },
];

/** Что показывать на колокольчике — настройки хранятся в этом браузере. */
function NotifyCard() {
  const [prefs, setPrefs] = useState(getNotifyPrefs);
  const flip = (key: keyof NotifyPrefs) => {
    const next = { ...prefs, [key]: !prefs[key] };
    setPrefs(next);
    setNotifyPrefs(next);
  };
  return (
    <div className="card card-section gap-0">
      <div className="mb-8">
        <h2>Уведомления</h2>
        <div className="text-muted mt-4">Что показывать на колокольчике</div>
      </div>
      {notifyRows.map(r => (
        <div className="row p-9-0 row--x1" key={r.key}>
          <div className="flex-1"><b>{r.title}</b>
            <div className="text-muted fs-13 mt-2">{r.hint}</div>
          </div>
          <span aria-checked={prefs[r.key]} className={`switch${prefs[r.key] ? ' is-active' : ''}`} role="switch" tabIndex={0}
            onClick={() => flip(r.key)} onKeyDown={e => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); flip(r.key); } }} />
        </div>
      ))}
    </div>
  );
}

/** Как profile.js дизайна: на широком экране сетка профиля масштабируется, чтобы заполнить окно. */
function useFitProfile() {
  const grid = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const BASE_WIDTH = 1184, MAX_ZOOM = 5;
    const main = document.querySelector<HTMLElement>('.main');
    function fit() {
      const g = grid.current;
      if (!g || !main) return;
      g.style.zoom = '1';
      g.style.minHeight = '0';
      if (window.innerWidth < 900) {
        g.style.zoom = '';
        return;
      }
      const mainZoom = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--content-zoom')) || 1;
      const availW = main.clientWidth - 80;
      const availH = window.innerHeight / mainZoom - g.offsetTop - 40;
      const natural = g.offsetHeight;
      if (!(availW > 0) || !(availH > 0) || !(natural > 0)) return;
      const zoom = Math.max(1, Math.min(MAX_ZOOM, Math.min(availH / natural, availW / BASE_WIDTH)));
      g.style.zoom = String(zoom);
      g.style.minHeight = `${Math.max(0, availH / zoom)}px`;
    }
    fit();
    window.addEventListener('resize', fit);
    document.fonts?.ready.then(fit);
    const ro = main ? new ResizeObserver(fit) : null;
    if (main) ro!.observe(main);
    return () => { window.removeEventListener('resize', fit); ro?.disconnect(); };
  }, []);
  return grid;
}
