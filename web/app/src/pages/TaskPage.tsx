import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api } from '../api/api';
import { saveBlob } from '../api/client';
import type { CellStatus, GradebookDto, HomeworkDto } from '../api/types';
import { useCourse } from '../course/CourseLayout';
import { useCrumbs } from '../layout/AppLayout';
import { initials, plural, score, shortDateTime } from '../lib/format';
import css from '../styles/pages/task-page.css?inline';
import { IconCheck } from '../ui/icons';
import { IconPen } from '../ui/icons';
import { PageStyle } from '../ui/PageStyle';
import { Alert, Async, Empty, Loading, useAction, useConfirm, useLoad, useToast } from '../ui/ui';
import { codePrefixes, FilterMenu, maxPoints, parseTitle, passes, pointsText, tagClass, type FilterOption } from './tasks/taskCommon';

// Фильтр «Статус» дизайна: три группы статусов ячейки сводной.
type StatusGroup = 'none' | 'review' | 'done';
const statusGroup: Record<CellStatus, StatusGroup> = {
  NotAssigned: 'none', NotSubmitted: 'none', Overdue: 'none', OnReview: 'review', Reviewed: 'done', ReviewedWithUnsentChanges: 'done',
};
const statusOptions: FilterOption[] = [
  { value: 'none', label: 'Не сдано' },
  { value: 'review', label: 'На проверке' },
  { value: 'done', label: 'Проверено' },
];

const IconLate = () => (
  <svg aria-hidden="true" fill="none" height="16" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.3" viewBox="0 0 24 24" width="16">
    <circle cx="12" cy="12" r="9" />
    <path d="M12 7v5l3 2" />
  </svg>
);
const IconEyeSmall = () => (
  <svg aria-hidden="true" fill="none" height="12" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.8" viewBox="0 0 24 24" width="12">
    <path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" />
    <circle cx="12" cy="12" r="3" />
  </svg>
);
const IconMinus = () => (
  <svg aria-hidden="true" fill="none" height="12" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.8" viewBox="0 0 24 24" width="12">
    <path d="M7 12h10" />
  </svg>
);

function StatusPill({ status }: { status: CellStatus }) {
  switch (status) {
    case 'OnReview':
      return <span className="status-pill chip--warn rounded-999"><u><IconEyeSmall /></u>На проверке</span>;
    case 'Reviewed':
      return <span className="status-pill chip--brand rounded-999"><u><IconCheck height={12} width={12} strokeWidth={2.8} /></u>Проверено</span>;
    case 'ReviewedWithUnsentChanges':
      return <span className="status-pill chip--warn rounded-999" title="После отправки проверку правили — изменения ещё не отправлены студенту">
        <u><IconCheck height={12} width={12} strokeWidth={2.8} /></u>Есть неотправленные правки</span>;
    case 'Overdue':
      return <span className="status-pill chip--gray rounded-999"><u><IconMinus /></u>Просрочено</span>;
    default:
      return <span className="status-pill chip--gray rounded-999"><u><IconMinus /></u>Не сдано</span>;
  }
}

/** task-page.html — страница задания: сдачи студентов по этому ДЗ (из сводной таблицы). */
export default function TaskPage() {
  const { homeworkId } = useParams();
  const id = Number(homeworkId);
  const { subjectId, semester } = useCourse();
  const state = useLoad(async () => {
    const [hw, all] = await Promise.all([api.homework(id), api.homeworks(subjectId, semester)]);
    return { hw, prefixes: codePrefixes(all.map(h => h.title)) };
  }, [id]);
  return (
    <>
      <PageStyle css={css} />
      <Async state={state}>{d => <Task hw={d.hw} prefixes={d.prefixes} reload={state.reload} />}</Async>
    </>
  );
}

function Task({ hw, prefixes, reload }: { hw: HomeworkDto; prefixes: string[]; reload: () => void }) {
  const { course, subjectId, semester, path } = useCourse();
  const { run, busy } = useAction();
  const { confirm, dialog } = useConfirm();
  const toast = useToast();
  const t = parseTitle(hw.title);
  const points = maxPoints(hw.subtasks);

  useCrumbs([
    course.isArchived ? { label: 'Архив', to: '/archive' } : { label: 'Курсы', to: '/courses' },
    { label: course.subjectName, to: path },
    { label: 'Задания', to: path },
  ]);

  async function exportXlsx() {
    const file = await run(() => api.exportGradebook(subjectId, semester, { homeworkId: hw.id }));
    if (file) saveBlob(file.blob, file.fileName ?? `${hw.title}.xlsx`);
  }

  async function publish() {
    const n = hw.recipientsCount;
    if (n === 0) {
      toast('У задания нет ни одного адресата — измените подгруппы или адресатов', { error: true });
      return;
    }
    const ok = await confirm({
      title: 'Опубликовать задание?',
      text: `ДЗ получат ${n} ${plural(n, 'студент', 'студента', 'студентов')}: им придёт уведомление в Telegram.`,
      ok: 'Опубликовать',
    });
    if (!ok) return;
    const result = await run(() => api.publishHomework(hw.id));
    if (result) {
      toast(`Отправлено ${result.notified} ${plural(result.notified, 'студенту', 'студентам', 'студентам')}`);
      reload();
    }
  }

  return (
    <>
      <div>
        <div className="row">
          {t.code && <span className={`${tagClass(t.prefix, prefixes)} h-36 min-w-64 fs-14`}>{t.code}</span>}
          <h1>{t.name}</h1>
          {/* Редактирование — карандашом у названия: в дизайне справа только выгрузка */}
          <Link aria-label={hw.isReadOnly ? 'Подробнее' : 'Редактировать задание'} className="icon-btn w-42 h-42 flex-none"
            title={hw.isReadOnly ? 'Подробнее' : 'Редактировать задание'} to={`${path}/tasks/${hw.id}/edit`}>
            <IconPen />
          </Link>
          <span className="spacer" />
          {hw.isPublished
            ? <button className="btn-dark h-56 p-0-30 fs-15" disabled={busy} onClick={exportXlsx} type="button">Выгрузить в Excel</button>
            : !hw.isReadOnly && <button className="btn-dark h-56 p-0-30 fs-15" disabled={busy} onClick={publish} type="button">Опубликовать</button>}
        </div>
        <div className="text-muted mt-10">
          Дедлайн {shortDateTime(hw.deadline).replace(' ', ' в ')}
          {points !== null && <> · {pointsText(points)}</>}
          {' · '}<a className="link-brand" href={hw.link} rel="noreferrer" target="_blank">условие</a>
          {!hw.isPublished && <> · черновик</>}
        </div>
      </div>
      {hw.isPublished
        ? <Submissions hw={hw} subjectId={subjectId} semester={semester} />
        : <Empty>Задание ещё не опубликовано — студенты его не видят. Опубликуйте его, чтобы адресаты получили уведомление.</Empty>}
      {dialog}
    </>
  );
}

function Submissions({ hw, subjectId, semester }: { hw: HomeworkDto; subjectId: number; semester: HomeworkDto['semester'] }) {
  const state = useLoad(() => api.gradebook(subjectId, semester, { homeworkId: hw.id }), [hw.id]);
  if (state.error && !state.data) return <Alert>{state.error}</Alert>;
  if (!state.data) return <Loading />;
  return <SubmissionsTable book={state.data} hw={hw} />;
}

function SubmissionsTable({ hw, book }: { hw: HomeworkDto; book: GradebookDto }) {
  const navigate = useNavigate();
  const rows = useMemo(() => book.rows
    .map(r => ({ row: r, cell: r.cells.find(c => c.homeworkId === hw.id) }))
    .filter((x): x is { row: typeof x.row; cell: NonNullable<typeof x.cell> } => !!x.cell && x.cell.status !== 'NotAssigned'),
  [book, hw.id]);

  const groups = useMemo(() => [...new Set(rows.map(x => x.row.subgroupName))].sort((a, b) => a.localeCompare(b, 'ru')), [rows]);
  const versions = useMemo(() => [...new Set(rows.filter(x => x.cell.versions > 0).map(x => x.cell.versions))].sort((a, b) => a - b).map(v => `v${v}`), [rows]);
  const statuses = statusOptions.map(o => o.value);

  const [status, setStatus] = useState<string[]>(statuses);
  const [group, setGroup] = useState<string[]>(groups);
  const [late, setLate] = useState<string[]>([]);
  const [version, setVersion] = useState<string[]>(versions);

  const shown = rows.filter(({ row, cell }) =>
    passes(status, statuses, statusGroup[cell.status])
    && passes(group, groups, row.subgroupName)
    && (!late.includes('late') || cell.isLate)
    && (version.length === versions.length || (cell.versions > 0 && version.includes(`v${cell.versions}`))));

  const pill = (label: string, filtered: boolean) => ({ open, toggle }: { open: boolean; toggle: (e: React.MouseEvent) => void }) => (
    <button aria-expanded={open} aria-haspopup="true" className={`card p-11-18 rounded-999 fw-800 nowrap filter-pill${filtered ? ' is-filtered' : ''}`}
      onClick={toggle} type="button">{label} ▾</button>
  );
  const review = (studentId: number) => navigate(`/review/${hw.id}/${studentId}`);

  if (rows.length === 0) return <Empty>Адресатов у задания пока нет</Empty>;

  return (
    <>
      <div className="row gap-10 flex-wrap">
        <span className="spacer" />
        <FilterMenu checked={status} list onChange={setStatus} options={statusOptions}
          trigger={pill('Все статусы', status.length !== statuses.length)} />
        {groups.length > 0 && (
          <FilterMenu checked={group} list onChange={setGroup} options={groups.map(g => ({ value: g, label: g }))}
            trigger={pill('Все группы', group.length !== groups.length)} />
        )}
        <FilterMenu checked={late} end list onChange={setLate} options={[{ value: 'late', label: 'Опоздание' }]}
          trigger={pill('Все метки', late.length > 0)} />
        {versions.length > 1 && (
          <FilterMenu checked={version} end list onChange={setVersion} options={versions.map(v => ({ value: v, label: v }))}
            trigger={pill('Все версии', version.length !== versions.length)} />
        )}
      </div>
      <div className="card p-8-0">
        <table>
          <thead>
            <tr>
              <th>Студент</th>
              <th>Группа</th>
              <th>Статус</th>
              <th>Метка</th>
              <th>Версия</th>
              <th>Сдано</th>
              <th>Балл</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {shown.map(({ row, cell }) => {
              const name = `${row.lastName} ${row.firstName}`;
              const submitted = cell.latestSubmissionId !== null;
              const done = statusGroup[cell.status] === 'done';
              return (
                <tr key={row.studentId}>
                  <td>
                    <div className="row gap-10">
                      <span className="avatar is-small">{initials(name)}</span>
                      {submitted
                        ? <Link className="text-ink" title="Открыть работу" to={`/review/${hw.id}/${row.studentId}`}><b>{name}</b></Link>
                        : <b>{name}</b>}
                    </div>
                  </td>
                  <td className="text-muted">{row.subgroupName}</td>
                  <td><StatusPill status={cell.status} /></td>
                  <td>
                    {cell.isLate && (
                      <span aria-label="Сдано с опозданием" className="late-mark" role="img" title="Сдано с опозданием"><IconLate /></span>
                    )}
                  </td>
                  <td>{cell.versions > 0 ? `v${cell.versions}` : '—'}</td>
                  <td className="text-muted" title={cell.reviewedBy ? `Проверил(а): ${cell.reviewedBy}` : undefined}>{cell.submittedAt ? shortDateTime(cell.submittedAt) : '—'}</td>
                  <td><b>{cell.score === null ? '—' : score(cell.score)}</b></td>
                  <td className="text-right">
                    {submitted && (
                      <button className={`btn-sm${done ? ' is-outline' : ''}`} onClick={() => review(row.studentId)} type="button">
                        {done ? 'Открыть' : 'Проверить'}
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
            {shown.length === 0 && <tr><td className="text-muted" colSpan={8}>Нет студентов под выбранные фильтры</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  );
}
