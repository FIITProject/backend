// HTTP-клиент API: JWT из сессии, ошибки — ProblemDetails (title — текст для человека, code — для логики).

export class ApiError extends Error {
  constructor(public status: number, message: string, public code: string | null) {
    super(message);
  }
}

const TOKEN_KEY = 'fiit-hometask:token';

let token: string | null = (() => {
  try { return localStorage.getItem(TOKEN_KEY); } catch { return null; }
})();
let onUnauthorized: () => void = () => {};

export const auth = {
  get token() { return token; },
  set(value: string | null) {
    token = value;
    try {
      if (value) localStorage.setItem(TOKEN_KEY, value);
      else localStorage.removeItem(TOKEN_KEY);
    } catch { /* приватный режим — живём без сохранения */ }
  },
  /** Вызывается на 401: сессия истекла или отозвана. */
  onUnauthorized(handler: () => void) { onUnauthorized = handler; },
};

type Query = Record<string, string | number | boolean | null | undefined | (string | number)[]>;

export function qs(query?: Query): string {
  if (!query) return '';
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === null || v === undefined || v === '') continue;
    if (Array.isArray(v)) v.forEach(x => p.append(k, String(x)));
    else p.append(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

async function send(method: string, url: string, body?: unknown): Promise<Response> {
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api${url}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (response.ok) return response;

  let message = `Ошибка ${response.status}`;
  let code: string | null = null;
  try {
    const problem = await response.json();
    message = problem.title ?? message;
    code = problem.code ?? null;
  } catch { /* не JSON */ }
  if (response.status === 401 && !url.startsWith('/auth/')) onUnauthorized();
  throw new ApiError(response.status, message, code);
}

export async function get<T>(url: string, query?: Query): Promise<T> {
  return (await send('GET', url + qs(query))).json();
}

export async function post<T = void>(url: string, body?: unknown): Promise<T> {
  const r = await send('POST', url, body ?? {});
  return (r.status === 204 || r.headers.get('content-length') === '0' ? undefined : await r.json().catch(() => undefined)) as T;
}

export async function put<T>(url: string, body: unknown): Promise<T> {
  return (await send('PUT', url, body)).json();
}

export async function patch<T>(url: string, body: unknown): Promise<T> {
  return (await send('PATCH', url, body)).json();
}

export async function del(url: string): Promise<void> {
  await send('DELETE', url);
}

/** Файл (PDF, Excel) с авторизацией — как Blob и имя из Content-Disposition. */
export async function download(url: string, query?: Query): Promise<{ blob: Blob; fileName: string | null }> {
  const r = await send('GET', url + qs(query));
  const cd = r.headers.get('content-disposition') ?? '';
  const star = /filename\*=UTF-8''([^;]+)/i.exec(cd);
  const plain = /filename="?([^";]+)"?/i.exec(cd);
  return { blob: await r.blob(), fileName: star ? decodeURIComponent(star[1]) : plain ? plain[1] : null };
}

export function saveBlob(blob: Blob, fileName: string) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = fileName;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
