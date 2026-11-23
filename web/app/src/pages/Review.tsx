import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api } from '../api/api';
import { ApiError, saveBlob } from '../api/client';
import { ErrorCodes, type HomeworkDto, type ReviewContextDto, type ReviewDto, type SubmissionDto } from '../api/types';
import { subjectCoursePath } from '../lib/courses';
import { shortDateTime } from '../lib/format';
import css from '../styles/pages/performance-check.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Alert, errorText, Loading, Modal, useToast } from '../ui/ui';
import { PdfReviewer, type Tool, type ToolOptions } from './review/pdfReviewer';
import reviewCss from './review/review.css?inline';
import { fmtScore, ScoreDial } from './review/ScoreDial';
import { parseTitle } from './tasks/taskCommon';

/**
 * performance-check.html (проверка) и evaluated.html (проверка уже отправлена: только PDF, «Перепроверить»).
 * Отдельная страница без бокового меню. П-9 ТЗ, решения 5, 8, 10 docs/architecture.md.
 */
export default function Review() {
  const params = useParams();
  const homeworkId = Number(params.homeworkId);
  const studentId = Number(params.studentId);
  // Другой студент — новый экран: размонтирование старого дописывает его несохранённый черновик
  return <ReviewScreen key={`${homeworkId}/${studentId}`} homeworkId={homeworkId} studentId={studentId} />;
}

function ReviewScreen({ homeworkId, studentId }: { homeworkId: number; studentId: number }) {
  const [ctx, setCtx] = useState<{ data: ReviewContextDto; gen: number; keepVersion: number | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [homework, setHomework] = useState<HomeworkDto | null>(null);
  const [neighbors, setNeighbors] = useState<number[]>([]);

  const load = useCallback((keepVersion: number | null) => {
    api.reviewContext(homeworkId, studentId)
      .then(data => { setCtx(c => ({ data, gen: (c?.gen ?? 0) + 1, keepVersion })); setError(null); })
      .catch(e => setError(errorText(e)));
  }, [homeworkId, studentId]);

  useEffect(() => load(null), [load]);

  // ДЗ — для ссылки «К заданию»; сводная таблица предмета — для «Предыдущая / Следующая» (студенты со сдачей этого ДЗ)
  useEffect(() => {
    let alive = true;
    api.homework(homeworkId)
      .then(async hw => {
        if (!alive) return;
        setHomework(hw);
        const gb = await api.gradebook(hw.subjectId, hw.semester);
        if (alive) setNeighbors(gb.rows.filter(r => r.cells.some(c => c.homeworkId === homeworkId && c.latestSubmissionId !== null)).map(r => r.studentId));
      })
      .catch(() => { /* без навигации между студентами страница работает */ });
    return () => { alive = false; };
  }, [homeworkId]);

  const backTo = homework ? `${subjectCoursePath(homework)}/tasks/${homeworkId}` : '/';
  const i = neighbors.indexOf(studentId);
  const nav = {
    backTo,
    prev: i > 0 ? neighbors[i - 1] : null,
    next: i >= 0 && i < neighbors.length - 1 ? neighbors[i + 1] : null,
  };

  const app = useFitViewport(880);

  return (
    <div className="app flex-col review-page" ref={app}>
      <PageStyle css={[css, reviewCss]} />
      <div className="pattern">
        <div className="top-n30">ФИИТ ФИИТ ФИИТ ФИИТ</div>
        <div className="top-160 left-n330">ФИИТ ФИИТ ФИИТ ФИИТ</div>
        <div className="top-350 top-350--x1">ФИИТ ФИИТ ФИИТ ФИИТ</div>
        <div className="top-540 left-n330">ФИИТ ФИИТ ФИИТ ФИИТ</div>
        <div className="top-730">ФИИТ ФИИТ ФИИТ ФИИТ</div>
      </div>
      {ctx ? (
        <Workspace key={ctx.gen} ctx={ctx.data} keepVersion={ctx.keepVersion} nav={nav} onReload={load} />
      ) : (
        <>
          <div className="glass stacked m-20-24-0 p-12-20 rounded-24">
            <Link className="text-brand-alt-strong fw-800 no-underline" to={backTo}>← К заданию</Link>
          </div>
          <div className="stacked p-20-24-24">{error ? <Alert>{error}</Alert> : <Loading />}</div>
        </>
      )}
    </div>
  );
}

/**
 * Как fit-viewport.js дизайна: страница проверки рассчитана на высоту refHeight и целиком масштабируется (zoom) под окно,
 * не мельче 0.6. При уходе со страницы масштаб сбрасывается.
 */
function useFitViewport(refHeight: number) {
  const app = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const root = document.documentElement;
    function fit() {
      const zoom = Math.max(0.6, Math.min(1, (window.innerHeight / refHeight) * 0.995));
      root.style.zoom = String(zoom);
      if (app.current) app.current.style.height = `${window.innerHeight / zoom}px`;
    }
    fit();
    window.addEventListener('resize', fit);
    return () => {
      window.removeEventListener('resize', fit);
      root.style.zoom = '';
    };
  }, [refHeight]);
  return app;
}

// ── Экран проверки ─────────────────────────────────────────────────────

const COLORS = [
  { name: 'Чёрный', cls: 'black', hex: '#1A1625' },
  { name: 'Красный', cls: 'red', hex: '#E5484D' },
  { name: 'Оранжевый', cls: 'orange', hex: '#F59E42' },
  { name: 'Зелёный', cls: 'green', hex: '#3DBE8B' },
  { name: 'Розовый', cls: 'pink', hex: '#D894D4' },
  { name: 'Фиолетовый', cls: 'violet', hex: '#8271E8' },
];

const SIZES = [
  { name: 'тонко', width: 1, font: 11, r: 2 },
  { name: 'обычно', width: 2, font: 14, r: 3.5 },
  { name: 'толсто', width: 4, font: 18, r: 5 },
];

type SaveState = 'none' | 'dirty' | 'saving' | 'saved' | 'error' | 'invalid';

const parseScore = (s: string): number | null | undefined => {
  const t = s.trim().replace(',', '.');
  if (t === '') return null;
  const n = Number(t);
  return isFinite(n) ? n : undefined; // undefined — не число
};

/** TimeSpan из JSON («1.02:03:04.5» или «02:03:04») → «1 дн. 2 ч 3 мин». */
function lateText(span: string | null): string {
  const m = span && /^(?:(\d+)\.)?(\d+):(\d+)/.exec(span);
  if (!m) return '';
  const [d, h, min] = [Number(m[1] ?? 0), Number(m[2]), Number(m[3])];
  const parts = [d && `${d} дн.`, h && `${h} ч`, (min || (!d && !h)) && `${min} мин`].filter(Boolean);
  return parts.join(' ');
}

interface Nav { backTo: string; prev: number | null; next: number | null }

function Workspace({ ctx, keepVersion, nav, onReload }: {
  ctx: ReviewContextDto; keepVersion: number | null; nav: Nav; onReload: (keepVersion: number | null) => void;
}) {
  const navigate = useNavigate();
  const toast = useToast();

  const [submissions, setSubmissions] = useState(ctx.submissions);
  const latest = submissions.find(s => s.isLatest) ?? submissions[submissions.length - 1];
  const [selectedId, setSelectedId] = useState(() =>
    (ctx.submissions.find(s => s.version === keepVersion) ?? latest)?.id ?? null);
  const sub = submissions.find(s => s.id === selectedId) ?? null;
  const review = sub?.review ?? null;

  // Форма проверки выбранной версии
  const [scores, setScores] = useState<Record<number, string>>({});
  const [override, setOverride] = useState<number | null>(null);
  const [comment, setComment] = useState('');
  const annotations = useRef('[]');

  const [saveState, setSaveState] = useState<SaveState>('none');
  const [conflict, setConflict] = useState<string | null>(null);
  const [conflictOpen, setConflictOpen] = useState(false);
  const [sending, setSending] = useState(false);
  const [sentNote, setSentNote] = useState(0);
  // evaluated.html: отправленная проверка без новых правок открывается только с PDF; «Перепроверить» возвращает панели
  const [recheck, setRecheck] = useState(() => !(latest?.review?.sentAt && !latest.review.hasUnsentChanges && !ctx.isReadOnly));

  // Инструменты
  const [tool, setTool] = useState<Tool>('pen');
  const [color, setColor] = useState(COLORS[1].hex);
  const size = 1; // «обычно»; отдельного переключателя толщины в дизайне нет
  const [scale, setScale] = useState<number | null>(null);
  const viewer = useRef<PdfReviewer | null>(null);
  const toolOptions = useMemo<ToolOptions>(() => ({ tool, color, width: SIZES[size].width, fontSize: SIZES[size].font }), [tool, color, size]);

  const readOnly = ctx.isReadOnly || !sub?.isLatest;

  // Отмена и повтор — с клавиатуры (Ctrl/⌘+Z, Ctrl/⌘+Shift+Z): кнопок в панели дизайна нет
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (readOnly || !(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 'z') return;
      if (e.target instanceof HTMLElement && e.target.closest('input, textarea')) return;
      e.preventDefault();
      if (e.shiftKey) viewer.current?.redo();
      else viewer.current?.undo();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [readOnly]);
  const sentView = !recheck && !!sub?.isLatest;

  const subtasks = ctx.subtasks.filter(st => st.id !== null);
  const parsed = subtasks.map(st => ({ st, value: parseScore(scores[st.id!] ?? '') }));
  const invalid = new Set(parsed.filter(p => p.value === undefined || (p.value !== null && (p.value < 0 || (p.st.maxScore !== null && p.value > p.st.maxScore))))
    .map(p => p.st.id!));
  const filled = parsed.filter(p => typeof p.value === 'number');
  const sum = filled.length ? Math.round(filled.reduce((a, p) => a + (p.value as number), 0) * 100) / 100 : null;
  const total = override ?? sum;
  const maxTotal = subtasks.length && subtasks.every(st => st.maxScore !== null)
    ? subtasks.reduce((a, st) => a + st.maxScore!, 0) : null;

  // ── Сохранение черновика (П-9.6): через секунду после последней правки, по одному запросу за раз ──
  const state = useRef({ sub, scores, override, comment, readOnly, invalid: invalid.size > 0, parsed });
  state.current = { sub, scores, override, comment, readOnly, invalid: invalid.size > 0, parsed };
  const reviews = useRef(new Map<number, ReviewDto | null>(ctx.submissions.map(s => [s.id, s.review])));
  const dirty = useRef(false);
  const inFlight = useRef<Promise<boolean> | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const conflictRef = useRef(false);

  const updateReview = useCallback((submissionId: number, r: ReviewDto) => {
    reviews.current.set(submissionId, r);
    setSubmissions(list => list.map(s => (s.id === submissionId ? { ...s, review: r } : s)));
  }, []);

  const save = useCallback(async (): Promise<boolean> => {
    clearTimeout(timer.current);
    while (inFlight.current) await inFlight.current;
    if (!dirty.current) return true;
    const st = state.current;
    if (!st.sub || st.readOnly || conflictRef.current) return false;
    if (st.invalid) {
      setSaveState('invalid');
      return false;
    }
    dirty.current = false;
    const submissionId = st.sub.id;
    const run = (async () => {
      setSaveState('saving');
      try {
        const r = await api.saveReview(submissionId, {
          annotations: annotations.current,
          comment: st.comment.trim() ? st.comment : null,
          subtaskScores: st.parsed.map(p => ({ subtaskId: p.st.id!, score: p.value ?? null })),
          totalOverride: st.override,
          baseUpdatedAt: reviews.current.get(submissionId)?.updatedAt ?? null,
        });
        updateReview(submissionId, r);
        setSaveState(dirty.current ? 'dirty' : 'saved');
        return true;
      } catch (e) {
        if (e instanceof ApiError && e.code === ErrorCodes.ReviewChangedByColleague) {
          conflictRef.current = true;
          setConflict(e.message);
          setConflictOpen(true);
        } else {
          dirty.current = true;
          toast(errorText(e), { error: true });
        }
        setSaveState('error');
        return false;
      }
    })();
    inFlight.current = run;
    try {
      return await run;
    } finally {
      inFlight.current = null;
    }
  }, [toast, updateReview]);

  const changed = useCallback(() => {
    if (state.current.readOnly || conflictRef.current) return;
    dirty.current = true;
    setSaveState('dirty');
    clearTimeout(timer.current);
    timer.current = setTimeout(() => { void save(); }, 1000);
  }, [save]);

  // Уход со страницы / к другому студенту — дописать черновик; закрытие вкладки с несохранённым — предупредить
  useEffect(() => {
    const onUnload = (e: BeforeUnloadEvent) => { if (dirty.current) e.preventDefault(); };
    window.addEventListener('beforeunload', onUnload);
    return () => {
      window.removeEventListener('beforeunload', onUnload);
      if (dirty.current) void save();
    };
  }, [save]);

  // Поля формы — из проверки выбранной версии
  useEffect(() => {
    const r = sub ? reviews.current.get(sub.id) ?? null : null;
    setScores(Object.fromEntries(ctx.subtasks.filter(st => st.id !== null).map(st => {
      const v = r?.subtaskScores.find(x => x.subtaskId === st.id)?.score;
      return [st.id!, v === null || v === undefined ? '' : fmtScore(v)];
    })));
    setOverride(r?.totalOverride ?? null);
    setComment(r?.comment ?? '');
    annotations.current = r?.annotations || '[]';
    setSaveState(r ? 'saved' : 'none');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId]);

  async function selectVersion(s: SubmissionDto) {
    if (s.id === selectedId) return;
    await save();
    setSelectedId(s.id);
  }

  function setSubtaskScore(id: number, value: string) {
    setScores(sc => ({ ...sc, [id]: value }));
    changed();
  }

  function setTotal(v: number | null) {
    // Совпадает с суммой подзадач — значит, вручную не переопределяли
    setOverride(subtasks.length > 0 && v === sum ? null : v);
    changed();
  }

  async function send() {
    if (!sub) return;
    setSending(true);
    try {
      if (viewer.current) annotations.current = viewer.current.json();
      if (!reviews.current.get(sub.id)) dirty.current = true; // проверки ещё нет — сначала создать черновик
      if (!(await save())) return;
      updateReview(sub.id, await api.sendReview(sub.id));
      setSaveState('saved');
      setSentNote(n => n + 1);
    } catch (e) {
      toast(errorText(e), { error: true });
    } finally {
      setSending(false);
    }
  }

  async function download(kind: 'original' | 'reviewed') {
    if (!sub) return;
    try {
      const f = kind === 'original' ? await api.submissionFile(sub.id) : await api.reviewedFile(sub.id);
      saveBlob(f.blob, kind === 'original' ? f.fileName ?? sub.fileName : f.fileName ?? `проверка-v${sub.version}.pdf`);
    } catch (e) {
      toast(errorText(e), { error: true });
    }
  }

  const saveText: Record<SaveState, string> = {
    none: 'Черновика ещё нет', dirty: 'Есть изменения…', saving: 'Сохраняю…', saved: 'Сохранено', error: 'Не сохранено', invalid: 'Проверьте баллы',
  };
  const statusText = ctx.isReadOnly ? 'Курс в архиве — только просмотр'
    : !sub?.isLatest ? 'Старая версия — только просмотр'
    : saveText[saveState];

  if (!sub) {
    return (
      <>
        <Header ctx={ctx} nav={nav} navigate={navigate} sub={null} submissions={[]} onVersion={() => {}}
          right={<span className="text-muted">Студент ещё не сдал работу</span>} />
        <div className="stacked p-20-24-24"><Alert>Сдач по этому заданию нет</Alert></div>
      </>
    );
  }

  return (
    <>
      <Header ctx={ctx} nav={nav} navigate={navigate} sub={sub} submissions={submissions} onVersion={selectVersion}
        right={conflict ? (
          <span className="text-c-c1272d fw-700">Изменено коллегой · <button className="link-btn" onClick={() => onReload(sub.version)} type="button">Обновить</button></span>
        ) : sentView ? (
          // evaluated.html: «Сохранено» и «Перепроверить» перед переходами; PDF с пометками скачивается из панели при перепроверке
          <>
            <span className="text-muted" title={review?.sentByName ? `Отправил(а) ${review.sentByName}` : undefined}>Сохранено</span>
            <button className="btn-primary h-44" id="recheck-btn" onClick={() => setRecheck(true)} type="button">Перепроверить</button>
          </>
        ) : (
          <span className="text-muted" title={review ? `Черновик последним правил(а): ${review.updatedByName}` : undefined}>{statusText}</span>
        )} />

      <div className="stacked row flex-1 items-stretch gap-20 p-20-24-24 review-main">
        {!sentView && (
          <div className="glass w-76 flex-none d-flex flex-col items-center gap-10 p-16-0 rounded-26 review-tools" id="toolbar">
            {([
              ['text', 'Текст', <path key="t" d="M5 6h14M12 6v13M9 19h6" />],
              ['pen', 'Перо', <><path d="M4 20l1.2-4.2L16.5 4.5a2.1 2.1 0 0 1 3 3L8.2 18.8z" /><path d="M14.5 6.5l3 3M4 20l4.2-1.2" /></>],
              ['marker', 'Маркер', <path key="m" d="M9 14l6-6 3 3-6 6zM9 14l-3 4 4-1M4 21h8" />],
              ['eraser', 'Ластик', <path key="e" d="M16 5l4 4-9 9H7l-3-3zM9 18h11" />],
            ] as [Tool, string, ReactNode][]).map(([t, label, icon]) => (
              <button aria-label={label} aria-pressed={tool === t} className={`icon-btn tool-btn${tool === t ? ' is-active' : ''}`} disabled={readOnly}
                key={t} onClick={() => setTool(t)} title={label} type="button">
                <ToolIcon>{icon}</ToolIcon>
              </button>
            ))}
            <span className="h-1 w-30 bg-switch-off m-4-0" />
            {COLORS.map(c => (
              <button aria-label={`Цвет: ${c.name}`} aria-pressed={color === c.hex} className={`color-swatch color-swatch--${c.cls}${color === c.hex ? ' is-active' : ''}`}
                disabled={readOnly} key={c.cls} onClick={() => setColor(c.hex)} title={c.name} type="button" />
            ))}
          </div>
        )}

        <div className="flex-1 d-flex flex-col items-center gap-14 review-center">
          <div className="bg-surface w-full max-w-580 flex-1 p-40 rounded-18 bg-surface--x1 work-paper-box">
            <PdfPane key={sub.id} sub={sub} readOnly={readOnly || sentView} tool={toolOptions} viewer={viewer}
              onChange={json => { annotations.current = json; changed(); }}
              onHistory={() => {}}
              onScale={setScale} />
          </div>
          <div className="glass row p-6-12 rounded-16">
            <button aria-label="Уменьшить масштаб" className="btn-outline h-36 border-0" onClick={() => viewer.current?.zoom(0.8)} type="button">−</button>
            <button className="link-btn" onClick={() => viewer.current?.fitWidth()} title="По ширине" type="button">
              <b>{scale === null ? '—' : `${Math.round(scale * 100)}%`}</b>
            </button>
            <button aria-label="Увеличить масштаб" className="btn-outline h-36 border-0" onClick={() => viewer.current?.zoom(1.25)} type="button">+</button>
          </div>
        </div>

        {!sentView && (
          <aside className="glass w-380 flex-none p-28 d-flex flex-col gap-16 items-stretch bg-c-ffffffd9 pos-relative review-aside">
            <div className="text-muted fs-13 text-center">Сдано {shortDateTime(sub.submittedAt)} · дедлайн {shortDateTime(ctx.deadline)}</div>
            <div className="d-flex flex-col items-center gap-6">
              <ScoreDial max={maxTotal} onChange={setTotal} readOnly={readOnly} value={total} />
              {!readOnly && (
                <div className="text-muted fs-12 text-center">
                  {maxTotal ? 'Потяните ползунок по кольцу или нажмите на число, чтобы ввести балл' : 'Нажмите на число, чтобы ввести итог'}
                </div>
              )}
              {override !== null && subtasks.length > 0 && (
                <div className="fs-13 text-center">
                  <span className="text-muted">Итог введён вручную{sum !== null ? `, сумма — ${fmtScore(sum)}` : ''}. </span>
                  {!readOnly && <button className="link-btn" onClick={() => setTotal(null)} type="button">Вернуть сумму</button>}
                </div>
              )}
            </div>
            {sub.isLate && (
              <div className="d-flex justify-center">
                <span className="d-inline-flex items-center gap-8 bg-c-ffe4e8 text-c-c1272d rounded-999 p-7-14 fw-800 fs-13 d-inline-flex--x1">
                  <svg aria-hidden="true" fill="none" height="16" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" viewBox="0 0 24 24" width="16">
                    <circle cx="12" cy="12" r="9" />
                    <path d="M12 8v5M12 16h.01" />
                  </svg>
                  Работа сдана после дедлайна{sub.lateBy ? ` на ${lateText(sub.lateBy)}` : ''}!
                </span>
              </div>
            )}
            {subtasks.map(st => (
              <div className="row" key={st.id}>
                <span>{st.title}</span>
                <span className="spacer" />
                <input aria-label={`Балл: ${st.title}`} className={`input w-64 h-44 text-center${invalid.has(st.id!) ? ' is-error' : ''}`} disabled={readOnly}
                  inputMode="decimal" onChange={e => setSubtaskScore(st.id!, e.target.value)} placeholder="—"
                  title={invalid.has(st.id!) ? `От 0 до ${st.maxScore !== null ? fmtScore(st.maxScore) : '∞'}` : undefined} value={scores[st.id!] ?? ''} />
                {st.maxScore !== null && <span className="text-muted">/ {fmtScore(st.maxScore)}</span>}
              </div>
            ))}
            <label>Комментарий
              <textarea className="input h-100 p-12 fw-500" disabled={readOnly} maxLength={10000}
                onChange={e => { setComment(e.target.value); changed(); }} value={comment} />
            </label>
            {!readOnly && (
              <button className="btn-primary h-44 rounded-999 mt-4" disabled={sending || !!conflict} id="send-btn" onClick={send} type="button">
                {sending ? 'Отправляю…' : review?.sentAt ? 'Отправить обновлённую проверку' : 'Отправить студенту'}
              </button>
            )}
            <div aria-live="polite" className={`sent-note${sentNote ? ' is-animated' : ''}`} key={sentNote} id="sent-note">
              <svg aria-hidden="true" className="sent-note__check" fill="none" height="18" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.4" viewBox="0 0 24 24" width="18">
                <path d="M5 12.5l4.5 4.5L19 7.5" />
              </svg>
              <span>Отправлено!</span>
            </div>
            {review && (
              <div className="text-muted review-meta">
                <div>Черновик последним правил(а): {review.updatedByName}, {shortDateTime(review.updatedAt)}</div>
                {review.sentAt && <div>Отправил(а) {review.sentByName} {shortDateTime(review.sentAt)}</div>}
                {review.sentAt && review.hasUnsentChanges && <div className="text-c-c1272d fw-700">Есть неотправленные правки</div>}
              </div>
            )}
            <div className="row fs-13">
              <button className="link-btn" onClick={() => download('original')} type="button">Скачать оригинал</button>
              {review?.sentAt && <button className="link-btn" onClick={() => download('reviewed')} type="button">Скачать отправленный PDF</button>}
            </div>
          </aside>
        )}
      </div>

      <Modal open={conflictOpen} title="Проверку изменил коллега" onClose={() => setConflictOpen(false)}
        actions={<>
          <button className="btn-outline" onClick={() => setConflictOpen(false)} type="button">Остаться</button>
          <button className="btn-primary" onClick={() => onReload(sub.version)} type="button">Обновить</button>
        </>}>
        <div className="modal__text">{conflict} Ваши несохранённые правки при обновлении будут потеряны.</div>
      </Modal>
    </>
  );
}

const ToolIcon = ({ children }: { children: ReactNode }) => (
  <svg aria-hidden="true" fill="none" height="22" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.7" viewBox="0 0 24 24" width="22">{children}</svg>
);

function Header({ ctx, nav, navigate, sub, submissions, onVersion, right, after }: {
  ctx: ReviewContextDto; nav: Nav; navigate: (to: string) => void; sub: SubmissionDto | null; submissions: SubmissionDto[];
  onVersion: (s: SubmissionDto) => void; right: ReactNode; after?: ReactNode;
}) {
  const go = (id: number | null) => id !== null && navigate(`/review/${ctx.homeworkId}/${id}`);
  return (
    <div className="glass stacked review-header m-20-24-0 p-12-20 rounded-24">
      <div className="row gap-16">
        <Link className="text-brand-alt-strong fw-800 no-underline" to={nav.backTo}>← К заданию</Link>
        <span className="w-6" />
        <span className="review-header__title">
          <a className="homework-badge no-underline" href={ctx.homeworkLink || undefined} rel="noreferrer" target="_blank" title={`${ctx.homeworkTitle} — открыть условие`}><i />{parseTitle(ctx.homeworkTitle).code ?? ctx.homeworkTitle}</a>
          <b className="fs-17" id="student-name">{ctx.studentName}</b>
        </span>
      </div>
      <div className="segmented w-130" role="tablist">
        {submissions.map(s => (
          <span aria-selected={s.id === sub?.id} className={s.id === sub?.id ? 'is-active' : undefined} key={s.id}
            onClick={() => onVersion(s)} onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && onVersion(s)} role="tab" tabIndex={0}
            title={`Сдано ${shortDateTime(s.submittedAt)}`}>v{s.version}</span>
        ))}
      </div>
      <div className="row">
        {sub?.isLate && <span className="late-badge">Опоздание</span>}
        <span className="spacer" />
        {right}
        <button aria-label="Предыдущая работа" className="btn-outline h-44" disabled={nav.prev === null} id="prev-work" onClick={() => go(nav.prev)} type="button">
          <svg aria-hidden="true" fill="none" height="16" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" viewBox="0 0 24 24" width="16"><path d="M15 6l-6 6 6 6" /></svg>
          Предыдущая
        </button>
        <button aria-label="Следующая работа" className="btn-outline h-44" disabled={nav.next === null} id="next-work" onClick={() => go(nav.next)} type="button">
          Следующая
          <svg aria-hidden="true" fill="none" height="16" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" viewBox="0 0 24 24" width="16"><path d="M9 6l6 6-6 6" /></svg>
        </button>
        {after}
      </div>
    </div>
  );
}

// ── PDF выбранной версии со слоем пометок ─────────────────────────────

function PdfPane({ sub, readOnly, tool, viewer, onChange, onHistory, onScale }: {
  sub: SubmissionDto; readOnly: boolean; tool: ToolOptions; viewer: React.RefObject<PdfReviewer | null>;
  onChange: (json: string) => void; onHistory: (canUndo: boolean, canRedo: boolean) => void; onScale: (scale: number) => void;
}) {
  const box = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const events = useRef({ onChange, onHistory, onScale });
  events.current = { onChange, onHistory, onScale };
  const initial = useRef({ readOnly, tool, annotations: sub.review?.annotations });

  useEffect(() => {
    const v = new PdfReviewer(box.current!, {
      onChange: json => events.current.onChange(json),
      onHistory: (u, r) => events.current.onHistory(u, r),
      onScale: s => events.current.onScale(s),
    }, initial.current.readOnly);
    v.setTool(initial.current.tool);
    viewer.current = v;
    let alive = true;
    setLoading(true);
    api.submissionFile(sub.id)
      .then(f => f.blob.arrayBuffer())
      .then(buf => v.load(new Uint8Array(buf), initial.current.annotations))
      .then(() => { if (alive) setLoading(false); })
      .catch(e => { if (alive) { setError(errorText(e)); setLoading(false); } });
    return () => {
      alive = false;
      v.destroy();
      if (viewer.current === v) viewer.current = null;
    };
  }, [sub.id, viewer]);

  useEffect(() => { viewer.current?.setReadOnly(readOnly); }, [readOnly, viewer]);
  useEffect(() => { viewer.current?.setTool(tool); }, [tool, viewer]);

  return (
    <>
      {error && <Alert>Не удалось открыть PDF: {error}</Alert>}
      {loading && <Loading />}
      <div className="pdf-pages" ref={box} />
    </>
  );
}
