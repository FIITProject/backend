import { useMemo, useState } from 'react';
import { api } from '../api/api';
import type { CourseAccessDto, TeacherDto, TempPasswordResponse } from '../api/types';
import { useMe } from '../auth/session';
import { useCourse } from '../course/CourseLayout';
import { initials, shortName } from '../lib/format';
import css from '../styles/pages/teachers.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, errorText, type Loaded, Modal, useAction, useConfirm, useLoad, useToast } from '../ui/ui';
import { CopyButton, IconSearch, IconTrash, useCourseCrumbs } from './course-admin/common';

interface Row {
  teacherId: number;
  fullName: string;
  login: string;
  /** Ведущий хотя бы одной подгруппы курса — в дизайне «Создатель». */
  isLead: boolean;
  /** Кто выдал доступ: выдаёт только ведущий подгруппы (П-3). */
  addedBy: string | null;
  /** Подгруппы, где у преподавателя доступ и где я ведущий, — из них я могу его убрать. */
  revocable: number[];
}

/**
 * teachers.html — преподаватели курса (П-3, П-10). Как в дизайне: поиск по логину и таблица.
 * Логина нет в сервисе — предлагаем создать аккаунт коллеге (временный пароль показывается один раз).
 * Клик по имени коллеги — сброс пароля и отключение аккаунта.
 */
export default function Teachers() {
  const me = useMe();
  const { course, path, readOnly } = useCourse();
  useCourseCrumbs(course, path, { label: 'Преподаватели' });
  const toast = useToast();
  const { run, busy } = useAction();
  const { confirm, dialog } = useConfirm();
  const [login, setLogin] = useState('');
  const [flash, setFlash] = useState<number | null>(null);
  const [create, setCreate] = useState<{ login: string; fullName: string } | null>(null);
  const [password, setPassword] = useState<TempPasswordResponse | null>(null);
  const [manage, setManage] = useState<TeacherDto | null>(null);

  const myLead = course.courses.filter(c => c.isAdmin && !c.isArchived);
  const teachers = useLoad(() => api.teachers(), []);
  const access = useLoad(async () => {
    const lists = await Promise.all(course.courses.map(c => api.courseAccess(c.id)));
    return new Map(course.courses.map((c, i) => [c.id, lists[i]] as [number, CourseAccessDto[]]));
  }, [course.key]);

  const rows = useMemo<Row[] | undefined>(() => {
    if (!access.data) return undefined;
    const map = new Map<number, Row>();
    const row = (id: number, fullName: string, login: string) => {
      let r = map.get(id);
      if (!r) map.set(id, r = { teacherId: id, fullName, login, isLead: false, addedBy: null, revocable: [] });
      return r;
    };
    const loginOf = (id: number) => teachers.data?.find(t => t.id === id)?.login ?? '';
    for (const c of course.courses) {
      row(c.adminTeacherId, c.adminTeacherName, loginOf(c.adminTeacherId)).isLead = true;
      for (const a of access.data.get(c.id) ?? []) {
        const r = row(a.teacherId, a.fullName, a.login);
        r.addedBy ??= shortName(c.adminTeacherName);
        if (c.isAdmin && !c.isArchived) r.revocable.push(c.id);
      }
    }
    return [...map.values()].sort((a, b) => Number(b.isLead) - Number(a.isLead) || a.fullName.localeCompare(b.fullName, 'ru'));
  }, [access.data, teachers.data, course]);

  async function grant(teacherId: number) {
    const ok = await run(async () => {
      for (const c of myLead) await api.grantAccess(c.id, teacherId);
      return true;
    }, 'Доступ к курсу выдан');
    if (ok) access.reload();
  }

  async function add() {
    const value = login.trim().replace(/^@/, '');
    if (!value) return;
    const existing = rows?.find(r => r.login.toLowerCase() === value.toLowerCase());
    if (existing) {
      setFlash(null);
      requestAnimationFrame(() => setFlash(existing.teacherId));
      return;
    }
    if (myLead.length === 0) {
      toast('Выдавать доступ к курсу может только ведущий преподаватель', { error: true });
      return;
    }
    const teacher = teachers.data?.find(t => t.login.toLowerCase() === value.toLowerCase());
    if (!teacher) {
      setCreate({ login: value, fullName: '' });
      return;
    }
    if (teacher.isDisabled) {
      toast('Аккаунт этого преподавателя отключён', { error: true });
      return;
    }
    await grant(teacher.id);
    setLogin('');
  }

  async function createTeacher() {
    if (!create || !create.fullName.trim()) return;
    try {
      const created = await api.addTeacher(create.fullName.trim(), create.login);
      setCreate(null);
      setLogin('');
      teachers.reload();
      await grant(created.teacherId);
      setPassword(created);
    } catch (e) {
      toast(errorText(e), { error: true });
    }
  }

  async function remove(r: Row) {
    const ok = await confirm({
      title: 'Удаление преподавателя',
      text: 'Вы уверены, что хотите удалить преподавателя из текущего курса?',
      ok: 'Удалить',
      danger: true,
    });
    if (!ok) return;
    const done = await run(async () => {
      for (const id of r.revocable) await api.revokeAccess(id, r.teacherId);
      return true;
    }, 'Преподаватель удалён из курса');
    if (done) access.reload();
  }

  async function resetPassword(t: TeacherDto) {
    const r = await run(() => api.resetTeacherPassword(t.id));
    if (r) {
      setManage(null);
      setPassword(r);
    }
  }

  async function toggleDisabled(t: TeacherDto) {
    const ok = t.isDisabled || await confirm({
      title: 'Отключить преподавателя',
      text: `${t.fullName} не сможет войти, его сессии завершатся. Включить аккаунт обратно можно здесь же.`,
      ok: 'Отключить',
      danger: true,
    });
    if (!ok) return;
    const done = await run(() => (t.isDisabled ? api.enableTeacher(t.id) : api.disableTeacher(t.id)).then(() => true),
      t.isDisabled ? 'Аккаунт включён' : 'Аккаунт отключён');
    if (done) {
      setManage(null);
      teachers.reload();
    }
  }

  return (
    <>
      <PageStyle css={css} />
      {!readOnly && (
        <div className="teacher-search" role="search">
          <span aria-hidden="true" className="teacher-search__icon"><IconSearch /></span>
          <input aria-label="Логин преподавателя" className="teacher-search__input combo-field__input" list="teacher-logins"
            onChange={e => setLogin(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }}
            placeholder="Введите логин преподавателя, чтобы пригласить его на курс" type="search" value={login} />
          <datalist id="teacher-logins">
            {(teachers.data ?? []).filter(t => !t.isDisabled && !rows?.some(r => r.teacherId === t.id)).map(t => (
              <option key={t.id} value={t.login}>{t.fullName}</option>
            ))}
          </datalist>
          <span aria-hidden="true" className="combo-field__divider" />
          <button className={`combo-field__button${login.trim() ? ' is-filled' : ''}`} disabled={busy} onClick={add} type="button">Добавить</button>
        </div>
      )}
      <Async state={{ ...access, data: rows, setData: () => {} } as Loaded<Row[]>}>
        {list => (
          <div className="card p-8-0">
            <table>
              <thead>
                <tr>
                  <th>Преподаватель</th>
                  <th>Логин</th>
                  <th>Роль</th>
                  <th>Добавил</th>
                  <th className="text-right">Действия</th>
                </tr>
              </thead>
              <tbody>
                {list.map(r => {
                  const account = teachers.data?.find(t => t.id === r.teacherId);
                  const canManage = !!account && account.id !== me.id;
                  return (
                    <tr className={flash === r.teacherId ? 'is-flash' : undefined} key={r.teacherId}>
                      <td>
                        <div className="row gap-10">
                          <span className="avatar is-small">{initials(r.fullName)}</span>
                          {canManage
                            ? <b className="cursor-pointer" onClick={() => setManage(account)} title="Сбросить пароль или отключить">{r.fullName}</b>
                            : <b>{r.fullName}</b>}
                        </div>
                      </td>
                      <td className="text-muted">{r.login || account?.login}</td>
                      <td><span className={r.isLead ? 'chip chip--brand' : 'chip chip--gray'}>{r.isLead ? 'Создатель' : 'Преподаватель'}</span></td>
                      <td className="text-muted">{r.isLead ? '—' : r.addedBy ?? '—'}</td>
                      <td className="text-right">
                        <div className="row justify-end gap-8">
                          {r.revocable.length > 0 && !r.isLead && (
                            <button aria-label="Удалить" className="icon-btn icon-btn--delete w-38 h-38" onClick={() => remove(r)} title="Удалить" type="button">
                              <IconTrash />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Async>

      <Modal open={!!create} title="Новый преподаватель" onClose={() => setCreate(null)}
        actions={<>
          <button className="btn-outline" onClick={() => setCreate(null)} type="button">Отмена</button>
          <button className="btn-primary" disabled={!create?.fullName.trim()} onClick={createTeacher} type="button">Создать и добавить</button>
        </>}>
        <div className="modal__text">
          Преподавателя с логином <b>{create?.login}</b> ещё нет в сервисе. Создайте ему аккаунт — он получит временный пароль и сменит его при первом входе.
        </div>
        <label className="d-flex flex-col gap-8 fw-700">ФИО
          <input autoFocus className="input" onChange={e => setCreate(c => c && { ...c, fullName: e.target.value })}
            onKeyDown={e => { if (e.key === 'Enter') createTeacher(); }} placeholder="Иванов Иван Иванович" value={create?.fullName ?? ''} />
        </label>
      </Modal>

      <Modal open={!!manage} title={manage?.fullName ?? ''} onClose={() => setManage(null)}
        actions={<>
          {manage?.canDisable && (
            <button className={manage.isDisabled ? 'btn-outline' : 'btn-primary btn-danger-soft'} disabled={busy} onClick={() => toggleDisabled(manage)} type="button">
              {manage.isDisabled ? 'Включить аккаунт' : 'Отключить аккаунт'}
            </button>
          )}
          <button className="btn-primary" disabled={busy} onClick={() => manage && resetPassword(manage)} type="button">Сбросить пароль</button>
        </>}>
        <div className="modal__text">
          Логин {manage?.login}{manage?.addedByName ? `, добавил(а) ${manage.addedByName}` : ''}{manage?.isDisabled ? '. Аккаунт отключён' : ''}.
          Сброс пароля выдаст новый временный пароль — передайте его коллеге.
        </div>
      </Modal>

      <Modal open={!!password} title="Временный пароль" onClose={() => setPassword(null)}
        actions={<button className="btn-primary" onClick={() => setPassword(null)} type="button">Готово</button>}>
        <div className="modal__text">
          Логин <b>{password?.login}</b>. Пароль показывается один раз — передайте его коллеге, при первом входе он его сменит.
        </div>
        <div className="row gap-12">
          <b className="fs-18">{password?.tempPassword}</b>
          <span className="spacer" />
          {password && <CopyButton text={password.tempPassword} />}
        </div>
      </Modal>
      {dialog}
    </>
  );
}
