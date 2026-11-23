import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError } from '../api/client';
import { IconAlert, IconClose, IconEye, IconEyeOff } from './icons';

// ── Загрузка данных ───────────────────────────────────────────────────

export interface Loaded<T> {
  data: T | undefined;
  error: string | null;
  loading: boolean;
  reload: () => void;
  setData: (value: T) => void;
}

/** Загружает данные при изменении deps; ошибка API — человеческий текст из ProblemDetails. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]): Loaded<T> {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let alive = true;
    setLoading(true);
    load()
      .then(d => { if (alive) { setData(d); setError(null); } })
      .catch(e => { if (alive) setError(errorText(e)); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  return { data, error, loading, reload: useCallback(() => setTick(t => t + 1), []), setData };
}

export function errorText(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof TypeError) return 'Сервер недоступен, проверьте соединение';
  return e instanceof Error ? e.message : String(e);
}

/** Пока грузится — спиннер, при ошибке — сообщение, иначе children(data). */
export function Async<T>({ state, children }: { state: Loaded<T>; children: (data: T) => ReactNode }) {
  if (state.error && state.data === undefined) return <Alert>{state.error}</Alert>;
  if (state.data === undefined) return <Loading />;
  return <>{children(state.data)}</>;
}

export const Loading = () => (
  <div className="ui-loading" aria-busy="true"><div className="ui-spinner" /></div>
);

export const Alert = ({ children }: { children: ReactNode }) => (
  <div className="ui-alert" role="alert"><IconAlert />{children}</div>
);

export const Empty = ({ children }: { children: ReactNode }) => <div className="ui-empty">{children}</div>;

// ── Тосты ─────────────────────────────────────────────────────────────

interface Toast { id: number; text: string; error: boolean }
const ToastContext = createContext<(text: string, opts?: { error?: boolean }) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const seq = useRef(0);
  const show = useCallback((text: string, opts?: { error?: boolean }) => {
    const id = ++seq.current;
    setToasts(t => [...t, { id, text, error: !!opts?.error }]);
    setTimeout(() => setToasts(t => t.filter(x => x.id !== id)), opts?.error ? 5000 : 2600);
  }, []);
  return (
    <ToastContext.Provider value={show}>
      {children}
      {toasts.slice(-1).map(t => (
        <div key={t.id} className={`toast${t.error ? ' toast--error' : ''}`} role="status">{t.text}</div>
      ))}
    </ToastContext.Provider>
  );
}

/** toast('Сохранено') или toast(errorText(e), { error: true }). */
export const useToast = () => useContext(ToastContext);

/** Выполнить действие с API: ошибка — тостом, успех — опциональным тостом. Возвращает результат или undefined. */
export function useAction() {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const run = useCallback(async <T,>(action: () => Promise<T>, success?: string): Promise<T | undefined> => {
    setBusy(true);
    try {
      const result = await action();
      if (success) toast(success);
      return result;
    } catch (e) {
      toast(errorText(e), { error: true });
      return undefined;
    } finally {
      setBusy(false);
    }
  }, [toast]);
  return { run, busy };
}

// ── Модальное окно (разметка modal.css дизайна) ──────────────────────

export function Modal({ open, title, onClose, children, actions, size }: {
  open: boolean; title: ReactNode; onClose: () => void; children?: ReactNode; actions?: ReactNode; size?: 'lg';
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div aria-modal="true" className="modal" role="dialog">
      <div className="modal__backdrop" onClick={onClose} />
      <div className={`modal__dialog${size ? ` modal__dialog--${size}` : ''}`}>
        <button aria-label="Закрыть" className="modal__close" onClick={onClose} title="Закрыть" type="button"><IconClose /></button>
        <h2 className="modal__title">{title}</h2>
        {children}
        {actions && <div className="modal__actions">{actions}</div>}
      </div>
    </div>
  );
}

/** Подтверждение действия: confirm({ title, text, ok }) → Promise<boolean>. */
export function useConfirm() {
  const [state, setState] = useState<{ title: string; text?: string; ok: string; danger?: boolean; resolve: (v: boolean) => void } | null>(null);
  const confirm = useCallback((o: { title: string; text?: string; ok?: string; danger?: boolean }) =>
    new Promise<boolean>(resolve => setState({ ok: 'Подтвердить', ...o, resolve })), []);
  const close = (v: boolean) => { state?.resolve(v); setState(null); };
  const dialog = (
    <Modal open={!!state} title={state?.title} onClose={() => close(false)}
      actions={<>
        <button className="btn-outline" type="button" onClick={() => close(false)}>Отмена</button>
        <button className={state?.danger ? 'btn-primary btn-danger-soft' : 'btn-primary'} type="button" onClick={() => close(true)}>{state?.ok}</button>
      </>}>
      {state?.text && <div className="modal__text">{state.text}</div>}
    </Modal>
  );
  return { confirm, dialog };
}

// ── Поле пароля с «глазом» (forms.css дизайна) ────────────────────────

export function PasswordInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  const [show, setShow] = useState(false);
  return (
    <>
      <input {...props} className="input" type={show ? 'text' : 'password'} />
      <button aria-label={show ? 'Скрыть пароль' : 'Показать пароль'} aria-pressed={show} className="eye-btn" onClick={() => setShow(s => !s)}
        title={show ? 'Скрыть пароль' : 'Показать пароль'} type="button">
        <IconEye /><IconEyeOff />
      </button>
    </>
  );
}
