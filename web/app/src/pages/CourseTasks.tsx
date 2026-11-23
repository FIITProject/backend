import { useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import type { HomeworkDto } from '../api/types';
import { useCourse, useHeroAction } from '../course/CourseLayout';
import { useCrumbs } from '../layout/AppLayout';
import { semesterName, shortDateTime } from '../lib/format';
import css from '../styles/pages/course-task.css?inline';
import { IconDoc } from '../ui/icons';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, useLoad } from '../ui/ui';
import {
  codePrefixes, FilterMenu, IconChevronUp, IconEdit, IconFilter, IconGo, maxPoints, parseTitle, passes, pointsText, tagClass,
} from './tasks/taskCommon';

const NO_CODE = '';

/** course-task.html — вкладка «Задания» курса: черновики и опубликованные ДЗ. */
export default function CourseTasks() {
  const { course, subjectId, semester, path, readOnly } = useCourse();
  useHeroAction(readOnly ? null : <Link className="btn-dark" to={`${path}/tasks/new`}>+ Создать задание</Link>, [readOnly, path]);
  useCrumbs([
    course.isArchived ? { label: 'Архив', to: '/archive' } : { label: 'Курсы', to: '/courses' },
    { label: `${course.subjectName}, ${semesterName(course.semester)}` },
  ]);
  const state = useLoad(() => api.homeworks(subjectId, semester), [subjectId, semester.year, semester.term]);

  return (
    <>
      <PageStyle css={css} />
      <Async state={state}>
        {homeworks => homeworks.length === 0
          ? (
            <Empty>
              Заданий пока нет.{' '}
              {!readOnly && <Link className="link-brand" to={`${path}/tasks/new`}>Создать задание</Link>}
            </Empty>
          )
          : <TaskLists homeworks={homeworks} path={path} />}
      </Async>
    </>
  );
}

function TaskLists({ homeworks, path }: { homeworks: HomeworkDto[]; path: string }) {
  const drafts = homeworks.filter(h => !h.isPublished);
  const published = homeworks.filter(h => h.isPublished);
  const prefixes = codePrefixes(homeworks.map(h => h.title));
  const hasNoCode = published.some(h => !parseTitle(h.title).prefix);
  const filterValues = [...prefixes, ...(hasNoCode && prefixes.length ? [NO_CODE] : [])];
  const [checked, setChecked] = useState<string[]>(filterValues);
  const [draftsOpen, setDraftsOpen] = useState(false);
  const filtered = checked.length !== filterValues.length;
  const rows = published.filter(h => passes(checked, filterValues, parseTitle(h.title).prefix ?? NO_CODE));

  return (
    <>
      {(
        <div className="card p-0 overflow-hidden bg-bg-dashed shadow-none card--x1">
          <div className="row p-18-24 gap-14">
            <span className="icon-btn w-42 h-42 bg-surface text-brand-strong icon-btn--x1"><IconDoc height={18} width={18} /></span>
            <div className="flex-1">
              <h2 className="fs-18">Черновики</h2>
              <div className="text-muted fs-13 mt-2">Неопубликованные задания, их видят только преподаватели курса</div>
            </div>
            <button aria-controls="drafts-list" aria-expanded={draftsOpen} aria-label="Свернуть или развернуть черновики" className="chevron-btn"
              onClick={() => setDraftsOpen(o => !o)} type="button">
              <IconChevronUp />
            </button>
          </div>
          {draftsOpen && (
            <div id="drafts-list">
              {drafts.length === 0 && <div className="row p-12-24 gap-14 row--x1 text-muted">Черновиков нет</div>}
              {drafts.map(h => {
                const t = parseTitle(h.title);
                const points = maxPoints(h.subtasks);
                return (
                  <div className="row p-12-24 gap-14 row--x1" key={h.id}>
                    {t.code && <span className={tagClass(t.prefix, prefixes)}>{t.code}</span>}
                    <div className="flex-1"><b>{t.name}</b>
                      <div className="text-muted fs-12 mt-2">
                        {[points !== null && pointsText(points), `дедлайн ${shortDateTime(h.deadline)}`, 'не опубликовано'].filter(Boolean).join(' · ')}
                      </div>
                    </div>
                    <Link className="btn-go is-ghost" to={`${path}/tasks/${h.id}/edit`}>Редактировать<IconEdit /></Link>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
      {published.length === 0
        ? <Empty>Опубликованных заданий пока нет</Empty>
        : (
          <div className="card p-6-0 overflow-hidden">
            <table className="data-table">
              <thead>
                <tr>
                  <th className={filtered ? 'is-filtered-col' : undefined}>
                    <span className="th-code">Код
                      {prefixes.length > 0 && (
                        <FilterMenu checked={checked} onChange={setChecked}
                          options={filterValues.map(v => ({ value: v, label: v === NO_CODE ? 'Без кода' : v }))}
                          trigger={({ open, toggle }) => (
                            <button aria-expanded={open} aria-haspopup="true" aria-label="Фильтр по коду" onClick={toggle} title="Фильтр по коду" type="button"
                              className={`filter-btn${filtered ? ' is-filtered' : ''}`}>
                              <IconFilter />
                            </button>
                          )} />
                      )}
                    </span>
                  </th>
                  <th>Название</th>
                  <th>Дедлайн</th>
                  <th>Работы</th>
                  <th>В работу</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(h => {
                  const t = parseTitle(h.title);
                  const points = maxPoints(h.subtasks);
                  return (
                    <tr key={h.id}>
                      <td>{t.code ? <span className={tagClass(t.prefix, prefixes)}>{t.code}</span> : <span className="text-muted">—</span>}</td>
                      <td><b>{t.name}</b>
                        {points !== null && <div className="text-muted fs-12 mt-2">{pointsText(points)}</div>}
                      </td>
                      <td>{shortDateTime(h.deadline)}</td>
                      <td>
                        <div>Сдано: <b>{h.submittedCount}/{h.recipientsCount}</b></div>
                        <div className="text-muted mt-2">Проверено: <b className="text-ink">{h.reviewedCount}</b></div>
                      </td>
                      <td className="text-right"><Link className="btn-go" to={`${path}/tasks/${h.id}`}>Перейти<IconGo /></Link></td>
                    </tr>
                  );
                })}
                {rows.length === 0 && (
                  <tr><td className="text-muted" colSpan={5}>Нет заданий с выбранными кодами</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}
    </>
  );
}
