import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import { api } from '../api/api';
import { auth } from '../api/client';
import type { MeResponse } from '../api/types';
import { Loading } from '../ui/ui';

interface Session {
  me: MeResponse | null;
  /** true, пока проверяем сохранённый токен при старте. */
  checking: boolean;
  login: (login: string, password: string) => Promise<MeResponse>;
  changePassword: (oldPassword: string, newPassword: string) => Promise<void>;
  logout: () => void;
}

const SessionContext = createContext<Session>(null!);

/** JWT живёт 14 дней (П-1.2); с временным паролем доступна только смена пароля. */
export function SessionProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<MeResponse | null>(null);
  const [checking, setChecking] = useState(!!auth.token);

  const logout = useCallback(() => {
    auth.set(null);
    setMe(null);
  }, []);

  useEffect(() => {
    auth.onUnauthorized(logout);
    if (!auth.token) return;
    api.me().then(setMe).catch(logout).finally(() => setChecking(false));
  }, [logout]);

  const login = useCallback(async (login: string, password: string) => {
    const r = await api.login(login, password);
    auth.set(r.token);
    const profile = await api.me();
    setMe(profile);
    return profile;
  }, []);

  const changePassword = useCallback(async (oldPassword: string, newPassword: string) => {
    const r = await api.changePassword(oldPassword, newPassword);
    auth.set(r.token);
    setMe(await api.me());
  }, []);

  return <SessionContext.Provider value={{ me, checking, login, changePassword, logout }}>{children}</SessionContext.Provider>;
}

export const useSession = () => useContext(SessionContext);

/** Текущий преподаватель (внутри RequireAuth он всегда есть). */
export const useMe = () => useContext(SessionContext).me!;

export function RequireAuth({ children }: { children: ReactNode }) {
  const { me, checking } = useSession();
  const location = useLocation();
  if (checking) return <Loading />;
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  if (me.mustChangePassword && location.pathname !== '/profile/password')
    return <Navigate to="/profile/password" replace />;
  return <>{children}</>;
}
