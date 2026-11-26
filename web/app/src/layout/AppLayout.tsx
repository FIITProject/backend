import { createContext, Fragment, useContext, useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { useMe } from '../auth/session';
import { api } from '../api/api';
import { loadOverview, subjectCoursePath } from '../lib/courses';
import { getNotifyPrefs, onNotifyPrefsChange } from '../lib/notifyPrefs';
import { initials } from '../lib/format';
import { IconArchive, IconBell, IconCollapse, IconCourses, IconCrumbSep, IconOverview } from '../ui/icons';

// ── Навигационная цепочка: страница сообщает её через useCrumbs ──────

export interface Crumb { label: string; to?: string }
const CrumbsContext = createContext<(crumbs: Crumb[]) => void>(() => {});

/** useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: 'Студенты' }]) — последний элемент без ссылки. */
export function useCrumbs(crumbs: Crumb[]) {
  const set = useContext(CrumbsContext);
  const key = JSON.stringify(crumbs);
  useEffect(() => set(crumbs), [key]); // eslint-disable-line react-hooks/exhaustive-deps
}

// ── Счётчики колокольчика: заявки и работы на проверке ───────────────

interface Counters { pending: number; toReview: number; refresh: () => void }
const CountersContext = createContext<Counters>({ pending: 0, toReview: 0, refresh: () => {} });
/** Обновить колокольчик после действия (подтвердили заявки, отправили проверку). */
export const useCounters = () => useContext(CountersContext);

const MENU_KEY = 'fiit-hometask:menu';

/** Каркас страниц приложения из дизайна: боковое меню, верхняя панель, контент. */
export function AppLayout() {
  const me = useMe();
  const location = useLocation();
  const [crumbs, setCrumbs] = useState<Crumb[]>([]);
  const [collapsed, setCollapsed] = useState(() => document.documentElement.classList.contains('menu-collapsed'));
  const [notifOpen, setNotifOpen] = useState(false);
  const [counters, setCounters] = useState({ pending: 0, toReview: 0 });
  const [extra, setExtra] = useState<{ to: string; text: string }[]>([]);
  const [prefs, setPrefs] = useState(getNotifyPrefs);
  const [tick, setTick] = useState(0);
  useEffect(() => onNotifyPrefsChange(() => setPrefs(getNotifyPrefs())), []);

  useEffect(() => {
    document.documentElement.classList.toggle('menu-collapsed', collapsed);
    try { localStorage.setItem(MENU_KEY, collapsed ? 'collapsed' : 'expanded'); } catch { /* без сохранения */ }
  }, [collapsed]);

  // Счётчики пересчитываются при переходах и по запросу страниц
  useEffect(() => {
    let alive = true;
    loadOverview().then(async o => {
      if (!alive) return;
      setCounters({ pending: o.pending, toReview: o.toReview });
      // Пересдачи и просрочки видны только в сводной таблице — грузим её, только если эти уведомления включены
      if (!prefs.resubmissions && !prefs.overdue) {
        setExtra([]);
        return;
      }
      const books = await Promise.all(o.courses.map(c => api.gradebook(c.subjectId, c.semester)));
      if (!alive) return;
      const notices: { to: string; text: string }[] = [];
      o.courses.forEach((c, i) => {
        const cells = books[i].rows.flatMap(r => r.cells);
        const resubmits = cells.filter(x => x.status === 'OnReview' && x.versions > 1).length;
        const overdue = cells.filter(x => x.status === 'Overdue').length;
        if (prefs.resubmissions && resubmits > 0)
          notices.push({ to: `${subjectCoursePath(c)}/summary`, text: `${c.subjectName}: пересдач на проверке — ${resubmits}` });
        if (prefs.overdue && overdue > 0)
          notices.push({ to: `${subjectCoursePath(c)}/summary`, text: `${c.subjectName}: просрочено сдач — ${overdue}` });
      });
      setExtra(notices);
    }).catch(() => {});
    return () => { alive = false; };
  }, [location.pathname, tick, prefs]);

  useEffect(() => setNotifOpen(false), [location.pathname]);
  useEffect(() => {
    if (!notifOpen) return;
    const close = () => setNotifOpen(false);
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && close();
    document.addEventListener('click', close);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('click', close); document.removeEventListener('keydown', onKey); };
  }, [notifOpen]);

  const notices = [
    prefs.applications && counters.pending > 0 && { to: '/courses?filter=pending', text: 'Появились новые заявки на вступление в курс!' },
    prefs.reviews && counters.toReview > 0 && { to: '/courses?filter=review', text: counters.toReview === 1 ? 'Новая работа готова к проверке!' : `Работ ждут проверки: ${counters.toReview}` },
    ...extra,
  ].filter(Boolean) as { to: string; text: string }[];
  const label = collapsed ? 'Развернуть меню' : 'Свернуть меню';
  const navClass = ({ isActive }: { isActive: boolean }) => `nav-link${isActive ? ' is-active' : ''}`;

  return (
    <CountersContext.Provider value={{ ...counters, refresh: () => setTick(t => t + 1) }}>
      <CrumbsContext.Provider value={setCrumbs}>
        <div className="app">
          <nav aria-label="Основное меню" className="sidebar">
            <div className="dots" />
            <div className="wordmark">
              <div className="wordmark__row">
                <button aria-expanded={!collapsed} aria-label={label} className="menu-toggle" onClick={() => setCollapsed(c => !c)} title={label} type="button">
                  <IconCollapse />
                </button>
                <Link aria-label="fiit.hometask — на главную" className="wordmark__link" to="/">
                  <span className="wordmark__text">fiit</span>
                  <span className="wordmark__accent">.hometask</span>
                </Link>
              </div>
            </div>
            <NavLink className={navClass} end to="/"><IconOverview />Обзор</NavLink>
            <NavLink className={navClass} to="/courses"><IconCourses />Курсы</NavLink>
            <NavLink className={navClass} to="/archive"><IconArchive />Архив</NavLink>
          </nav>
          <main className="main">
            <header className="topbar">
              <nav aria-label="Навигационная цепочка" className="crumbs text-muted">
                {crumbs.map((c, i) => (
                  <Fragment key={i}>
                    {i > 0 && <IconCrumbSep />}
                    {c.to && i < crumbs.length - 1
                      ? <Link className="crumbs__link" title={`Перейти: ${c.label}`} to={c.to}>{c.label}</Link>
                      : <span aria-current="page" className="crumbs__current">{c.label}</span>}
                  </Fragment>
                ))}
              </nav>
              <div className="account">
                <button aria-expanded={notifOpen} aria-label="Уведомления" className="notif-btn" type="button"
                  onClick={e => { e.stopPropagation(); setNotifOpen(o => !o); }}>
                  <IconBell />
                  {notices.length > 0 && <i className="badge-count">{notices.length}</i>}
                </button>
                {notifOpen && (
                  <div className="notifications" onClick={e => e.stopPropagation()}>
                    <h3 className="notifications__title">Уведомления</h3>
                    {notices.length === 0
                      ? <div className="text-muted fs-14">Новых заявок и работ на проверку нет</div>
                      : (
                        <ul className="notifications__list">
                          {notices.map(n => (
                            <li key={n.text}><Link className="notifications__item" to={n.to}><span className="notifications__dot" />{n.text}</Link></li>
                          ))}
                        </ul>
                      )}
                  </div>
                )}
                <span className="account__divider" />
                <Link aria-label="Профиль" className="profile-chip" to="/profile">
                  <span className="avatar">{initials(me.fullName)}</span>
                  <span className="profile-chip__text">
                    <b>{me.fullName.split(' ').slice(0, 2).join(' ')}</b>
                    <span>Преподаватель</span>
                  </span>
                </Link>
              </div>
            </header>
            <Outlet />
          </main>
        </div>
      </CrumbsContext.Provider>
    </CountersContext.Provider>
  );
}

