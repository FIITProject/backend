// Просмотр PDF (pdfjs-dist) и слой пометок преподавателя: перо, маркер, текст, ластик, отмена/повтор.
// Перенос web/Fiit.Web/wwwroot/js/review.js. Пометки хранятся в координатах страницы PDF (пункты, начало — левый
// верхний угол, как viewport pdf.js при scale = 1) — не съезжают при масштабе, бэкенд (PdfAnnotator) впечатывает их
// в PDF теми же координатами.
import {
  getDocument, GlobalWorkerOptions, type PageViewport, type PDFDocumentLoadingTask, type PDFPageProxy, type RenderTask,
} from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

GlobalWorkerOptions.workerSrc = workerUrl;

/** Формат Fiit.Api.Services.Annotation (JSON camelCase). */
export interface Annotation {
  type: 'stroke' | 'text';
  page: number;
  color: string;
  opacity: number;
  /** stroke */
  width?: number;
  points?: number[];
  /** text: левый верхний угол первой строки */
  x?: number;
  y?: number;
  text?: string;
  size?: number;
}

export type Tool = 'pen' | 'marker' | 'text' | 'eraser';

export interface ToolOptions { tool: Tool; color: string; width: number; fontSize: number }

type Op =
  | { kind: 'add'; a: Annotation }
  | { kind: 'remove'; a: Annotation; index: number }
  | { kind: 'batchRemove'; items: { a: Annotation; i: number }[] };

interface Page {
  index: number;
  page: PDFPageProxy;
  base: PageViewport;
  wrap: HTMLDivElement;
  canvas: HTMLCanvasElement;
  overlay: HTMLCanvasElement;
  renderTask?: RenderTask;
}

const MARKER_COLOR = '#FFD400';
const round = (v: number) => Math.round(v * 10) / 10;

export function parseAnnotations(json: string | null | undefined): Annotation[] {
  try {
    const v = JSON.parse(json || '[]');
    return Array.isArray(v) ? v : [];
  } catch {
    return [];
  }
}

export class PdfReviewer {
  private loadingTask: PDFDocumentLoadingTask | null = null;
  private pages: Page[] = [];
  private annotations: Annotation[] = [];
  private undoStack: Op[] = [];
  private redoStack: Op[] = [];
  private opts: ToolOptions = { tool: 'pen', color: '#E5484D', width: 2, fontSize: 14 };
  private fit = true;
  private scale = 1;
  private scaleUsed = 1;
  private drawing: { p: Page; a: Annotation } | null = null;
  private editor: { p: Page; x: number; y: number; ta: HTMLTextAreaElement } | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private destroyed = false;

  constructor(
    private readonly container: HTMLElement,
    private readonly events: {
      /** Пометки изменились (JSON для SaveReviewRequest.annotations). */
      onChange: (json: string) => void;
      /** Масштаб пересчитан (1 = 100%: пункт PDF = CSS-пиксель). */
      onScale?: (scale: number) => void;
      /** Изменились стеки отмены/повтора. */
      onHistory?: (canUndo: boolean, canRedo: boolean) => void;
    },
    private readOnly: boolean,
  ) {}

  async load(data: Uint8Array, annotationsJson: string | null | undefined): Promise<number> {
    this.annotations = parseAnnotations(annotationsJson);
    const task = getDocument({ data });
    this.loadingTask = task;
    const pdf = await task.promise;
    if (this.destroyed) return 0;
    this.container.innerHTML = '';
    this.updateCursor();
    for (let i = 1; i <= pdf.numPages; i++) {
      const page = await pdf.getPage(i);
      if (this.destroyed) return 0;
      const wrap = document.createElement('div');
      wrap.className = 'pdf-page';
      const canvas = document.createElement('canvas');
      const overlay = document.createElement('canvas');
      overlay.className = 'pdf-overlay';
      wrap.append(canvas, overlay);
      this.container.append(wrap);
      const p: Page = { index: i - 1, page, base: page.getViewport({ scale: 1 }), wrap, canvas, overlay };
      this.pages.push(p);
      this.attach(p);
    }
    let lastWidth = this.container.clientWidth;
    this.resizeObserver = new ResizeObserver(() => {
      const w = this.container.clientWidth;
      if (this.fit && w !== lastWidth) {
        lastWidth = w;
        this.render();
      }
    });
    this.resizeObserver.observe(this.container);
    this.render();
    this.history();
    return pdf.numPages;
  }

  setTool(opts: ToolOptions) {
    this.commitEditor();
    this.opts = { ...opts };
    this.updateCursor();
  }

  setReadOnly(readOnly: boolean) {
    if (readOnly) this.commitEditor();
    this.readOnly = readOnly;
    this.updateCursor();
  }

  zoom(factor: number) {
    if (!this.pages.length) return;
    this.fit = false;
    this.scale = Math.min(4, Math.max(0.3, this.currentScale() * factor));
    this.render();
  }

  fitWidth() {
    this.fit = true;
    this.render();
  }

  undo() {
    if (this.readOnly) return;
    this.commitEditor();
    const op = this.undoStack.pop();
    if (!op) return;
    this.revert(op);
    this.redoStack.push(op);
    this.changed();
  }

  redo() {
    if (this.readOnly) return;
    const op = this.redoStack.pop();
    if (!op) return;
    this.apply(op);
    this.undoStack.push(op);
    this.changed();
  }

  /** Текущий JSON пометок (с учётом открытого текстового поля). */
  json(): string {
    this.commitEditor();
    return JSON.stringify(this.annotations);
  }

  destroy() {
    this.destroyed = true;
    this.resizeObserver?.disconnect();
    for (const p of this.pages) p.renderTask?.cancel();
    void this.loadingTask?.destroy();
    this.loadingTask = null;
    this.container.innerHTML = '';
    this.pages = [];
  }

  // ── внутреннее ──────────────────────────────────────────────────────

  private updateCursor() {
    this.container.dataset.tool = this.readOnly ? 'none' : this.opts.tool;
  }

  private currentScale() {
    if (!this.fit) return this.scale;
    const maxWidth = Math.max(...this.pages.map(p => p.base.width));
    return Math.max(0.3, (this.container.clientWidth - 8) / maxWidth);
  }

  private render() {
    if (!this.pages.length) return;
    const scale = this.currentScale();
    this.scaleUsed = scale;
    this.events.onScale?.(scale);
    const dpr = window.devicePixelRatio || 1;
    for (const p of this.pages) {
      const vp = p.page.getViewport({ scale });
      for (const c of [p.canvas, p.overlay]) {
        c.width = Math.floor(vp.width * dpr);
        c.height = Math.floor(vp.height * dpr);
        c.style.width = `${vp.width}px`;
        c.style.height = `${vp.height}px`;
      }
      p.wrap.style.width = `${vp.width}px`;
      p.wrap.style.height = `${vp.height}px`;
      p.renderTask?.cancel();
      p.renderTask = p.page.render({
        canvas: p.canvas,
        viewport: vp,
        transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : undefined,
      });
      p.renderTask.promise.catch(() => { /* отменён новым рендером */ });
      if (this.editor?.p === p) this.placeEditor();
      this.drawOverlay(p);
    }
  }

  private drawOverlay(p: Page) {
    const ctx = p.overlay.getContext('2d');
    if (!ctx) return;
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, p.overlay.width, p.overlay.height);
    ctx.setTransform(dpr * this.scaleUsed, 0, 0, dpr * this.scaleUsed, 0, 0);
    for (const a of this.annotations) {
      if (a.page !== p.index) continue;
      ctx.globalAlpha = a.opacity || 1;
      if (a.type === 'stroke') {
        const pts = a.points ?? [];
        if (pts.length < 2) continue;
        const width = a.width || 1;
        ctx.strokeStyle = a.color;
        ctx.fillStyle = a.color;
        ctx.lineWidth = width;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.beginPath();
        if (pts.length < 4) {
          ctx.arc(pts[0], pts[1], width / 2, 0, Math.PI * 2);
          ctx.fill();
        } else {
          ctx.moveTo(pts[0], pts[1]);
          for (let i = 2; i + 1 < pts.length; i += 2) ctx.lineTo(pts[i], pts[i + 1]);
          ctx.stroke();
        }
      } else if (a.type === 'text') {
        const size = a.size || 14;
        ctx.fillStyle = a.color;
        ctx.font = `${size}px "DejaVu Sans", Arial, sans-serif`;
        ctx.textBaseline = 'top';
        String(a.text ?? '').split('\n').forEach((line, i) => ctx.fillText(line, a.x ?? 0, (a.y ?? 0) + i * size * 1.2));
      }
    }
    ctx.globalAlpha = 1;
  }

  private toPdf(p: Page, e: PointerEvent) {
    const r = p.overlay.getBoundingClientRect();
    return { x: round((e.clientX - r.left) / this.scaleUsed), y: round((e.clientY - r.top) / this.scaleUsed) };
  }

  private attach(p: Page) {
    const o = p.overlay;
    o.addEventListener('pointerdown', e => {
      if (this.readOnly || e.button !== 0) return;
      const pt = this.toPdf(p, e);
      const { tool } = this.opts;
      if (tool === 'pen' || tool === 'marker') {
        this.commitEditor();
        const marker = tool === 'marker';
        const a: Annotation = {
          type: 'stroke', page: p.index, color: marker ? MARKER_COLOR : this.opts.color,
          width: marker ? Math.max(8, this.opts.width * 6) : this.opts.width, opacity: marker ? 0.35 : 1,
          points: [pt.x, pt.y],
        };
        this.annotations.push(a);
        this.drawing = { p, a };
        o.setPointerCapture(e.pointerId);
        this.drawOverlay(p);
      } else if (tool === 'text') {
        e.preventDefault();
        this.commitEditor();
        this.openEditor(p, pt.x, pt.y);
      } else if (tool === 'eraser') {
        const index = this.hitTest(p, pt);
        if (index >= 0) {
          const [a] = this.annotations.splice(index, 1);
          this.pushUndo({ kind: 'remove', a, index });
          this.changed();
        }
      }
    });
    o.addEventListener('pointermove', e => {
      if (this.drawing?.p !== p) return;
      const pt = this.toPdf(p, e);
      const pts = this.drawing.a.points!;
      if (Math.hypot(pts[pts.length - 2] - pt.x, pts[pts.length - 1] - pt.y) < 0.8) return;
      pts.push(pt.x, pt.y);
      this.drawOverlay(p);
    });
    const end = () => {
      if (!this.drawing) return;
      const { a } = this.drawing;
      this.drawing = null;
      this.pushUndo({ kind: 'add', a });
      this.changed();
    };
    o.addEventListener('pointerup', end);
    o.addEventListener('pointercancel', end);
  }

  private openEditor(p: Page, x: number, y: number) {
    const ta = document.createElement('textarea');
    ta.className = 'pdf-text-editor';
    ta.placeholder = 'Текст… (Ctrl+Enter — готово, Esc — отмена)';
    ta.style.color = this.opts.color;
    p.wrap.append(ta);
    this.editor = { p, x, y, ta };
    this.placeEditor();
    ta.addEventListener('keydown', e => {
      if (e.key === 'Escape') {
        ta.value = '';
        this.commitEditor();
      } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        this.commitEditor();
      }
    });
    ta.addEventListener('blur', () => setTimeout(() => this.commitEditor(), 0));
    setTimeout(() => ta.focus(), 0);
  }

  private placeEditor() {
    const ed = this.editor;
    if (!ed) return;
    ed.ta.style.left = `${ed.x * this.scaleUsed}px`;
    ed.ta.style.top = `${ed.y * this.scaleUsed}px`;
    ed.ta.style.fontSize = `${this.opts.fontSize * this.scaleUsed}px`;
  }

  private commitEditor() {
    const ed = this.editor;
    if (!ed) return;
    this.editor = null;
    const text = ed.ta.value.trimEnd();
    ed.ta.remove();
    if (!text) return;
    const a: Annotation = { type: 'text', page: ed.p.index, x: ed.x, y: ed.y, text, color: this.opts.color, size: this.opts.fontSize, opacity: 1 };
    this.annotations.push(a);
    this.pushUndo({ kind: 'add', a });
    this.changed();
  }

  private hitTest(p: Page, pt: { x: number; y: number }) {
    for (let i = this.annotations.length - 1; i >= 0; i--) {
      const a = this.annotations[i];
      if (a.page !== p.index) continue;
      if (a.type === 'stroke') {
        const tol = Math.max(6 / this.scaleUsed, (a.width || 1) / 2 + 2);
        const pts = a.points ?? [];
        if (pts.length < 2) continue;
        if (pts.length < 4 && Math.hypot(pts[0] - pt.x, pts[1] - pt.y) <= tol) return i;
        for (let j = 2; j + 1 < pts.length; j += 2)
          if (segmentDistance(pt, pts[j - 2], pts[j - 1], pts[j], pts[j + 1]) <= tol) return i;
      } else {
        const size = a.size || 14;
        const lines = String(a.text ?? '').split('\n');
        const w = Math.max(...lines.map(l => l.length)) * size * 0.6;
        const h = lines.length * size * 1.2;
        const x = a.x ?? 0, y = a.y ?? 0;
        if (pt.x >= x - 3 && pt.x <= x + w + 3 && pt.y >= y - 3 && pt.y <= y + h + 3) return i;
      }
    }
    return -1;
  }

  private pushUndo(op: Op) {
    this.undoStack.push(op);
    this.redoStack = [];
  }

  private apply(op: Op) {
    if (op.kind === 'add') this.annotations.push(op.a);
    else if (op.kind === 'remove') this.annotations.splice(this.annotations.indexOf(op.a), 1);
    else this.annotations = this.annotations.filter(a => !op.items.some(x => x.a === a));
  }

  private revert(op: Op) {
    if (op.kind === 'add') this.annotations.splice(this.annotations.indexOf(op.a), 1);
    else if (op.kind === 'remove') this.annotations.splice(Math.min(op.index, this.annotations.length), 0, op.a);
    else for (const x of op.items) this.annotations.splice(Math.min(x.i, this.annotations.length), 0, x.a);
  }

  private history() {
    this.events.onHistory?.(this.undoStack.length > 0, this.redoStack.length > 0);
  }

  private changed() {
    for (const p of this.pages) this.drawOverlay(p);
    this.history();
    this.events.onChange(JSON.stringify(this.annotations));
  }
}

function segmentDistance(p: { x: number; y: number }, x1: number, y1: number, x2: number, y2: number) {
  const dx = x2 - x1, dy = y2 - y1;
  const len = dx * dx + dy * dy;
  const t = len === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - x1) * dx + (p.y - y1) * dy) / len));
  return Math.hypot(p.x - (x1 + t * dx), p.y - (y1 + t * dy));
}
