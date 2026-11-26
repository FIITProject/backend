import { useMemo, useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import type { EnrollmentDto, TagDto } from '../api/types';
import { useCourse } from '../course/CourseLayout';
import { initials } from '../lib/format';
import css from '../styles/pages/students.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, Modal, useAction, useConfirm, useLoad } from '../ui/ui';
import { CheckFilter, IconSearch, norm, useCourseCrumbs } from './StudentFilters';

const NO_TAG = '—';

// Значок «бот заблокирован», выделение строк и окно меток — нет в дизайне, стили минимальные.
const extraCss = `
@layer pages {
  .students-toolbar { flex-wrap: wrap; }
  .tag-chips { display: flex; flex-wrap: wrap; gap: 6px; }
  .bot-blocked { display: inline-grid; place-items: center; color: var(--text-danger); }
  .tag-options { display: flex; flex-direction: column; gap: 4px; max-height: 40vh; overflow-y: auto; }
  .tag-options .dropdown__option { font-weight: 700; }
}`;

const IconPen = () => (
  <svg aria-hidden="true" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.9" viewBox="0 0 24 24" width="18">
    <path d="M4 20l4-1 11-11-3-3L5 16z" />
    <path d="M14 6l3 3" />
  </svg>
);

const IconTrash = () => (
  <svg aria-hidden="true" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.9" viewBox="0 0 24 24" width="18">
    <path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13" />
  </svg>
);

const IconBotBlocked = () => (
  <svg aria-hidden="true" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.9" viewBox="0 0 24 24" width="18">
    <circle cx="12" cy="12" r="9" />
    <path d="M5.6 5.6l12.8 12.8" />
  </svg>
);

/** students.html — подтверждённые студенты курса (П-5), метки (П-6.3). */
export default function Students() {
  const { course, path, readOnly, reload: reloadCourse } = useCourse();
  useCourseCrumbs('Студенты');
  const { run } = useAction();
  const { confirm, dialog } = useConfirm();

  const courseIds = course.courses.map(c => c.id);
  const list = useLoad(() => api.enrollments({ courseIds, status: 'Approved' }), [courseIds.join(',')]);
  const tags = useLoad(() => api.tags(), []);

  const [query, setQuery] = useState('');
  const [subgroups, setSubgroups] = useState<Set<string> | null>(null);
  const [tagFilter, setTagFilter] = useState<Set<string> | null>(null);
  const [tagging, setTagging] = useState<EnrollmentDto[] | null>(null);

  const archivedCourse = (courseId: number) => course.courses.find(c => c.id === courseId)?.isArchived ?? true;
  const subgroupOptions = course.courses.map(c => ({ value: String(c.id), label: c.subgroupName }));
  const tagOptions = [...(tags.data ?? []).map(t => ({ value: String(t.id), label: t.name })), { value: NO_TAG, label: 'Без метки' }];

  const visible = useMemo(() => {
    const q = norm(query.trim());
    const qNick = q.replace(/^@/, '');
    return (list.data ?? []).filter(e => {
      if (subgroups && !subgroups.has(String(e.courseId))) return false;
      if (tagFilter) {
        const keys = e.tags.length ? e.tags.map(t => String(t.id)) : [NO_TAG];
        if (!keys.some(k => tagFilter.has(k))) return false;
      }
      if (!q) return true;
      return norm(`${e.lastName} ${e.firstName}`).includes(q)
        || (!!e.telegramUsername && norm(e.telegramUsername).includes(qNick))
        || e.tags.some(t => norm(t.name).includes(q));
    });
  }, [list.data, query, subgroups, tagFilter]);

  const editable = (e: EnrollmentDto) => !readOnly && !archivedCourse(e.courseId);

  async function remove(e: EnrollmentDto) {
    const ok = await confirm({
      title: 'Удаление студента',
      text: `Удалить ${e.lastName} ${e.firstName} из ${e.courseName} по предмету «${e.subjectName}»?`,
      ok: 'Удалить',
      danger: true,
    });
    if (!ok) return;
    const done = await run(() => api.removeEnrollment(e.id).then(() => true), 'Студент удалён из курса');
    if (done) {
      list.reload();
      reloadCourse();
    }
  }

  const pending = course.pending;
  return (
    <>
      <PageStyle css={[css, extraCss]} />
      {dialog}
      <div className="row gap-12 students-toolbar">
        <div className="segmented w-440">
          <Link to={`${path}/students/applications`}>Заявки {pending}</Link>
          <span className="is-active">Студенты {list.data?.length ?? course.students}</span>
        </div>
        <span className="spacer" />
        <div className="pos-relative search-field">
          <input className="input pl-50" onChange={e => setQuery(e.target.value)} placeholder="Поиск по студенту, нику или метке" type="search" value={query} />
          <span className="pos-absolute left-18 top-16 text-ink-subtle"><IconSearch /></span>
        </div>
      </div>
      <Async state={list}>
        {data => data.length === 0
          ? <Empty>В курсе пока нет студентов. Пригласите их по <Link to={`${path}/invites`}>ссылке-приглашению</Link></Empty>
          : (
            <div className="card p-8-0">
              <table>
                <thead>
                  <tr>
                    <th>Студент</th>
                    <th>Telegram</th>
                    <th className={subgroups ? 'is-filtered-col' : undefined}>
                      <span className="th-filter">Группа
                        <CheckFilter label="Фильтр по группе" options={subgroupOptions} selected={subgroups} onChange={setSubgroups} />
                      </span>
                    </th>
                    <th className={tagFilter ? 'is-filtered-col' : undefined}>
                      <span className="th-filter">Метка
                        <CheckFilter label="Фильтр по метке" options={tagOptions} selected={tagFilter} onChange={setTagFilter} />
                      </span>
                    </th>
                    {!readOnly && <th />}
                  </tr>
                </thead>
                <tbody>
                  {visible.length === 0 && (
                    <tr><td className="text-muted" colSpan={readOnly ? 4 : 5}>Никого не нашли — измените поиск или фильтры</td></tr>
                  )}
                  {visible.map(e => (
                    <tr key={e.id}>
                      <td>
                        <div className="row gap-10">
                          <span className="avatar is-small">{initials(`${e.lastName} ${e.firstName}`)}</span>
                          <b>{e.lastName} {e.firstName}</b>
                          {e.botBlocked && (
                            <span className="bot-blocked" title="Студент заблокировал бота — уведомления до него не доходят"><IconBotBlocked /></span>
                          )}
                        </div>
                      </td>
                      <td className="text-muted">{e.telegramUsername ? `@${e.telegramUsername}` : 'не привязан'}</td>
                      <td className="text-muted">{e.courseName}</td>
                      <td>
                        {e.tags.length
                          ? <div className="tag-chips">{e.tags.map(t => <span className="chip chip--brand" key={t.id}>{t.name}</span>)}</div>
                          : <span className="text-muted">—</span>}
                      </td>
                      {!readOnly && (
                        <td className="text-right">
                          {editable(e) && (
                            <div className="row justify-end gap-8">
                              <button aria-label="Метки" className="icon-btn icon-btn--edit w-38 h-38" onClick={() => setTagging([e])} title="Метки" type="button"><IconPen /></button>
                              <button aria-label="Удалить" className="icon-btn icon-btn--delete w-38 h-38" onClick={() => remove(e)} title="Удалить из курса" type="button"><IconTrash /></button>
                            </div>
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
      {tagging && (
        <TagsModal
          students={tagging}
          tags={tags.data ?? []}
          onClose={() => setTagging(null)}
          onSaved={() => { setTagging(null); list.reload(); tags.reload(); }} />
      )}
    </>
  );
}

type TagState = 'on' | 'off' | 'mixed';

/** Метки одного или нескольких студентов (П-6.3): повесить/снять, создать новую. */
function TagsModal({ students, tags, onClose, onSaved }: {
  students: EnrollmentDto[]; tags: TagDto[]; onClose: () => void; onSaved: () => void;
}) {
  const { run, busy } = useAction();
  const studentIds = [...new Set(students.map(s => s.studentId))];
  const initial = (tagId: number): TagState => {
    const n = students.filter(s => s.tags.some(t => t.id === tagId)).length;
    return n === 0 ? 'off' : n === students.length ? 'on' : 'mixed';
  };
  const [allTags, setAllTags] = useState(tags);
  const [state, setState] = useState<Record<number, TagState>>(() => Object.fromEntries(tags.map(t => [t.id, initial(t.id)])));
  const [newTag, setNewTag] = useState('');

  async function create() {
    const name = newTag.trim();
    if (!name) return;
    const tag = await run(() => api.createTag(name));
    if (!tag) return;
    setAllTags(ts => [...ts, tag].sort((a, b) => a.name.localeCompare(b.name, 'ru')));
    setState(s => ({ ...s, [tag.id]: 'on' }));
    setNewTag('');
  }

  async function save() {
    const changed = allTags.filter(t => state[t.id] !== 'mixed' && state[t.id] !== initial(t.id));
    const ok = await run(async () => {
      for (const t of changed) {
        if (state[t.id] === 'on') await api.tagStudents(t.id, studentIds);
        else await api.untagStudents(t.id, studentIds);
      }
      return true;
    }, changed.length ? 'Метки сохранены' : undefined);
    if (ok) onSaved();
  }

  const title = students.length === 1 ? `Метки: ${students[0].lastName} ${students[0].firstName}` : `Метки для ${students.length} студентов`;
  return (
    <Modal open onClose={onClose} title={title}
      actions={<>
        <button className="btn-outline" onClick={onClose} type="button">Отмена</button>
        <button className="btn-primary" disabled={busy} onClick={save} type="button">Сохранить</button>
      </>}>
      <div className="modal__body">
        {allTags.length === 0
          ? <div className="modal__hint">Меток пока нет — создайте первую. Метки видите только вы.</div>
          : (
            <div className="tag-options">
              {allTags.map(t => (
                <label className="dropdown__option" key={t.id}>
                  <input checked={state[t.id] === 'on'} type="checkbox"
                    ref={el => { if (el) el.indeterminate = state[t.id] === 'mixed'; }}
                    onChange={() => setState(s => ({ ...s, [t.id]: s[t.id] === 'on' ? 'off' : 'on' }))} />
                  {t.name}
                </label>
              ))}
            </div>
          )}
        <div className="row gap-8">
          <input className="input rounded-999 flex-1" maxLength={100} onChange={e => setNewTag(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); create(); } }} placeholder="Новая метка, например «Староста»" value={newTag} />
          <button className="btn-outline" disabled={busy || !newTag.trim()} onClick={create} type="button">Создать</button>
        </div>
      </div>
    </Modal>
  );
}
