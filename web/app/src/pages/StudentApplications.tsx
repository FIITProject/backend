import { useMemo, useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import type { EnrollmentDto } from '../api/types';
import { useCourse } from '../course/CourseLayout';
import { useCounters } from '../layout/AppLayout';
import { initials, shortDateTime } from '../lib/format';
import css from '../styles/pages/students.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, Modal, useAction, useLoad } from '../ui/ui';
import { CheckFilter, useCourseCrumbs } from './StudentFilters';

const extraCss = `
@layer pages {
  .applications-toolbar { flex-wrap: wrap; }
}`;

/** student-applications.html — заявки на вступление (П-5.4). */
export default function StudentApplications() {
  const { course, path, readOnly, reload: reloadCourse } = useCourse();
  useCourseCrumbs('Студенты');
  const counters = useCounters();
  const { run, busy } = useAction();

  const courseIds = course.courses.map(c => c.id);
  const list = useLoad(() => api.enrollments({ courseIds, status: 'Pending' }), [courseIds.join(',')]);

  const [subgroups, setSubgroups] = useState<Set<string> | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [rejecting, setRejecting] = useState<EnrollmentDto[] | null>(null);
  const [reason, setReason] = useState('');

  const archivedCourse = (courseId: number) => course.courses.find(c => c.id === courseId)?.isArchived ?? true;
  const subgroupOptions = course.courses.map(c => ({ value: String(c.id), label: c.subgroupName }));
  const visible = useMemo(
    () => (list.data ?? []).filter(e => !subgroups || subgroups.has(String(e.courseId))),
    [list.data, subgroups],
  );
  const actionable = (e: EnrollmentDto) => !readOnly && !archivedCourse(e.courseId);
  const selectable = visible.filter(actionable);
  // Выбранные считаем только среди видимых: «подтвердить выбранные в текущем фильтре»
  const chosen = selectable.filter(e => selected.has(e.id));
  const allChosen = selectable.length > 0 && chosen.length === selectable.length;
  const toggle = (id: number) => setSelected(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  const toggleAll = () => setSelected(allChosen ? new Set() : new Set(selectable.map(e => e.id)));

  function afterDecision(ids: number[]) {
    setSelected(s => { const n = new Set(s); ids.forEach(id => n.delete(id)); return n; });
    list.reload();
    reloadCourse();
    counters.refresh();
  }

  async function approve(items: EnrollmentDto[]) {
    const ids = items.map(e => e.id);
    const done = await run(() => api.approve(ids).then(() => true),
      ids.length === 1 ? 'Заявка подтверждена' : `Подтверждено заявок: ${ids.length}`);
    if (done) afterDecision(ids);
  }

  function askReject(items: EnrollmentDto[]) {
    setReason('');
    setRejecting(items);
  }

  async function reject() {
    if (!rejecting) return;
    const ids = rejecting.map(e => e.id);
    const done = await run(() => api.reject(ids, reason.trim() || null).then(() => true),
      ids.length === 1 ? 'Заявка отклонена' : `Отклонено заявок: ${ids.length}`);
    if (done) {
      setRejecting(null);
      afterDecision(ids);
    }
  }

  const filterText = subgroups === null ? 'Все группы' : `Групп: ${subgroups.size}`;
  return (
    <>
      <PageStyle css={[css, extraCss]} />
      <div className="row gap-12 applications-toolbar">
        <div className="segmented w-440">
          <span className="is-active">Заявки {list.data?.length ?? course.pending}</span>
          <Link to={`${path}/students`}>Студенты {course.students}</Link>
        </div>
        <span className="spacer" />
        <CheckFilter label="Фильтр по группе" onChange={setSubgroups} options={subgroupOptions} pillText={filterText} selected={subgroups} variant="pill" />
        {!readOnly && (
          <>
            <button className="btn-primary" disabled={busy || chosen.length === 0} onClick={() => approve(chosen)} type="button">
              Подтвердить выбранные{chosen.length > 0 ? ` (${chosen.length})` : ''}
            </button>
          </>
        )}
      </div>
      <Async state={list}>
        {data => data.length === 0
          ? <Empty>Новых заявок нет</Empty>
          : (
            <div className="card p-8-0">
              <table>
                <thead>
                  <tr>
                    {!readOnly && (
                      <th className="w-56">
                        <input aria-label="Выбрать все заявки" checked={allChosen} disabled={selectable.length === 0} onChange={toggleAll}
                          ref={el => { if (el) el.indeterminate = !allChosen && chosen.length > 0; }} title="Выбрать все заявки" type="checkbox" />
                      </th>
                    )}
                    <th>Студент</th>
                    <th>Telegram</th>
                    <th>Группа</th>
                    <th>Заявка</th>
                    {!readOnly && <th />}
                  </tr>
                </thead>
                <tbody>
                  {visible.length === 0 && (
                    <tr><td className="text-muted" colSpan={readOnly ? 4 : 6}>Нет заявок в выбранных группах</td></tr>
                  )}
                  {visible.map(e => (
                    <tr key={e.id}>
                      {!readOnly && (
                        <td>{actionable(e) && <input aria-label="Выбрать" checked={selected.has(e.id)} onChange={() => toggle(e.id)} type="checkbox" />}</td>
                      )}
                      <td>
                        <div className="row gap-10">
                          <span className="avatar is-small">{initials(`${e.lastName} ${e.firstName}`)}</span>
                          <b>{e.lastName} {e.firstName}</b>
                        </div>
                      </td>
                      <td className="text-muted">{e.telegramUsername ? `@${e.telegramUsername}` : 'не привязан'}</td>
                      <td className="text-muted">{e.courseName}</td>
                      <td className="text-muted">{shortDateTime(e.createdAt)}</td>
                      {!readOnly && (
                        <td className="text-right">
                          {actionable(e) && (
                            <>
                              <button className="btn-sm" disabled={busy} onClick={() => approve([e])} type="button">Подтвердить</button>{' '}
                              <button className="btn-sm is-outline" disabled={busy} onClick={() => askReject([e])} type="button">Отклонить</button>
                            </>
                          )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </Async>
      <Modal open={!!rejecting} onClose={() => setRejecting(null)}
        title={rejecting?.length === 1 ? 'Отклонить заявку' : `Отклонить заявки: ${rejecting?.length ?? 0}`}
        actions={<>
          <button className="btn-outline" onClick={() => setRejecting(null)} type="button">Отмена</button>
          <button className="btn-primary btn-danger-soft" disabled={busy} onClick={reject} type="button">Отклонить</button>
        </>}>
        {rejecting?.length === 1 && (
          <div className="modal__text">{rejecting[0].lastName} {rejecting[0].firstName}, {rejecting[0].courseName}</div>
        )}
        <div className="modal__body">
          <label>Причина (необязательно)
            <textarea autoFocus className="input h-100 p-12 fw-500" maxLength={500} onChange={e => setReason(e.target.value)}
              placeholder="Например: вы не из этой группы" value={reason} />
          </label>
        </div>
        <div className="modal__hint">Причину увидит студент в сообщении от бота.</div>
      </Modal>
    </>
  );
}
