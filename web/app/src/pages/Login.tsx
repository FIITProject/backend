import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { ApiError } from '../api/client';
import { useSession } from '../auth/session';
import authCss from '../styles/pages/auth.css?inline';
import authorizationCss from '../styles/pages/authorization.css?inline';
import { IconAlert, IconArrowRight, IconDoc, IconLock, IconPen, IconTable, IconUser } from '../ui/icons';
import { PageStyle } from '../ui/PageStyle';
import { errorText, PasswordInput } from '../ui/ui';

/** authorization.html */
export default function Login() {
  const { me, login } = useSession();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/';
  const [form, setForm] = useState({ login: '', password: '' });
  const [error, setError] = useState<{ title: string; hint?: string } | null>(null);
  const [busy, setBusy] = useState(false);

  if (me) return <Navigate to={me.mustChangePassword ? '/profile/password' : from} replace />;

  async function submit() {
    setError(null);
    if (!form.login.trim() || !form.password) {
      setError({ title: 'Введите логин и пароль' });
      return;
    }
    setBusy(true);
    try {
      const profile = await login(form.login.trim(), form.password);
      navigate(profile.mustChangePassword ? '/profile/password' : from, { replace: true });
    } catch (e) {
      setError(e instanceof ApiError && e.status === 401
        ? { title: 'Неверный логин или пароль', hint: 'Проверьте раскладку и Caps Lock' }
        : { title: errorText(e) });
    } finally {
      setBusy(false);
    }
  }

  const onKey = (e: React.KeyboardEvent) => e.key === 'Enter' && submit();
  const set = (field: 'login' | 'password') => (e: React.ChangeEvent<HTMLInputElement>) => {
    setForm(f => ({ ...f, [field]: e.target.value }));
    setError(null);
  };

  return (
    <div className="auth-split">
      <PageStyle css={[authCss, authorizationCss]} />
      <div className="auth-aside">
        <div className="glow" />
        <div className="dots" />
        <div className="dots--secondary" />
        <div className="wordmark">
          <span className="logo-mark">f</span>
          <div>fiit<i>.hometask</i></div>
        </div>
        <div className="headline">Проверка домашних работ без лишней рутины</div>
        <div className="subhead">Всё, что нужно преподавателю: от выдачи задания до проверки и выгрузки баллов в ведомость</div>
        <div className="mt-36 d-flex flex-col gap-16">
          <div className="feature-item"><span className="feature-item__icon"><IconDoc /></span>Задания и дедлайны по темам</div>
          <div className="feature-item"><span className="feature-item__icon"><IconPen /></span>Проверка прямо на PDF с пометками</div>
          <div className="feature-item"><span className="feature-item__icon"><IconTable /></span>Сводная таблица и выгрузка в Excel</div>
        </div>
      </div>
      <div className="auth-main">
        <div className="card">
          <div>
            <h1>Вход</h1>
            <div className="text-muted mt-8">С возвращением! Введите логин и пароль</div>
          </div>
          <label>Логин
            <div className="field">
              <IconUser />
              <input autoComplete="username" autoFocus className="input" onChange={set('login')} onKeyDown={onKey} placeholder="happy_person" type="text" value={form.login} />
            </div>
          </label>
          <label>Пароль
            <div className="field">
              <IconLock />
              <PasswordInput autoComplete="current-password" onChange={set('password')} onKeyDown={onKey} placeholder="••••••••" value={form.password} />
            </div>
          </label>
          {error && (
            <div className="alert-error" role="alert">
              <IconAlert />
              <div>{error.title}{error.hint && <div className="fw-500 fs-13 mt-2">{error.hint}</div>}</div>
            </div>
          )}
          <button className="btn-primary" disabled={busy} onClick={submit} type="button">Войти <IconArrowRight /></button>
          <div className="text-muted fs-13">Сессия действует 14 дней. По истечении этого срока можно авторизоваться заново</div>
        </div>
      </div>
    </div>
  );
}
