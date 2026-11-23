// Что показывать на колокольчике (карточка «Уведомления» в профиле). Хранится в браузере преподавателя.

export interface NotifyPrefs {
  /** Новые заявки студентов. */
  applications: boolean;
  /** Работы на проверку (сдано, проверка не отправлена). */
  reviews: boolean;
  /** Пересдачи: на проверке вторая и следующие версии. */
  resubmissions: boolean;
  /** Просроченные задания: студенты, не сдавшие к дедлайну. */
  overdue: boolean;
}

const KEY = 'fiit-hometask:notify';
const defaults: NotifyPrefs = { applications: true, reviews: true, resubmissions: true, overdue: false };
const listeners = new Set<() => void>();

export function getNotifyPrefs(): NotifyPrefs {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') };
  } catch {
    return defaults;
  }
}

export function setNotifyPrefs(prefs: NotifyPrefs) {
  try { localStorage.setItem(KEY, JSON.stringify(prefs)); } catch { /* без сохранения */ }
  listeners.forEach(l => l());
}

export function onNotifyPrefsChange(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
