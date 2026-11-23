import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { api } from '../api/api';
import type { CourseDto, SemesterDto, SemesterTerm, SubjectDto, TeacherDto } from '../api/types';
import { useMe } from '../auth/session';
import { useCounters, useCrumbs } from '../layout/AppLayout';
import { subjectCoursePath } from '../lib/courses';
import { initials, plural } from '../lib/format';
import css from '../styles/pages/new-course.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { errorText, Modal, useConfirm, useLoad, useToast } from '../ui/ui';

const IconChevron = () => (
  <svg aria-hidden="true" fill="none" height="16" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" viewBox="0 0 24 24" width="16">
    <path d="M6 9l6 6 6-6" />
  </svg>
);

const IconDelete = () => (
  <svg aria-hidden="true" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.9" viewBox="0 0 24 24" width="18">
    <path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13" />
  </svg>
);

/**
 * Та же нормализация, что на сервере (GroupName.Parse): «фт 201 1» → «ФТ-201-1».
 * null — не похоже на группу; сервер всё равно проверит ещё раз.
 */
function normalizeGroup(input: string): string | null {
  const s = input.trim().toUpperCase().replace(/[–—‐]/g, '-');
  const m = /^([A-ZА-ЯЁ]+)[\s-]*(\d+)(?:[\s-]+(\d+))?$/.exec(s);
  if (!m || (m[3] !== undefined && +m[3] <= 0)) return null;
  return m[3] !== undefined ? `${m[1]}-${m[2]}-${+m[3]}` : `${m[1]}-${m[2]}`;
}

const seasons: { term: SemesterTerm; label: string }[] = [{ term: 'Autumn', label: 'Осень' }, { term: 'Spring', label: 'Весна' }];

interface Failure { group: string; error: string }

/**
 * new-course.html. Курс в интерфейсе — предмет в семестре; в API на каждую группу (подгруппу) создаётся свой курс.
 * Преподавателям выдаётся доступ к созданным курсам, приглашение — одно на все созданные группы.
 */
export default function NewCourse() {
  useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: 'Новый курс' }]);
  const me = useMe();
  const navigate = useNavigate();
  const toast = useToast();
  const counters = useCounters();
  const { confirm, dialog } = useConfirm();

  const [title, setTitle] = useState('');
  const [suggestOpen, setSuggestOpen] = useState(false);
  const [suggestions, setSuggestions] = useState<SubjectDto[]>([]);
  const [term, setTerm] = useState<SemesterTerm | null>(null);
  const [year, setYear] = useState<number | null>(null);
  const [yearOpen, setYearOpen] = useState(false);
  const [groupInput, setGroupInput] = useState('');
  const [groups, setGroups] = useState<{ name: string; on: boolean }[]>([]);
  const [teachers, setTeachers] = useState<TeacherDto[]>([]);
  const [teacherModal, setTeacherModal] = useState(false);
  const [teacherPick, setTeacherPick] = useState<number | ''>('');
  const [invite, setInvite] = useState(true);
  const [missing, setMissing] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [failures, setFailures] = useState<{ list: Failure[]; path: string | null } | null>(null);
  const yearRef = useRef<HTMLDivElement>(null);

  // Текущий семестр — по умолчанию; группы из моих прошлых курсов — подсказки (выключены)
  const init = useLoad(async () => {
    const [current, active, archived, colleagues] = await Promise.all([
      api.currentSemester(),
      api.courses(),
      api.courses({ archived: true }),
      api.teachers().catch(() => [] as TeacherDto[]),
    ]);
    return { current, known: [...new Set([...active, ...archived].map(c => c.subgroupName))].sort((a, b) => a.localeCompare(b, 'ru')), colleagues };
  }, []);

  useEffect(() => {
    if (!init.data) return;
    setTerm(t => t ?? init.data!.current.term);
    setYear(y => y ?? init.data!.current.year);
    setGroups(g => g.length ? g : init.data!.known.map(name => ({ name, on: false })));
  }, [init.data]);

  // Подсказки предметов из общего справочника
  useEffect(() => {
    const q = title.trim();
    if (!q) { setSuggestions([]); return; }
    let alive = true;
    const t = setTimeout(() => api.subjects(q).then(s => alive && setSuggestions(s.slice(0, 8))).catch(() => {}), 250);
    return () => { alive = false; clearTimeout(t); };
  }, [title]);

  useEffect(() => {
    if (!yearOpen) return;
    const close = (e: MouseEvent) => { if (!yearRef.current?.contains(e.target as Node)) setYearOpen(false); };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setYearOpen(false);
    document.addEventListener('click', close);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('click', close); document.removeEventListener('keydown', onKey); };
  }, [yearOpen]);

  const years = useMemo(() => {
    const base = init.data?.current.year ?? new Date().getFullYear();
    const list = [base - 1, base, base + 1, base + 2, base + 3];
    return year !== null && !list.includes(year) ? [...list, year].sort((a, b) => a - b) : list;
  }, [init.data, year]);

  const selected = groups.filter(g => g.on).map(g => g.name);
  const semester: SemesterDto | null = term && year ? { term, year } : null;
  const semesterLabel = semester ? `${term === 'Autumn' ? 'Осень' : 'Весна'} ${year}` : null;
  const colleagues = (init.data?.colleagues ?? []).filter(t => t.id !== me.id && !t.isDisabled && !teachers.some(x => x.id === t.id));

  function addGroup() {
    if (!groupInput.trim()) return;
    const name = normalizeGroup(groupInput);
    if (!name) {
      toast('Не удалось разобрать группу. Пример: ФТ-201 или ФТ-201-1', { error: true });
      return;
    }
    setGroups(g => g.some(x => x.name === name) ? g.map(x => x.name === name ? { ...x, on: true } : x) : [...g, { name, on: true }]);
    setGroupInput('');
  }

  const toggleGroup = (name: string) => setGroups(g => g.map(x => x.name === name ? { ...x, on: !x.on } : x));

  async function removeTeacher(t: TeacherDto) {
    const ok = await confirm({
      title: 'Удаление преподавателя',
      text: 'Вы уверены, что хотите продолжить создание курса без этого преподавателя? Список преподавателей можно изменить после создания курса.',
      danger: true,
    });
    if (ok) setTeachers(list => list.filter(x => x.id !== t.id));
  }

  function addTeacher() {
    const t = colleagues.find(x => x.id === teacherPick);
    if (t) setTeachers(list => [...list, t]);
    setTeacherPick('');
    setTeacherModal(false);
  }

  async function create() {
    const miss = [!title.trim() && 'название', !semester && 'семестр', selected.length === 0 && 'группы'].filter(Boolean);
    if (miss.length) {
      setMissing(miss.join(', '));
      return;
    }
    setBusy(true);
    try {
      // Сервер вернёт существующий предмет при совпадении названия без учёта регистра
      const subject = await api.createSubject(title.trim());
      const created: CourseDto[] = [];
      const failed: Failure[] = [];
      for (const group of selected) {
        try {
          created.push(await api.createCourse({ subjectId: subject.id, group, subgroup: null, semester }));
        } catch (e) {
          failed.push({ group, error: errorText(e) });
        }
      }
      if (created.length === 0) {
        setFailures({ list: failed, path: null });
        return;
      }
      for (const t of teachers)
        for (const c of created) {
          try { await api.grantAccess(c.id, t.id); } catch (e) { failed.push({ group: `${t.fullName} → ${c.subgroupName}`, error: errorText(e) }); }
        }
      let inviteCreated = false;
      if (invite) {
        try {
          await api.createInvite(subject.id, created.map(c => c.id), null);
          inviteCreated = true;
        } catch (e) {
          failed.push({ group: 'Приглашение', error: errorText(e) });
        }
      }
      counters.refresh();
      const path = subjectCoursePath(created[0]);
      const target = inviteCreated ? `${path}/invites` : path;
      if (failed.length) {
        setFailures({ list: failed, path: target });
        return;
      }
      toast(inviteCreated ? 'Курс создан, ссылка-приглашение готова' : 'Курс создан');
      navigate(target);
    } catch (e) {
      toast(errorText(e), { error: true });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="nc-wrap">
      <PageStyle css={css} />
      <div>
        <h1>Новый курс</h1>
      </div>
      {init.error && <div className="alert-error" role="alert"><div>{init.error}</div></div>}
      <div className="row items-start gap-28">
        <div className="flex-1 min-w-0 d-flex flex-col gap-20">
          <div className="card card-section">
            <div className="row gap-14">
              <label className="flex-1" style={{ position: 'relative' }}>Название
                <input autoComplete="off" className="input rounded-999" maxLength={200} onBlur={() => setTimeout(() => setSuggestOpen(false), 150)}
                  onChange={e => { setTitle(e.target.value); setSuggestOpen(true); }} onFocus={() => setSuggestOpen(true)}
                  onKeyDown={e => e.key === 'Escape' && setSuggestOpen(false)} placeholder="Теория вероятностей" value={title} />
                {suggestOpen && suggestions.length > 0 && !(suggestions.length === 1 && suggestions[0].name === title.trim()) && (
                  <div className="dropdown semester-year__panel" role="listbox">
                    {suggestions.map(s => (
                      <button aria-selected={s.name === title.trim()} className={`semester-year__option${s.name === title.trim() ? ' is-active' : ''}`} key={s.id}
                        onMouseDown={e => e.preventDefault()} onClick={() => { setTitle(s.name); setSuggestOpen(false); }} role="option" type="button">
                        {s.name}
                      </button>
                    ))}
                  </div>
                )}
              </label>
              <div className="semester-field">
                <span id="course-semester-label">Семестр</span>
                <div className="row gap-8">
                  <div aria-labelledby="course-semester-label" className="segmented semester-field__season" role="tablist">
                    {seasons.map(s => (
                      <span aria-selected={term === s.term} className={term === s.term ? 'is-active' : undefined} key={s.term}
                        onClick={() => setTerm(s.term)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), setTerm(s.term))}
                        role="tab" tabIndex={0}>
                        {s.label}
                      </span>
                    ))}
                  </div>
                  <div className="dropdown-anchor semester-year" ref={yearRef}>
                    <button aria-expanded={yearOpen} aria-haspopup="listbox" aria-label="Год" className="semester-year__btn" onClick={() => setYearOpen(o => !o)} type="button">
                      <span>{year ?? 'Год'}</span><IconChevron />
                    </button>
                    {yearOpen && (
                      <div className="dropdown semester-year__panel" role="listbox">
                        {years.map(y => (
                          <button aria-selected={y === year} className={`semester-year__option${y === year ? ' is-active' : ''}`} key={y}
                            onClick={() => { setYear(y); setYearOpen(false); }} role="option" type="button">
                            {y}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div className="card card-section">
            <div className="row">
              <span className="step-number">1</span>
              <h2>Группы и подгруппы</h2>
            </div>
            <div className="combo-field combo-field--groups">
              <input aria-label="Добавить группу" className="combo-field__input" onChange={e => setGroupInput(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); addGroup(); } }} placeholder="Добавить группу" value={groupInput} />
              <span aria-hidden="true" className="combo-field__divider" />
              <button className={`combo-field__button${groupInput.trim() ? ' is-filled' : ''}`} onClick={addGroup} type="button">Добавить</button>
            </div>
            {groups.length > 0 && <div className="text-muted fs-13">Нажмите на группу, чтобы выбрать или снять её</div>}
            {groups.length > 0 && (
              <div aria-label="Группы и подгруппы" className="group-tiles" role="group">
                {groups.map(g => (
                  <span aria-pressed={g.on} className={`group-chip${g.on ? '' : ' is-off'}`} key={g.name} onClick={() => toggleGroup(g.name)}
                    onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), toggleGroup(g.name))} role="button" tabIndex={0} title={g.name}>
                    {g.name}
                  </span>
                ))}
              </div>
            )}
          </div>
          <div className="card card-section">
            <div className="row">
              <span className="step-number">2</span>
              <h2>Преподаватели</h2>
            </div>
            <div className="d-flex flex-col gap-16">
              <div className="row">
                <span className="avatar is-small">{initials(me.fullName)}</span>
                <b>{me.fullName}</b>
                <span className="text-muted fs-13">ведущий</span>
              </div>
              {teachers.map(t => (
                <div className="row" key={t.id}>
                  <span className="avatar is-small">{initials(t.fullName)}</span>
                  <b>{t.fullName}</b>
                  <span className="text-muted fs-13">@{t.login}</span>
                  <span className="spacer" />
                  <button aria-label="Удалить" className="icon-btn icon-btn--delete w-38 h-38" onClick={() => removeTeacher(t)} title="Удалить" type="button"><IconDelete /></button>
                </div>
              ))}
            </div>
            <div>
              <button className="btn-outline h-46 p-0-24" disabled={colleagues.length === 0} onClick={() => setTeacherModal(true)}
                title={colleagues.length === 0 ? 'Нет коллег, которых можно добавить' : undefined} type="button">+ Добавить преподавателя</button>
            </div>
          </div>
          <div className="card card-section flex-row items-center">
            <span className="step-number">3</span>
            <div className="flex-1">
              <h2>Приглашение для студентов</h2>
              <div className="text-muted">Создать пригласительную ссылку для Telegram-бота.</div>
            </div>
            <span aria-checked={invite} aria-label="Создать пригласительную ссылку" className={`switch${invite ? ' is-active' : ''}`}
              onClick={() => setInvite(v => !v)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), setInvite(v => !v))}
              role="switch" tabIndex={0} />
          </div>
          <div className="row justify-end">
            <Link className="btn-outline" to="/courses">Отмена</Link>
            <button className="btn-primary h-58 p-0-34 fs-15" disabled={busy || init.loading} onClick={create} type="button">
              {busy ? 'Создаём…' : 'Создать курс'}
            </button>
          </div>
        </div>
        <aside className="course-preview">
          <h2>Предпросмотр</h2>
          <div className="hero course-preview__card">
            <div className="row gap-8">
              <span className="pill">{semesterLabel ?? <span className="is-placeholder">Семестр</span>}</span>
            </div>
            <h2 className="course-preview__title">{title.trim() || <span className="is-placeholder">Название курса</span>}</h2>
            <div className="course-preview__meta">
              {selected.length
                ? `${selected.length} ${plural(selected.length, 'группа', 'группы', 'групп')}: ${selected.join(', ')}`
                : <span className="is-placeholder">группы не выбраны</span>}
            </div>
          </div>
        </aside>
      </div>

      <Modal open={missing !== null} onClose={() => setMissing(null)} size="lg" title="Курс нельзя создать"
        actions={<>
          <Link className="btn-outline" to="/courses">Отменить создание</Link>
          <button className="btn-primary" onClick={() => setMissing(null)} type="button">Продолжить</button>
        </>}>
        <div className="modal__text modal__text--regular">Заполните обязательные поля, чтобы создать курс: <b>{missing}</b>.</div>
      </Modal>

      <Modal open={teacherModal} onClose={() => setTeacherModal(false)} title="Добавить преподавателя"
        actions={<>
          <button className="btn-outline" onClick={() => setTeacherModal(false)} type="button">Отмена</button>
          <button className="btn-primary" disabled={teacherPick === ''} onClick={addTeacher} type="button">Добавить</button>
        </>}>
        <div className="modal__body">
          <label>Преподаватель
            <select className="input rounded-999" onChange={e => setTeacherPick(e.target.value ? Number(e.target.value) : '')} value={teacherPick}>
              <option value="">Выберите коллегу</option>
              {colleagues.map(t => <option key={t.id} value={t.id}>{t.fullName} (@{t.login})</option>)}
            </select>
          </label>
          <div className="modal__hint">Коллега получит доступ ко всем группам курса. Нового преподавателя можно добавить на вкладке «Преподаватели» курса.</div>
        </div>
      </Modal>

      <Modal open={failures !== null} onClose={() => (failures?.path ? navigate(failures.path) : setFailures(null))} size="lg"
        title={failures?.path ? 'Курс создан, но не всё получилось' : 'Курс не создан'}
        actions={failures?.path
          ? <button className="btn-primary" onClick={() => navigate(failures.path!)} type="button">Перейти в курс</button>
          : <button className="btn-primary" onClick={() => setFailures(null)} type="button">Понятно</button>}>
        <div className="modal__text modal__text--regular">
          {failures?.list.map(f => <div key={f.group}><b>{f.group}</b>: {f.error}</div>)}
        </div>
      </Modal>
      {dialog}
    </div>
  );
}
