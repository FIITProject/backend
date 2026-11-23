import { useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api/api';
import type { InviteDto } from '../api/types';
import { useCourse } from '../course/CourseLayout';
import { dateTime, defaultDeadline, fromInput, toInput } from '../lib/format';
import { PageStyle } from '../ui/PageStyle';
import { Alert, useAction, useToast } from '../ui/ui';
import { useCrumbs } from '../layout/AppLayout';
import { CopyButton, displayLink, GroupPicker } from './course-admin/common';

const extraCss = `@layer pages {
  .segmented > [role="tab"] { cursor: pointer; user-select: none; }
}`;

/** new-invitation.html — новая ссылка-приглашение в бота (П-4.1) для выбранных подгрупп курса. */
export default function NewInvitation() {
  const { course, subjectId, activeCourses, readOnly, path } = useCourse();
  // Как в дизайне: на вложенной странице в цепочке только название предмета
  useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: course.subjectName, to: path }, { label: 'Приглашения', to: `${path}/invites` }, { label: 'Новое' }]);
  const toast = useToast();
  const { run, busy } = useAction();
  const [selected, setSelected] = useState(() => activeCourses.map(c => c.id));
  const [withDeadline, setWithDeadline] = useState(true);
  const [until, setUntil] = useState(() => toInput(defaultDeadline()));
  const [created, setCreated] = useState<InviteDto | null>(null);

  async function create() {
    if (selected.length === 0) {
      toast('Выберите хотя бы одну группу', { error: true });
      return;
    }
    const expiresAt = withDeadline ? fromInput(until) : null;
    if (withDeadline && !expiresAt) {
      toast('Укажите, до какого момента действует ссылка', { error: true });
      return;
    }
    const invite = await run(() => api.createInvite(subjectId, selected, expiresAt), 'Ссылка создана');
    if (invite) setCreated(invite);
  }

  function again() {
    setCreated(null);
    setSelected(activeCourses.map(c => c.id));
  }

  if (readOnly) return <Alert>Курс в архиве — новые приглашения создавать нельзя. Верните курс из архива в настройках.</Alert>;

  const tab = (value: boolean, label: string) => (
    <span aria-selected={withDeadline === value} className={withDeadline === value ? 'is-active' : undefined}
      onClick={() => !created && setWithDeadline(value)}
      onKeyDown={e => { if (!created && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); setWithDeadline(value); } }}
      role="tab" tabIndex={0}>{label}</span>
  );

  return (
    <>
      <PageStyle css={extraCss} />
      <div>
        <h1>Новое приглашение</h1>
      </div>
      <div className="row items-start gap-28">
        <div className="flex-1 min-w-0 d-flex flex-col gap-20">
          <fieldset className="card card-section" disabled={!!created} style={{ border: 0, margin: 0, minWidth: 0 }}>
            <div className="row">
              <span className="step-number">1</span>
              <h2>Для каких групп</h2>
            </div>
            <GroupPicker courses={activeCourses} disabled={!!created} onChange={setSelected} selected={selected} />
            <div className="text-muted">Студент выберет подгруппу из этого списка, когда перейдёт по ссылке</div>
          </fieldset>
          <fieldset className="card card-section" disabled={!!created} style={{ border: 0, margin: 0, minWidth: 0 }}>
            <div className="row">
              <span className="step-number">2</span>
              <h2>Срок действия</h2>
            </div>
            <div className="segmented w-340" role="tablist">
              {tab(true, 'До даты')}
              {tab(false, 'Без срока')}
            </div>
            {withDeadline
              ? (
                <label className="w-260">Действует до
                  <input className="input rounded-16" onChange={e => setUntil(e.target.value)} type="datetime-local" value={until} />
                </label>
              )
              : <div className="text-muted">Ссылка будет действовать, пока вы её не закроете</div>}
          </fieldset>
          <div className="row justify-end">
            {created
              ? <>
                <button className="btn-outline" onClick={again} type="button">Создать ещё</button>
                <Link className="btn-primary h-52" to={`${path}/invites`}>К приглашениям</Link>
              </>
              : <>
                <Link className="btn-outline" to={`${path}/invites`}>Отмена</Link>
                <button className="btn-primary h-52" disabled={busy} onClick={create} type="button">Создать ссылку</button>
              </>}
          </div>
        </div>
        <aside className="panel-dark w-360 flex-none p-28 d-flex flex-col gap-14">
          <div className="dots" />
          <h2 className="text-on-brand">Ссылка</h2>
          <div className="bg-c-ffffff1f rounded-16 p-16 fw-700 break-all">{created ? displayLink(created.link) : 't.me/…?start=…'}</div>
          {created
            ? <>
              <div><CopyButton text={created.link} /></div>
              <div className="opacity-8 fs-13">
                {created.courses.map(c => c.name).join(', ')} · {created.expiresAt ? `до ${dateTime(created.expiresAt)}` : 'без срока'}.
                Опубликуйте ссылку в чате курса, студенты заполнят анкету в боте.
              </div>
            </>
            : <div className="opacity-8 fs-13">Ссылка появится после создания. Опубликуйте её в чате курса, студенты заполнят анкету в боте.</div>}
        </aside>
      </div>
    </>
  );
}
