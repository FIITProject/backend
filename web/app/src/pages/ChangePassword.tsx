import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useMe, useSession } from '../auth/session';
import { useCrumbs } from '../layout/AppLayout';
import css from '../styles/pages/password-change.css?inline';
import { IconAlert, IconCheck, IconLock } from '../ui/icons';
import { PageStyle } from '../ui/PageStyle';
import { errorText, Modal, PasswordInput, useToast } from '../ui/ui';

// Требования к паролю из дизайна (сервер дополнительно проверяет длину и что пароль сменился)
const rules = [
  { key: 'len', label: 'Не менее 8 символов', test: (v: string) => v.length >= 8 },
  { key: 'case', label: 'Строчные и прописные латинские буквы', test: (v: string) => /[a-z]/.test(v) && /[A-Z]/.test(v) && !/\p{L}/u.test(v.replace(/[A-Za-z]/g, '')) },
  { key: 'digit', label: 'Хотя бы одна цифра', test: (v: string) => /[0-9]/.test(v) },
];

/** password-change.html; с временным паролем (П-10) сюда отправляют сразу после входа. */
export default function ChangePassword() {
  const me = useMe();
  const { changePassword } = useSession();
  const navigate = useNavigate();
  const toast = useToast();
  const [cur, setCur] = useState('');
  const [pw, setPw] = useState('');
  const [rep, setRep] = useState('');
  const [empty, setEmpty] = useState(false);
  const [invalid, setInvalid] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useCrumbs([{ label: 'Курсы', to: '/courses' }, { label: 'Профиль', to: '/profile' }, { label: 'Смена пароля' }]);

  const met = [...rules.map(r => r.test(pw)), pw !== '' && pw === rep];
  const ok = met.every(Boolean);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!cur || !pw || !rep) {
      setEmpty(true);
      return;
    }
    if (!ok) {
      setInvalid('Текущий пароль нельзя изменить, потому что новый пароль не соответствует требованиям.');
      return;
    }
    setBusy(true);
    try {
      await changePassword(cur, pw);
      toast('Пароль изменён, остальные сессии завершены');
      navigate('/profile', { replace: true });
    } catch (err) {
      setInvalid(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="pc-wrap">
      <PageStyle css={css} />
      <div>
        <h1>Смена пароля</h1>
        <div className="text-muted fs-17 mt-8">
          {me.mustChangePassword ? 'Вы вошли с временным паролем — придумайте свой, чтобы продолжить' : 'Придумайте новый пароль для входа в аккаунт'}
        </div>
      </div>
      <div className="d-grid gap-24 items-start-grid d-grid--x1">
        <form className="card card-section gap-20" noValidate onSubmit={submit}>
          <label className="field-label">
            <span>{me.mustChangePassword ? 'Временный пароль' : 'Текущий пароль'}</span>
            <div className="field"><IconLock /><PasswordInput autoComplete="current-password" onChange={e => { setCur(e.target.value); setEmpty(false); }} placeholder="••••••••" value={cur} /></div>
          </label>
          <div className="h-1 bg-border-faint" />
          <label className="field-label">
            <span>Новый пароль</span>
            <div className="field"><IconLock /><PasswordInput autoComplete="new-password" onChange={e => { setPw(e.target.value); setEmpty(false); }} placeholder="••••••••••" value={pw} /></div>
          </label>
          <label className="field-label">
            <span>Повторите новый пароль</span>
            <div className="field"><IconLock /><PasswordInput autoComplete="new-password" onChange={e => { setRep(e.target.value); setEmpty(false); }} placeholder="••••••••••" value={rep} /></div>
          </label>
          {empty && (
            <div className="alert-error" role="alert"><IconAlert /><div>Пожалуйста, заполните все поля ввода</div></div>
          )}
          <div className="row gap-12 mt-6">
            {!me.mustChangePassword && <Link className="btn-outline h-52" to="/profile">Отмена</Link>}
            <button className="btn-submit" disabled={busy} type="submit">Сменить пароль</button>
          </div>
        </form>
        <div className="card card-section gap-14">
          <h2 className="fs-20">Требования к паролю</h2>
          {[...rules.map(r => r.label), 'Пароли совпадают'].map((label, i) => (
            <div className="row gap-12" key={label}>
              <span className={`check${met[i] ? '' : ' is-off'}`}><IconCheck /></span>
              <span className="fw-600">{label}</span>
            </div>
          ))}
        </div>
      </div>
      <Modal open={!!invalid} title="Пароль не изменён" onClose={() => setInvalid(null)} size="lg">
        <div className="modal__text modal__text--regular">{invalid}</div>
      </Modal>
    </div>
  );
}
