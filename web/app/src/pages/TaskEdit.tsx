import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api } from '../api/api';
import type { HomeworkDto, HomeworkEditRequest, HomeworkTargetDto, HomeworkTargetKind, SubtaskDto } from '../api/types';
import { useCourse } from '../course/CourseLayout';
import { useCrumbs } from '../layout/AppLayout';
import { defaultDeadline, fromInput, plural, shortDateTime, toInput } from '../lib/format';
import css from '../styles/pages/new-task.css?inline';
import { IconCheck, IconClose, IconPen } from '../ui/icons';
import { PageStyle } from '../ui/PageStyle';
import { Alert, Async, useAction, useConfirm, useLoad, useToast } from '../ui/ui';
import { IconDash, IconRemove, IconTick, maxPoints, parseTitle, pointsText } from './tasks/taskCommon';

// Свои мелочи поверх new-task.css: блок «Кому» (его нет в дизайне) и заблокированные чипы.
const ownCss = `
@layer pages {
  .target-group { display: flex; flex-direction: column; gap: 10px; }
  .target-group__title { font-weight: 800; font-size: 14px; }
  .target-list { max-height: 280px; overflow-y: auto; padding: 6px; border: 1px solid var(--border-soft); border-radius: 18px; }
  .target-list .dropdown__option { white-space: normal; }
  .target-list__empty { padding: 8px 10px; }
  .group-chip[aria-disabled="true"] { cursor: default; opacity: .7; }
  .switch[aria-disabled="true"] { opacity: .6; }
  .input:disabled { opacity: .7; }
  .link-button { background: none; border: 0; padding: 0; color: inherit; text-decoration: underline; font: inherit; cursor: pointer; }
}`;

// Коды заданий (ДЗ, ДР, Г…) — как в дизайне: тип выбирается переключателем, номер присваивается автоматически.
// В API кода нет — он становится началом названия («ДЗ-3 Условная вероятность»), так его понимают и список заданий, и сводная.
const KINDS_KEY = 'fiit-hometask:task-kinds';
const DEFAULT_KINDS = ['ДЗ', 'ДР', 'Г'];
function loadKinds(): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(KINDS_KEY) ?? 'null');
    return Array.isArray(v) && v.length ? v : DEFAULT_KINDS;
  } catch {
    return DEFAULT_KINDS;
  }
}
function saveKinds(kinds: string[]) {
  try { localStorage.setItem(KINDS_KEY, JSON.stringify(kinds)); } catch { /* без сохранения */ }
}
const codeNumber = (code: string | null) => (code ? Number(code.split('-')[1]) : NaN);

const DAY = 24 * 60 * 60 * 1000;
const inWeeks = (n: number) => toInput(new Date(Date.parse(defaultDeadline()) + (n - 1) * 7 * DAY).toISOString());

interface SubtaskRow { key: number; id: number | null; title: string; points: string }
let rowSeq = 0;
const toRow = (s?: SubtaskDto): SubtaskRow =>
  ({ key: ++rowSeq, id: s?.id ?? null, title: s?.title ?? '', points: s?.maxScore == null ? '' : String(s.maxScore).replace('.', ',') });

/** '' → null, «2,5» → 2.5, мусор → NaN. */
function parsePoints(text: string): number | null {
  const t = text.trim().replace(',', '.');
  if (!t) return null;
  return /^\d+(\.\d+)?$/.test(t) ? Number(t) : NaN;
}

const sameSubtasks = (a: SubtaskDto[], b: SubtaskDto[]) =>
  a.length === b.length && a.every((s, i) => s.title.trim() === b[i].title.trim() && s.maxScore === b[i].maxScore);

/** new-task.html — создание и редактирование ДЗ. Маршруты: tasks/new и tasks/:homeworkId/edit. */
export default function TaskEdit() {
  const { homeworkId } = useParams();
  const id = homeworkId ? Number(homeworkId) : null;
  const state = useLoad<HomeworkDto | null>(() => (id ? api.homework(id) : Promise.resolve(null)), [id]);
  return (
    <>
      <PageStyle css={[css, ownCss]} />
      <Async state={state}>{hw => <Editor hw={hw} key={hw?.id ?? 'new'} />}</Async>
    </>
  );
}

function Editor({ hw }: { hw: HomeworkDto | null }) {
  const { course, subjectId, semester, activeCourses, readOnly: courseReadOnly, path } = useCourse();
  const navigate = useNavigate();
  const toast = useToast();
  const { run, busy } = useAction();
  const { confirm, dialog } = useConfirm();

  const published = !!hw?.isPublished;
  const ro = hw ? hw.isReadOnly : courseReadOnly;
  const subtasksRo = ro || !!hw?.subtasksLocked;
  const taskPath = hw ? `${path}/tasks/${hw.id}` : path;

  useCrumbs([
    course.isArchived ? { label: 'Архив', to: '/archive' } : { label: 'Курсы', to: '/courses' },
    { label: course.subjectName, to: path },
    { label: 'Задания', to: path },
    ...(hw ? [{ label: hw.title, to: taskPath }] : []),
    { label: hw ? 'Редактирование' : 'Новое' },
  ]);

  // ── Поля формы ──
  const parsed = parseTitle(hw?.title ?? '');
  const [kinds, setKinds] = useState(() => {
    const list = loadKinds();
    return parsed.prefix && !list.includes(parsed.prefix) ? [...list, parsed.prefix] : list;
  });
  const [kind, setKind] = useState(parsed.prefix ?? kinds[0]);
  const [name, setName] = useState(parsed.code ? parsed.name : hw?.title ?? '');
  const [codesOpen, setCodesOpen] = useState(false);
  const [recipientsOpen, setRecipientsOpen] = useState(false);
  const courseHomeworks = useLoad(() => api.homeworks(subjectId, semester), [subjectId, semester.year, semester.term]);
  // Номер: свой у редактируемого задания того же типа, иначе следующий после максимального в курсе
  const number = parsed.prefix === kind && !Number.isNaN(codeNumber(parsed.code))
    ? codeNumber(parsed.code)
    : 1 + Math.max(0, ...(courseHomeworks.data ?? [])
      .filter(h => h.id !== hw?.id)
      .map(h => parseTitle(h.title))
      .filter(t => t.prefix === kind)
      .map(t => codeNumber(t.code))
      .filter(n => !Number.isNaN(n)));
  const code = `${kind}-${number}`;
  const title = name.trim() ? `${code} ${name.trim()}` : '';
  const [link, setLink] = useState(hw?.link ?? '');
  const [deadline, setDeadline] = useState(toInput(hw?.deadline ?? defaultDeadline()));
  const [subtasks, setSubtasks] = useState<SubtaskDto[]>(hw?.subtasks ?? []);
  const [courseIds, setCourseIds] = useState<number[]>(hw ? hw.courses.map(c => c.id) : activeCourses.map(c => c.id));
  const [narrow, setNarrow] = useState((hw?.targets.length ?? 0) > 0);
  const [targets, setTargets] = useState<HomeworkTargetDto[]>(hw?.targets ?? []);
  const [notify, setNotify] = useState(true);
  const [subtasksOpen, setSubtasksOpen] = useState(false);
  const [incomplete, setIncomplete] = useState<string[] | null>(null);

  const total = maxPoints(subtasks);
  const deadlineIso = fromInput(deadline);
  const deadlineChanged = published && deadlineIso !== null && Date.parse(deadlineIso) !== Date.parse(hw!.deadline);
  const subtasksChanged = published && !sameSubtasks(subtasks, hw!.subtasks);
  const changesNotify = deadlineChanged || subtasksChanged;
  const willNotify = published ? changesNotify && notify : notify;

  // ── Адресаты ──
  const courseOptions = useMemo(() => [
    ...activeCourses.map(c => ({ id: c.id, name: c.subgroupName })),
    ...(hw?.courses ?? []).filter(c => !activeCourses.some(a => a.id === c.id)),
  ], [activeCourses, hw]);
  const sentTargets = narrow ? targets : [];
  const tags = useLoad(() => api.tags(), []);
  const courseKey = [...courseIds].sort((a, b) => a - b).join(',');
  const enrollments = useLoad(
    () => (courseIds.length ? api.enrollments({ courseIds, status: 'Approved' }) : Promise.resolve([])),
    [courseKey]);
  const students = useMemo(() => {
    const map = new Map<number, { id: number; name: string; groups: string[] }>();
    for (const e of enrollments.data ?? []) {
      const s = map.get(e.studentId) ?? { id: e.studentId, name: `${e.lastName} ${e.firstName}`, groups: [] };
      s.groups.push(e.courseName);
      map.set(e.studentId, s);
    }
    return [...map.values()].sort((a, b) => a.name.localeCompare(b.name, 'ru'));
  }, [enrollments.data]);

  const [count, setCount] = useState<number | null>(hw?.recipientsCount ?? null);
  const previewKey = JSON.stringify([courseKey, sentTargets]);
  useEffect(() => {
    if (!courseIds.length) { setCount(0); return; }
    let alive = true;
    const timer = setTimeout(() => {
      api.previewRecipients(courseIds, sentTargets)
        .then(r => alive && setCount(r.count))
        .catch(() => alive && setCount(null));
    }, 300);
    return () => { alive = false; clearTimeout(timer); };
  }, [previewKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const hasTarget = (kind: HomeworkTargetKind, refId: number) => targets.some(t => t.kind === kind && t.refId === refId);
  const toggleTarget = (kind: HomeworkTargetKind, refId: number) =>
    setTargets(ts => hasTarget(kind, refId) ? ts.filter(t => !(t.kind === kind && t.refId === refId)) : [...ts, { kind, refId }]);

  function toggleCourse(id: number) {
    if (courseIds.includes(id)) {
      const rest = courseIds.filter(c => c !== id);
      setCourseIds(rest);
      // Адресаты должны быть из курсов ДЗ: убираем подгруппу и студентов, которых больше нет в выбранных подгруппах
      const stay = new Set((enrollments.data ?? []).filter(e => rest.includes(e.courseId)).map(e => e.studentId));
      setTargets(ts => ts.filter(t => !(t.kind === 'Course' && t.refId === id)
        && !(t.kind === 'Student' && enrollments.data && !stay.has(t.refId))));
    } else {
      setCourseIds([...courseIds, id]);
    }
  }
  const allCourses = courseOptions.length > 0 && courseOptions.every(c => courseIds.includes(c.id));
  const someCourses = courseIds.length > 0 && !allCourses;

  // ── Сохранение ──
  function missingFields(): string[] {
    const missing: string[] = [];
    if (!title.trim()) missing.push('название');
    if (!link.trim()) missing.push('ссылку на условие');
    if (!deadlineIso) missing.push('дедлайн');
    if (!courseIds.length) missing.push('подгруппы');
    return missing;
  }

  function request(): HomeworkEditRequest | null {
    if (!deadlineIso) { toast('Укажите дедлайн', { error: true }); return null; }
    if (!courseIds.length) { toast('Выберите хотя бы одну подгруппу', { error: true }); return null; }
    return {
      subjectId, semester, title: title.trim(), link: link.trim(), deadline: deadlineIso,
      subtasks: subtasks.map(s => ({ id: s.id, title: s.title.trim(), maxScore: s.maxScore })),
      courseIds, targets: sentTargets, notifyStudents: notify,
    };
  }

  const persist = (r: HomeworkEditRequest) => (hw ? api.updateHomework(hw.id, r) : api.createHomework(r));

  async function save() {
    setIncomplete(null);
    const r = request();
    if (!r) return;
    const saved = await run(() => persist(r), published ? 'Изменения сохранены' : 'Черновик сохранён');
    if (saved) navigate(`${path}/tasks/${saved.id}`);
  }

  async function publish() {
    const missing = missingFields();
    if (missing.length) { setIncomplete(missing); return; }
    const r = request();
    if (!r) return;
    const saved = await run(() => persist(r));
    if (!saved) return;
    if (saved.recipientsCount === 0) {
      toast('У задания нет ни одного адресата — проверьте подгруппы и адресатов', { error: true });
      if (!hw) navigate(`${path}/tasks/${saved.id}/edit`, { replace: true });
      return;
    }
    const n = saved.recipientsCount;
    const ok = await confirm({
      title: 'Опубликовать задание?',
      text: `ДЗ получат ${n} ${plural(n, 'студент', 'студента', 'студентов')}` + (notify ? ': им придёт уведомление в Telegram.' : ' — без уведомления, задание появится в «Моих заданиях» в боте.'),
      ok: 'Опубликовать',
    });
    if (!ok) {
      toast('Черновик сохранён');
      if (!hw) navigate(`${path}/tasks/${saved.id}/edit`, { replace: true });
      return;
    }
    const result = await run(() => api.publishHomework(saved.id, notify));
    if (result) {
      toast(notify ? `Отправлено ${result.notified} ${plural(result.notified, 'студенту', 'студентам', 'студентам')}` : 'Задание опубликовано без уведомления');
      navigate(`${path}/tasks/${saved.id}`);
    } else if (!hw) {
      navigate(`${path}/tasks/${saved.id}/edit`, { replace: true });
    }
  }

  async function remove() {
    if (!hw) return;
    const ok = await confirm({
      title: published ? 'Удалить задание?' : 'Удалить черновик?',
      text: `«${hw.title}» будет удалено без возможности восстановления.`,
      ok: 'Удалить',
      danger: true,
    });
    if (!ok) return;
    if (await run(() => api.deleteHomework(hw.id).then(() => true), 'Задание удалено')) navigate(path);
  }

  // ── Предпросмотр ──
  const deadlineLabel = deadlineIso ? shortDateTime(deadlineIso).replace(' ', ' в ') : '';
  const pointsHint = () => toast(total !== null
    ? `Макс. балл равен сумме баллов подзадач (${String(total).replace('.', ',')}). Чтобы изменить его, измените баллы подзадач.`
    : 'Макс. балл — сумма баллов подзадач. Чтобы задать его, добавьте подзадачи с баллами.');

  if (!hw && courseReadOnly) return <Alert>Курс в архиве — создавать задания нельзя.</Alert>;

  return (
    <>
      <div>
        <h1>{ro ? 'Задание' : hw ? 'Редактирование задания' : 'Новое задание'}</h1>
      </div>
      {ro && <Alert>Курс в архиве — задание доступно только для просмотра.</Alert>}
      <div className="row items-start gap-28">
        <div className="flex-1 min-w-0 d-flex flex-col gap-20">
          <div className="card card-section">
            <div className="row">
              <span className="step-number">1</span>
              <h2>Основное</h2>
            </div>
            <div className="row gap-12">
              <div className="segmented w-300" role="tablist">
                {kinds.map(k => (
                  <span aria-disabled={ro} aria-selected={kind === k} className={kind === k ? 'is-active' : ''} key={k} role="tab" tabIndex={0}
                    onClick={() => !ro && setKind(k)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && !ro && setKind(k)}>{k}</span>
                ))}
              </div>
              {!ro && (
                <button aria-label="Изменить код" className="icon-btn w-42 h-42 flex-none" onClick={() => setCodesOpen(true)} title="Изменить код" type="button">
                  <IconPen />
                </button>
              )}
              <span className="spacer" />
              <div className="text-right">
                <div className="text-muted fs-12">Номер задания · присваивается автоматически</div>
                <b className="fs-26 ls-n_02em fs-26--x1">{code}</b>
              </div>
            </div>
            <label>Название
              <input className="input rounded-999" disabled={ro} maxLength={280} onChange={e => setName(e.target.value)} placeholder="Условная вероятность" value={name} />
            </label>
            <label>Ссылка на условие
              <input className="input rounded-999" disabled={ro} onChange={e => setLink(e.target.value)} placeholder="https://" type="url" value={link} />
            </label>
          </div>

          <div className="card card-section">
            <div className="row">
              <span className="step-number">2</span>
              <h2>Срок и оценка</h2>
            </div>
            <div className="row gap-14 items-end">
              <label className="flex-1">Дедлайн
                <input className="input rounded-999" disabled={ro} onChange={e => setDeadline(e.target.value)} type="datetime-local" value={deadline} />
              </label>
              <label className="w-110">Макс. балл
                <input className="input rounded-30" disabled={ro} inputMode="numeric" onChange={pointsHint} placeholder="—"
                  value={total === null ? '' : String(total).replace('.', ',')} />
              </label>
            </div>
            {!ro && (
              <div className="row gap-8">
                {[1, 2].map(n => {
                  const value = inWeeks(n);
                  const active = deadline === value;
                  return (
                    <button aria-pressed={active} className={`quick-chip${active ? ' is-active' : ''}`} key={n} onClick={() => setDeadline(value)} type="button">
                      {n === 1 ? 'через неделю' : 'через 2 недели'}
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          <div className="card card-section">
            <div className="row">
              <span className="step-number bg-border-soft text-brand">3</span>
              <div className="flex-1">
                <h2>Подзадачи</h2>
                <div className="text-muted">
                  {hw?.subtasksLocked
                    ? 'Подзадачи менять нельзя: по ним уже выставлены баллы'
                    : 'Необязательно: разбейте задание на части с отдельными баллами'}
                </div>
              </div>
              {!subtasksRo && (
                <button className="btn-outline" onClick={() => setSubtasksOpen(true)} type="button">{subtasks.length ? 'Изменить' : '+ Добавить'}</button>
              )}
            </div>
            {subtasks.length > 0 && (
              <ol className="subtask-list">
                {subtasks.map((s, i) => (
                  <li className="subtask-item" key={s.id ?? `new-${i}`}>
                    <div className="subtask-item__box">
                      <span className="subtask-item__title">{s.title}</span>
                      {s.maxScore !== null && <span className="subtask-item__points">{pointsText(s.maxScore)}</span>}
                    </div>
                    {!subtasksRo && (
                      <button aria-label="Удалить подзадачу" className="subtask-remove" title="Удалить подзадачу" type="button"
                        onClick={() => setSubtasks(subtasks.filter((_, j) => j !== i))}>
                        <IconRemove />
                      </button>
                    )}
                  </li>
                ))}
                {total !== null && total > 0 && <li className="subtask-total">Всего: {pointsText(total)}</li>}
              </ol>
            )}
          </div>

          <div className="card card-section flex-row items-center">
            <span className="step-number bg-border-soft text-brand">4</span>
            <div className="flex-1">
              <h2>Приглашение для студентов</h2>
              <div className="text-muted">
                {!published
                  ? 'Необязательно: отправить уведомление студентам через Telegram-бота'
                  : changesNotify
                    ? 'Адресаты получат в Telegram сообщение об изменении дедлайна или подзадач'
                    : 'Название и ссылка меняются без уведомления; уведомление уходит при изменении дедлайна или подзадач'}
              </div>
            </div>
            <span aria-checked={notify} aria-disabled={ro || (published && !changesNotify)} aria-label="Отправить уведомление студентам"
              className={`switch${notify ? ' is-active' : ''}`} role="switch" tabIndex={0}
              onClick={() => !ro && setNotify(n => !n)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && !ro && setNotify(n => !n)} />
          </div>

          {!ro && (
            <div className="row justify-end">
              {hw && hw.submittedCount === 0 && (
                <>
                  <button className="btn-outline" disabled={busy} onClick={remove} type="button">{published ? 'Удалить задание' : 'Удалить черновик'}</button>
                  <span className="spacer" />
                </>
              )}
              <Link className="btn-outline" to={taskPath}>Отмена</Link>
              {published
                ? <button className="btn-primary h-52" disabled={busy} onClick={save} type="button">Сохранить изменения</button>
                : (
                  <>
                    <button className="btn-outline" disabled={busy} onClick={save} type="button">Сохранить черновик</button>
                    <button className="btn-primary h-52" disabled={busy} onClick={publish} type="button">Опубликовать</button>
                  </>
                )}
            </div>
          )}
        </div>

        <div className="task-side">
          <aside className="panel-dark p-28 d-flex flex-col gap-16">
            <div className="dots" />
            <h2 className="text-on-brand">Общие сведения</h2>
            <div className="bg-surface text-ink p-18 bg-surface--x1 task-summary__card">
              <div className="text-muted fs-12 fw-700 task-summary__course">{course.subjectName}</div>
              <div className="fw-800 fs-17 m-6-0 fw-800--x1 task-summary__title">
                {code} · <span className={name.trim() ? undefined : 'is-placeholder'}>{name.trim() || 'Название задания'}</span>
              </div>
              <div className="task-summary__deadline">Дедлайн: <span className={deadlineLabel ? undefined : 'is-placeholder'}>{deadlineLabel || 'не указан'}</span></div>
              <div className="task-summary__points">Баллы: <span className={total !== null ? undefined : 'is-placeholder'}>{total !== null ? String(total).replace('.', ',') : 'не указаны'}</span></div>
              {willNotify && (
                <div className="status-tile status-tile--compact task-summary__notify"><span className="status-tile__label">Уведомления</span><IconTick className="status-tile__check" /></div>
              )}
              {link.trim() && (
                <div className="status-tile status-tile--compact task-summary__condition"><span className="status-tile__label">Условие</span><IconTick className="status-tile__check" /></div>
              )}
            </div>
            <div className="opacity-75 fs-13">
              Адресатов: {count === null ? '…' : count}
              {' · '}<button className="link-button" onClick={() => setRecipientsOpen(true)} type="button">{ro ? 'кто' : 'изменить'}</button>
              {published && hw?.publishedAt ? ` · опубликовано ${shortDateTime(hw.publishedAt)}` : ''}
            </div>
            {willNotify && (
              <div className="opacity-75 fs-13">
                {published ? 'Уведомление об изменении придёт в Telegram после сохранения' : 'Уведомление придёт в Telegram сразу после публикации'}
              </div>
            )}
          </aside>
        </div>
      </div>

      {subtasksOpen && (
        <SubtasksDialog initial={subtasks} onClose={() => setSubtasksOpen(false)} onSave={next => {
          const before = maxPoints(subtasks);
          const after = maxPoints(next);
          setSubtasks(next);
          setSubtasksOpen(false);
          if (before !== null && before !== after) toast('Максимальный балл был изменён!');
        }} />
      )}

      {recipientsOpen && (
        <Dialog onClose={() => setRecipientsOpen(false)} size="wide" title="Кому"
          actions={<button className="btn-primary" onClick={() => setRecipientsOpen(false)} type="button">Готово</button>}>
          <div className="text-muted">
            {published ? 'После публикации круг адресатов можно только расширить' : 'По умолчанию — все студенты выбранных подгрупп'}
          </div>
            <div className="target-group">
              <div className="target-group__title">Подгруппы</div>
              {!ro && courseOptions.length > 1 && (
                <button aria-checked={allCourses ? 'true' : someCourses ? 'mixed' : 'false'} className="select-all" role="checkbox" type="button"
                  onClick={() => {
                    if (allCourses) { setCourseIds([]); setTargets([]); } else setCourseIds(courseOptions.map(c => c.id));
                  }}>
                  <span className="select-all__box"><IconCheck className="select-all__tick" /><IconDash className="select-all__dash" /></span>
                  <span className="select-all__label">Выбрать все</span>
                </button>
              )}
              <div className="row gap-10 flex-wrap">
                {courseOptions.map(c => (
                  <Chip disabled={ro} key={c.id} on={courseIds.includes(c.id)} onToggle={() => toggleCourse(c.id)}>{c.name}</Chip>
                ))}
              </div>
            </div>
            <div className="segmented w-340" role="tablist">
              {[false, true].map(v => (
                <span aria-disabled={ro} aria-selected={narrow === v} className={narrow === v ? 'is-active' : ''} key={String(v)} role="tab" tabIndex={0}
                  onClick={() => !ro && setNarrow(v)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && !ro && setNarrow(v)}>
                  {v ? 'Выбрать адресатов' : 'Все студенты'}
                </span>
              ))}
            </div>
            {narrow && (
              <Targets courseOptions={courseOptions.filter(c => courseIds.includes(c.id))} has={hasTarget} ro={ro}
                students={students} studentsLoading={enrollments.loading} tags={tags.data ?? []} toggle={toggleTarget} />
            )}
            <div className="text-muted">
              {narrow && targets.length === 0 && 'Никто не выбран — задание получат все студенты выбранных подгрупп. '}
              Итого: <b className="text-ink">{count === null ? '…' : `${count} ${plural(count, 'студент', 'студента', 'студентов')}`}</b>
            </div>
        </Dialog>
      )}

      {codesOpen && (
        <CodesDialog kinds={kinds} onClose={() => setCodesOpen(false)} onSave={next => {
          setKinds(next);
          saveKinds(next);
          if (!next.includes(kind)) setKind(next[0]);
          setCodesOpen(false);
        }} />
      )}

      {incomplete && (
        <Dialog onClose={() => setIncomplete(null)} size="lg" title="Задание нельзя опубликовать"
          actionsClass="modal__actions modal__actions--split"
          actions={<>
            <Link className="btn-outline" to={taskPath}>{hw ? 'Отменить изменения' : 'Отменить создание'}</Link>
            <div className="modal__actions-main">
              <button className="btn-outline" disabled={busy} onClick={save} type="button">Сохранить как черновик</button>
              <button className="btn-primary" onClick={() => setIncomplete(null)} type="button">Продолжить</button>
            </div>
          </>}>
          <div className="modal__text modal__text--regular">
            Заполните обязательные поля, чтобы опубликовать задание: <b>{incomplete.join(', ')}</b>. Или сохраните его как черновик и вернитесь к редактированию позже.
          </div>
        </Dialog>
      )}
      {dialog}
    </>
  );
}

// ── Коды заданий (модалка из дизайна) ────────────────────────────────

function CodesDialog({ kinds, onSave, onClose }: { kinds: string[]; onSave: (k: string[]) => void; onClose: () => void }) {
  const [list, setList] = useState(kinds);
  const [value, setValue] = useState('');
  const add = () => {
    const v = value.trim().toUpperCase();
    if (/^[A-ZА-ЯЁ]{1,4}$/u.test(v) && !list.includes(v)) setList([...list, v]);
    setValue('');
  };
  return (
    <Dialog onClose={onClose} size="narrow" title="Коды заданий"
      actions={<>
        <button className="btn-outline" onClick={onClose} type="button">Отмена</button>
        <button className="btn-primary" disabled={list.length === 0} onClick={() => onSave(list)} type="button">Сохранить</button>
      </>}>
      <ul className="code-list">
        {list.map((k, i) => (
          <li className="code-list__item" key={k}>
            <span className={`task-tag task-tag--${(i % 3) + 1}`}>{k}</span>
            <button aria-label="Удалить код" className="icon-btn w-38 h-38" disabled={list.length === 1} onClick={() => setList(list.filter(x => x !== k))} title="Удалить" type="button">
              <IconClose />
            </button>
          </li>
        ))}
      </ul>
      <div className="combo-field">
        <input aria-label="Новый код" className="combo-field__input" maxLength={4} onChange={e => setValue(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }} placeholder="Новый код" value={value} />
        <span aria-hidden="true" className="combo-field__divider" />
        <button className={`combo-field__button${value.trim() ? ' is-filled' : ''}`} onClick={add} type="button">Добавить</button>
      </div>
    </Dialog>
  );
}

// ── Блок «Выбрать адресатов»: подгруппы целиком, метки, студенты ─────

function Targets({ courseOptions, tags, students, studentsLoading, has, toggle, ro }: {
  courseOptions: { id: number; name: string }[];
  tags: { id: number; name: string; studentsCount: number }[];
  students: { id: number; name: string; groups: string[] }[];
  studentsLoading: boolean;
  has: (kind: HomeworkTargetKind, refId: number) => boolean;
  toggle: (kind: HomeworkTargetKind, refId: number) => void;
  ro: boolean;
}) {
  const [q, setQ] = useState('');
  const query = q.trim().toLowerCase();
  const shown = students.filter(s => !query || s.name.toLowerCase().includes(query) || s.groups.some(g => g.toLowerCase().includes(query)));
  const picked = students.filter(s => has('Student', s.id)).length;
  return (
    <>
      <div className="text-muted fs-13">Задание получат студенты, подходящие под любой из выбранных вариантов</div>
      {courseOptions.length > 1 && (
        <div className="target-group">
          <div className="target-group__title">Подгруппы целиком</div>
          <div className="row gap-10 flex-wrap">
            {courseOptions.map(c => (
              <Chip disabled={ro} key={c.id} on={has('Course', c.id)} onToggle={() => toggle('Course', c.id)}>{c.name}</Chip>
            ))}
          </div>
        </div>
      )}
      <div className="target-group">
        <div className="target-group__title">Метки</div>
        {tags.length === 0
          ? <div className="text-muted fs-13">Меток пока нет — их можно создать на вкладке «Студенты»</div>
          : (
            <div className="row gap-10 flex-wrap">
              {tags.map(t => (
                <Chip disabled={ro} key={t.id} on={has('Tag', t.id)} onToggle={() => toggle('Tag', t.id)}>{t.name} · {t.studentsCount}</Chip>
              ))}
            </div>
          )}
      </div>
      <div className="target-group">
        <div className="target-group__title">Студенты{picked > 0 ? ` · выбрано ${picked}` : ''}</div>
        <input className="input rounded-999" onChange={e => setQ(e.target.value)} placeholder="Поиск по фамилии или подгруппе" value={q} />
        <div className="target-list">
          {studentsLoading && students.length === 0 && <div className="text-muted fs-13 target-list__empty">Загрузка…</div>}
          {!studentsLoading && shown.length === 0 && <div className="text-muted fs-13 target-list__empty">Студентов не найдено</div>}
          {shown.map(s => (
            <label className="dropdown__option" key={s.id}>
              <input checked={has('Student', s.id)} disabled={ro} onChange={() => toggle('Student', s.id)} type="checkbox" />
              <span className="flex-1">{s.name}</span>
              <span className="text-muted fs-12">{s.groups.join(', ')}</span>
            </label>
          ))}
        </div>
      </div>
    </>
  );
}

function Chip({ on, onToggle, disabled, children }: { on: boolean; onToggle: () => void; disabled?: boolean; children: ReactNode }) {
  const act = () => !disabled && onToggle();
  return (
    <span aria-disabled={disabled || undefined} aria-pressed={on} className={`group-chip${on ? '' : ' is-off'}`} role="button" tabIndex={disabled ? -1 : 0}
      onClick={act} onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); act(); } }}>
      {children}
    </span>
  );
}

// ── Окно «Подзадачи» (modal-subtasks дизайна) ───────────────────────

function SubtasksDialog({ initial, onSave, onClose }: { initial: SubtaskDto[]; onSave: (s: SubtaskDto[]) => void; onClose: () => void }) {
  const toast = useToast();
  const [rows, setRows] = useState<SubtaskRow[]>(() => (initial.length ? initial.map(toRow) : [toRow()]));
  const set = (key: number, patch: Partial<SubtaskRow>) => setRows(rs => rs.map(r => (r.key === key ? { ...r, ...patch } : r)));

  function removeRow(key: number) {
    // Как в дизайне: последнюю строку не удаляем, а очищаем
    setRows(rs => (rs.length === 1 ? [{ ...rs[0], id: null, title: '', points: '' }] : rs.filter(r => r.key !== key)));
  }

  function confirmRows() {
    const filled = rows.filter(r => r.title.trim());
    const result: SubtaskDto[] = [];
    for (const r of filled) {
      const p = parsePoints(r.points);
      if (p !== null && (Number.isNaN(p) || p <= 0)) {
        toast(`Баллы подзадачи «${r.title.trim()}» — положительное число`, { error: true });
        return;
      }
      result.push({ id: r.id, title: r.title.trim(), maxScore: p });
    }
    onSave(result);
  }

  return (
    <Dialog onClose={onClose} size="wide" title="Подзадачи"
      actions={<>
        <button className="btn-outline" onClick={onClose} type="button">Отмена</button>
        <button className="btn-primary" onClick={confirmRows} type="button">Готово</button>
      </>}>
      <div className="modal__rows">
        {rows.map((r, i) => (
          <div className="row gap-14 items-end" key={r.key}>
            <label className="flex-1">Название подзадачи
              <input autoFocus={i === rows.length - 1} className="input rounded-999" maxLength={300} onChange={e => set(r.key, { title: e.target.value })}
                placeholder="Например, пункт а)" value={r.title} />
            </label>
            <label className="w-110">Баллы
              <input className="input rounded-30" inputMode="decimal" onChange={e => set(r.key, { points: e.target.value })} placeholder="0" value={r.points} />
            </label>
            <button aria-label="Удалить подзадачу" className="subtask-remove subtask-remove--row" onClick={() => removeRow(r.key)} title="Удалить подзадачу" type="button">
              <IconRemove />
            </button>
          </div>
        ))}
      </div>
      <div>
        <button className="btn-outline" id="subtask-add-row" onClick={() => setRows(rs => [...rs, toRow()])} type="button">+ Добавить ещё</button>
      </div>
    </Dialog>
  );
}

/** Разметка modal.css с размером и классом блока действий (общий Modal умеет только --lg). */
function Dialog({ title, size, onClose, children, actions, actionsClass = 'modal__actions' }: {
  title: string; size: 'lg' | 'wide' | 'narrow'; onClose: () => void; children: ReactNode; actions: ReactNode; actionsClass?: string;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div aria-modal="true" className="modal" role="dialog">
      <div className="modal__backdrop" onClick={onClose} />
      <div className={`modal__dialog modal__dialog--${size}`}>
        <button aria-label="Закрыть" className="modal__close" onClick={onClose} title="Закрыть" type="button"><IconClose /></button>
        <h2 className="modal__title">{title}</h2>
        {children}
        <div className={actionsClass}>{actions}</div>
      </div>
    </div>
  );
}
