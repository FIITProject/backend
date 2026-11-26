// Живые события API (SSE, GET /api/me/events) для колокольчика.
// EventSource не умеет заголовок Authorization, поэтому поток читается через fetch.

import { auth } from '../api/client';

export type LiveEvent = { type: 'ready' } | { type: 'changed'; kind: string };

/**
 * Подписка на события с переподключением (1 с, 2 с, … до 30 с). После каждого (пере)подключения приходит
 * `ready`: за время разрыва события могли потеряться, и подписчик перечитывает данные. Возвращает отписку.
 */
export function subscribeLive(onEvent: (e: LiveEvent) => void): () => void {
  const abort = new AbortController();
  let delay = 1000;

  const run = async () => {
    while (!abort.signal.aborted) {
      try {
        const response = await fetch('/api/me/events', {
          headers: auth.token ? { Authorization: `Bearer ${auth.token}` } : {},
          signal: abort.signal,
        });
        // 401 — сессия кончилась: выход обработают обычные запросы страниц, здесь просто перестаём
        if (response.status === 401) return;
        if (!response.ok || !response.body) throw new Error(`events: ${response.status}`);
        await read(response.body, e => {
          if (e.type === 'ready') delay = 1000;
          onEvent(e);
        });
      } catch {
        if (abort.signal.aborted) return;
      }
      await new Promise(r => setTimeout(r, delay));
      delay = Math.min(delay * 2, 30_000);
    }
  };
  void run();
  return () => abort.abort();
}

async function read(body: ReadableStream<Uint8Array>, onEvent: (e: LiveEvent) => void) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += decoder.decode(value, { stream: true });
    let end: number;
    while ((end = buffer.indexOf('\n\n')) >= 0) {
      const block = buffer.slice(0, end);
      buffer = buffer.slice(end + 2);
      let name = '';
      let data = '';
      for (const line of block.split('\n')) {
        if (line.startsWith('event: ')) name = line.slice(7);
        else if (line.startsWith('data: ')) data = line.slice(6);
      }
      if (name === 'ready') onEvent({ type: 'ready' });
      else if (name === 'changed') onEvent({ type: 'changed', kind: data });
    }
  }
}
