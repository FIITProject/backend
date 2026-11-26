import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api } from '../api/api';
import { useCourse } from '../course/CourseLayout';
import { plural } from '../lib/format';
import css from '../styles/pages/settings.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { errorText, Modal, useAction, useToast } from '../ui/ui';
import { useCourseCrumbs } from './course-admin/common';

/** «ФТ-201 2» → группа ФТ-201, подгруппа 2; «ФТ-201-2» и «ФТ-201» сервер разбирает сам. */
function parseGroup(value: string): { group: string; subgroup: number | null } {
  const m = /^(.+?)\s+(\d{1,2})$/.exec(value.trim());
  return m ? { group: m[1], subgroup: Number(m[2]) } : { group: value.trim(), subgroup: null };
}

/**
 * settings.html — настройки курса (П-2): группы (подгруппы) курса и архив.
 * Карточку «Google Таблица» не переносим — такой интеграции нет; её место в сетке пустое, чтобы раскладка совпадала с дизайном.
 */
export default function Settings() {
  const { course, subjectId, semester, readOnly, reload, path } = useCourse();
  useCourseCrumbs(course, path, { label: 'Настройки' });
  const toast = useToast();
  const navigate = useNavigate();
  const { run, busy } = useAction();
  const [editing, setEditing] = useState(false);
  const [group, setGroup] = useState('');
  const [selected, setSelected] = useState<number[]>([]);
  const [archiveOpen, setArchiveOpen] = useState(false);

  const subgroups = course.courses.filter(c => !c.isArchived);
  const shown = readOnly ? course.courses : subgroups;
  const pending = subgroups.reduce((s, c) => s + c.pendingCount, 0);

  function setEdit(on: boolean) {
    setEditing(on);
    setSelected([]);
    setGroup('');
  }

  async function addGroup() {
    if (!group.trim()) return;
    const created = await run(() => api.createCourse({ subjectId, semester, ...parseGroup(group) }), 'Группа добавлена');
    if (created) {
      setGroup('');
      reload();
    }
  }

  async function deleteSelected() {
    // Удалить можно только пустую подгруппу и только ведущему — сервер скажет, если нельзя
    const errors: string[] = [];
    for (const id of selected) {
      try {
        await api.deleteCourse(id);
      } catch (e) {
        errors.push(`${course.courses.find(c => c.id === id)?.subgroupName}: ${errorText(e)}`);
      }
    }
    if (errors.length) toast(errors.join('; '), { error: true });
    else toast(selected.length > 1 ? 'Группы удалены' : 'Группа удалена');
    setSelected([]);
    if (errors.length < selected.length && selected.length === subgroups.length) navigate('/courses');
    else reload();
  }

  async function archive() {
    setArchiveOpen(false);
    const done = await run(() => api.archiveCourses(subgroups.map(c => c.id)).then(() => true), 'Курс отправлен в архив');
    if (done) navigate('/archive');
  }

  async function unarchive() {
    const done = await run(() => api.unarchiveCourses(course.courses.map(c => c.id)).then(() => true), 'Курс возвращён из архива');
    if (done) reload();
  }

  const toggle = (id: number) => setSelected(s => (s.includes(id) ? s.filter(x => x !== id) : [...s, id]));

  return (
    <div className="d-grid grid-cols-2 gap-20">
      <PageStyle css={css} />
      <div className="card card-section">
        <div className="row">
          <h2>Группы курса</h2>
          <span className="spacer" />
          {!readOnly && !editing && (
            <button className="btn-sm is-outline course-groups__edit" onClick={() => setEdit(true)} type="button">Изменить</button>
          )}
          {editing && <button className="btn-sm" onClick={() => setEdit(false)} type="button">Сохранить</button>}
        </div>
        {editing && (
          <>
            <div className="combo-field">
              <input aria-label="Добавить группу" className="combo-field__input" onChange={e => setGroup(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); addGroup(); } }} placeholder="Добавить группу" value={group} />
              <span aria-hidden="true" className="combo-field__divider" />
              <button className={`combo-field__button${group.trim() ? ' is-filled' : ''}`} disabled={busy} onClick={addGroup} type="button">Добавить</button>
            </div>
            <div className="text-muted fs-13">Выберите группу для удаления или добавьте новую, введя её.</div>
          </>
        )}
        <div className={`row gap-8 flex-wrap${editing ? ' is-editable' : ''}`}>
          {shown.map(c => (
            <span className={`course-group${selected.includes(c.id) ? ' is-selected' : ''}`} key={c.id}
              title={`Ведущий ${c.adminTeacherName}; ${c.approvedCount} ${plural(c.approvedCount, 'студент', 'студента', 'студентов')}`}
              {...(editing ? {
                role: 'button', tabIndex: 0, onClick: () => toggle(c.id),
                onKeyDown: (e: React.KeyboardEvent) => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggle(c.id); } },
              } : {})}>
              {c.subgroupName}
            </span>
          ))}
        </div>
        {editing && selected.length > 0 && (
          <div><button className="btn-sm is-outline" disabled={busy} onClick={deleteSelected} type="button">Удалить выбранные</button></div>
        )}
      </div>
      <div aria-hidden="true" />
      <div className="card card-section">
        <h2>Архив</h2>
        {readOnly
          ? (
            <>
              <div className="text-muted">Курс в архиве: доступен только для чтения, студенты видят его задания в разделе «Завершённые»</div>
              <div><button className="btn-outline" disabled={busy} onClick={unarchive} type="button">Вернуть из архива</button></div>
            </>
          )
          : (
            <>
              <div className="text-muted">Курс уйдёт в архив учебного года, студенты и работы сохранятся</div>
              <div><button className="btn-outline" onClick={() => setArchiveOpen(true)} type="button">Отправить в архив</button></div>
            </>
          )}
      </div>
      <Modal open={archiveOpen} title="Отправить в архив" onClose={() => setArchiveOpen(false)} size="narrow"
        actions={<>
          <button className="btn-outline" onClick={() => setArchiveOpen(false)} type="button">Отмена</button>
          <button className="btn-primary btn-danger-soft" onClick={archive} type="button">Подтвердить</button>
        </>}>
        <div className="modal__text">
          Вы уверены, что хотите перенести курс в архив?
          {pending > 0 && ` Ожидающие заявки (${pending}) будут отклонены с причиной «Набор закрыт».`}
        </div>
      </Modal>
    </div>
  );
}
