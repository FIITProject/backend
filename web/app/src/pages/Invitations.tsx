import { useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import type { InviteDto } from '../api/types';
import { useCourse, useHeroAction } from '../course/CourseLayout';
import { fromInput, inviteStatusName, plural, sameSemester, shortDateTime, toInput } from '../lib/format';
import css from '../styles/pages/invitations.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, useAction, useConfirm, useLoad } from '../ui/ui';
import { capitalize, CopyButton, displayLink, IconCross, IconPencil, IconTick, useCourseCrumbs } from './course-admin/common';

// Срок редактируется нативным datetime-local — ему нужна ширина; колонки таблицы — как в дизайне.
const extraCss = `@layer pages {
  .invite-duration .input { width: 190px; }
}`;

/** invitations.html — приглашения в бота (П-4) для подгрупп этого курса. */
export default function Invitations() {
  const { course, subjectId, semester, readOnly, path } = useCourse();
  useHeroAction(readOnly ? null : <Link className="btn-dark" to={`${path}/invites/new`}>+ Создать приглашение</Link>, [readOnly, path]);
  useCourseCrumbs(course, path, { label: 'Приглашения' });
  const state = useLoad(async () => (await api.invites()).filter(i => i.subjectId === subjectId && sameSemester(i.semester, semester)),
    [subjectId, semester.year, semester.term]);
  const { confirm, dialog } = useConfirm();
  const { run } = useAction();

  const replace = (updated: InviteDto) => state.setData((state.data ?? []).map(i => (i.id === updated.id ? updated : i)));

  async function close(invite: InviteDto) {
    const ok = await confirm({
      title: 'Закрыть приглашение',
      text: 'По этой ссылке больше нельзя будет подать заявку. Уже поданные заявки останутся. Открыть приглашение снова нельзя — понадобится новое.',
      ok: 'Закрыть',
      danger: true,
    });
    if (!ok) return;
    const updated = await run(() => api.closeInvite(invite.id), 'Приглашение закрыто');
    if (updated) replace(updated);
  }

  return (
    <>
      <PageStyle css={[css, extraCss]} />
      <Async state={state}>
        {invites => invites.length === 0
          ? <Empty>Приглашений пока нет. Создайте ссылку и опубликуйте её в чате курса — студенты подадут заявку через бота.</Empty>
          : (
            <div className="card p-8-0-0">
              <div className="invite-grid invite-head">
                <span>Ссылки для студентов</span>
                <span>Срок действия</span>
                <span>Статус заявок</span>
                <span>Действие</span>
              </div>
              {invites.map(i => (
                <InviteRow key={i.id} invite={i} onChange={replace} onClose={() => close(i)} />
              ))}
            </div>
          )}
      </Async>
      {dialog}
    </>
  );
}

function InviteRow({ invite, onChange, onClose }: { invite: InviteDto; onChange: (i: InviteDto) => void; onClose: () => void }) {
  const active = invite.status === 'Active';
  const chip = active ? 'chip chip--brand' : 'chip chip--gray';
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const { run, busy } = useAction();

  function startEdit() {
    setValue(invite.expiresAt ? toInput(invite.expiresAt) : '');
    setEditing(true);
  }

  async function save() {
    const expiresAt = value ? fromInput(value) : null;
    if (value && !expiresAt) return;
    if (expiresAt === invite.expiresAt) {
      setEditing(false);
      return;
    }
    const updated = await run(() => api.setInviteExpiry(invite.id, expiresAt), expiresAt ? 'Срок изменён' : 'Приглашение теперь бессрочное');
    if (updated) {
      onChange(updated);
      setEditing(false);
    }
  }

  return (
    <div className="invite-grid invite-row">
      <div>
        <b className="fs-15 invite-link">{displayLink(invite.link)}</b>
        <div className="row gap-10 mt-8 flex-wrap invite-groups">
          {invite.courses.map(c => <span className={chip} key={c.id}>{c.name}</span>)}
        </div>
      </div>
      <span className="text-muted invite-duration">
        {editing
          ? (
            <input aria-label="Срок действия (пусто — без срока)" autoFocus className="input" onChange={e => setValue(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') save(); if (e.key === 'Escape') setEditing(false); }}
              title="Очистите поле, чтобы сделать приглашение бессрочным" type="datetime-local" value={value} />
          )
          : <span>{invite.expiresAt ? `до ${shortDateTime(invite.expiresAt)}` : 'без срока'}</span>}
        {invite.status !== 'Closed' && (editing
          ? (
            <>
              <button aria-label="Сохранить срок" className="icon-btn w-32 h-32 flex-none" disabled={busy} onClick={save} title="Сохранить срок" type="button">
                <IconTick />
              </button>
              {/* Закрыть досрочно (П-4) — здесь, чтобы не добавлять в строку кнопок, которых нет в дизайне */}
              <button aria-label="Закрыть приглашение" className="icon-btn icon-btn--delete w-32 h-32 flex-none" onClick={() => { setEditing(false); onClose(); }}
                title="Закрыть приглашение досрочно" type="button">
                <IconCross />
              </button>
            </>
          )
          : (
            <button aria-label="Изменить срок" className="icon-btn w-32 h-32 flex-none" onClick={startEdit} title="Изменить срок" type="button">
              <IconPencil />
            </button>
          ))}
      </span>
      <span><span className={chip} title={`${invite.applicationsCount} ${plural(invite.applicationsCount, 'заявка', 'заявки', 'заявок')}`}>{capitalize(inviteStatusName[invite.status])}</span></span>
      <CopyButton text={invite.link} />
    </div>
  );
}
