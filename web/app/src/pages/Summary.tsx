import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { api } from '../api/api';
import { saveBlob } from '../api/client';
import type { GradebookCell, GradebookHomework, GradebookRow } from '../api/types';
import { useCourse, useHeroAction } from '../course/CourseLayout';
import { dateTime, initials, score } from '../lib/format';
import css from '../styles/pages/summary.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, useAction, useLoad } from '../ui/ui';
import { CheckFilter, useCourseCrumbs } from './StudentFilters';

// Отметки в ячейках, которых нет в дизайне (ТЗ П-8.2): опоздание, версия, неотправленные правки, просрочка.
const extraCss = `
@layer pages {
  .summary-toolbar { flex-wrap: wrap; }
  .hw-head { display: block; max-width: 200px; overflow: hidden; text-overflow: ellipsis; color: inherit; text-decoration: none; }
  .hw-head:hover { color: var(--brand-strong); }
  .cell-marks { display: inline-flex; align-items: center; gap: 6px; }
  .cell-flag { font-size: 11px; font-weight: 800; color: var(--text-danger); text-transform: lowercase; }
  .cell-version { font-size: 11px; font-weight: 800; color: var(--ink-muted); }
  .cell-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--accent); flex: none; }
  .status-text.is-overdue { color: var(--text-danger); }
}`;

const IconOpen = () => (
  <svg aria-hidden="true" fill="none" height="14" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" viewBox="0 0 24 24" width="14">
    <path d="M7 17L17 7M8 7h9v9" />
  </svg>
);

/** summary.html — сводная таблица (П-8). */
export default function Summary() {
  const { course, subjectId, semester, path } = useCourse();
  useCourseCrumbs('Сводная');
  const navigate = useNavigate();
  const { run, busy } = useAction();

  // Фильтры (П-8.4), как в дизайне — воронками в шапке столбцов: подгруппы у «Группа», метки у «Студент» (если метки есть)
  const [subgroups, setSubgroups] = useState<Set<string> | null>(null);
  const [tagSel, setTagSel] = useState<Set<string> | null>(null);
  const tags = useLoad(() => api.tags(), []);
  // Метки студентов — из списка студентов курса (в сводной их нет)
  const students = useLoad(() => api.enrollments({ courseIds: course.courses.map(c => c.id), status: 'Approved' }), [course.key]);

  const subgroupOptions = course.courses.map(c => ({ value: String(c.id), label: c.subgroupName }));
  const courseIds = subgroups ? subgroupOptions.map(o => Number(o.value)).filter(id => subgroups.has(String(id))) : undefined;
  const nothingSelected = courseIds !== undefined && courseIds.length === 0;
  const tagOptions = (tags.data ?? []).map(t => ({ value: String(t.id), label: t.name }));
  const taggedStudents = useMemo(() => {
    if (!tagSel) return null;
    return new Set((students.data ?? []).filter(e => e.tags.some(t => tagSel.has(String(t.id)))).map(e => e.studentId));
  }, [tagSel, students.data]);
  const filter = { courseIds, tagId: tagSel?.size === 1 ? Number([...tagSel][0]) : undefined };

  const book = useLoad(
    () => nothingSelected ? Promise.resolve({ homeworks: [], rows: [] }) : api.gradebook(subjectId, semester, { courseIds }),
    [subjectId, semester.year, semester.term, JSON.stringify(courseIds), nothingSelected],
  );

  async function exportXlsx() {
    const file = await run(() => api.exportGradebook(subjectId, semester, filter));
    if (file) saveBlob(file.blob, file.fileName ?? 'сводная.xlsx');
  }

  useHeroAction(
    <button className="btn-dark" disabled={busy || !book.data?.rows.length} onClick={exportXlsx} type="button">
      {busy ? 'Выгружаем…' : 'Экспорт в Excel'}
    </button>,
    [busy, book.data, JSON.stringify(filter)],
  );

  const openWork = (homeworkId: number, row: GradebookRow) => navigate(`/review/${homeworkId}/${row.studentId}`);

  return (
    <>
      <PageStyle css={[css, extraCss]} />
      <Async state={book}>
        {data => data.homeworks.length === 0 && data.rows.length === 0 && !courseIds && !tagSel
          ? <Empty>Пока нет ни студентов, ни опубликованных заданий — таблица появится, когда они будут</Empty>
          : (
            <div className="card p-8-0 overflow-x-auto">
              <table className="score-table min-w-760">
                <thead>
                  <tr>
                    <th className={tagSel ? 'is-filtered-col' : undefined}>
                      {tagOptions.length > 0
                        ? <span className="th-filter">Студент<CheckFilter label="Фильтр по метке" options={tagOptions} selected={tagSel} onChange={setTagSel} /></span>
                        : 'Студент'}
                    </th>
                    <th className={subgroups ? 'is-filtered-col' : undefined}>
                      <span className="th-filter">Группа
                        <CheckFilter label="Фильтр по подгруппе" options={subgroupOptions} selected={subgroups} onChange={setSubgroups} />
                      </span>
                    </th>
                    {data.homeworks.map(h => (
                      <th data-code key={h.id}>
                        <Link className="hw-head" title={homeworkHint(h)} to={`${path}/tasks/${h.id}`}>{h.title}</Link>
                      </th>
                    ))}
                    <th className="col-total" title="Средняя оценка по отправленным проверкам">Итог</th>
                  </tr>
                </thead>
                <tbody>
                  {data.rows.filter(r => !taggedStudents || taggedStudents.has(r.studentId)).length === 0 && (
                    <tr><td className="text-muted" colSpan={data.homeworks.length + 3}>Нет студентов по выбранным фильтрам</td></tr>
                  )}
                  {data.rows.filter(r => !taggedStudents || taggedStudents.has(r.studentId)).map(r => (
                    <tr key={`${r.studentId}:${r.courseId}`}>
                      <td>
                        <div className="row gap-10">
                          <span className="avatar is-small">{initials(`${r.lastName} ${r.firstName}`)}</span>
                          <b>{r.lastName} {r.firstName}</b>
                        </div>
                      </td>
                      <td className="text-muted">{r.subgroupName}</td>
                      {data.homeworks.map(h => {
                        const cell = r.cells.find(c => c.homeworkId === h.id);
                        return (
                          <td data-code key={h.id}>
                            {cell ? <Cell cell={cell} onOpen={() => openWork(h.id, r)} /> : <span className="status-text is-off">—</span>}
                          </td>
                        );
                      })}
                      <td className="col-total">{r.average === null ? '—' : score(r.average)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </Async>
    </>
  );
}

function homeworkHint(h: GradebookHomework) {
  return `${h.title}\nДедлайн: ${dateTime(h.deadline)}\nСдано: ${h.submittedCount} из ${h.assignedCount}, проверено: ${h.reviewedCount}`;
}

/** Ячейка по статусу (CellStatus); со сдачей — кнопка, ведущая на экран проверки (П-8.5). */
function Cell({ cell, onOpen }: { cell: GradebookCell; onOpen: () => void }) {
  const marks = (
    <>
      {cell.status === 'ReviewedWithUnsentChanges' && <span className="cell-dot" title="Есть неотправленные правки" />}
      {cell.isLate && <span className="cell-flag" title="Сдано с опозданием">опозд.</span>}
      {cell.versions > 1 && <span className="cell-version" title={`Версий: ${cell.versions}`}>v{cell.versions}</span>}
    </>
  );
  const open = (content: React.ReactNode, title: string) => (
    <span className="cell-marks">
      <button className="score-link" onClick={onOpen} title={title} type="button">{content}<IconOpen /></button>
      {marks}
    </span>
  );

  switch (cell.status) {
    case 'Reviewed':
    case 'ReviewedWithUnsentChanges': {
      const by = cell.reviewedBy ? `Проверил(а): ${cell.reviewedBy}` : 'Открыть работу';
      const hint = cell.status === 'ReviewedWithUnsentChanges' ? `${by}\nЕсть неотправленные правки` : by;
      return open(cell.score === null ? <span className="status-text">Без оценки</span> : score(cell.score), hint);
    }
    case 'OnReview':
      return open(<span className="status-text">На проверке</span>, 'Открыть работу');
    case 'Overdue':
      return <span className="status-text is-overdue">Просрочено</span>;
    case 'NotSubmitted':
      return <span className="status-text is-off">Не сдано</span>;
    case 'NotAssigned':
      return <span className="status-text is-off" title="Задание не назначено студенту">—</span>;
  }
}
