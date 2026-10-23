// Просмотр PDF (pdf.js) и слой пометок преподавателя: перо, маркер, текст, ластик, отмена/повтор.
// Пометки хранятся в координатах страницы PDF (пункты, начало — левый верхний угол) — не съезжают при масштабе,
// бэкенд впечатывает их в PDF теми же координатами.

let state = null;

export async function init(container, pdfBytes, annotationsJson, readOnly, dotnet) {
    destroy();
    const pdfjsLib = window.pdfjsLib;
    pdfjsLib.GlobalWorkerOptions.workerSrc = 'lib/pdfjs/pdf.worker.min.js';
    const pdf = await pdfjsLib.getDocument({ data: pdfBytes }).promise;

    state = {
        container, pdf, dotnet, readOnly,
        pages: [],
        annotations: safeParse(annotationsJson),
        undo: [], redo: [],
        tool: 'pen', color: '#d00000', width: 2, fontSize: 14,
        fit: true, scale: 1, drawing: null, editor: null,
    };
    container.innerHTML = '';
    container.dataset.tool = readOnly ? 'none' : state.tool;

    for (let i = 1; i <= pdf.numPages; i++) {
        const page = await pdf.getPage(i);
        const wrap = document.createElement('div');
        wrap.className = 'pdf-page';
        const canvas = document.createElement('canvas');
        const overlay = document.createElement('canvas');
        overlay.className = 'pdf-overlay';
        wrap.append(canvas, overlay);
        container.append(wrap);
        const p = { index: i - 1, page, base: page.getViewport({ scale: 1 }), wrap, canvas, overlay };
        state.pages.push(p);
        attach(p);
    }
    state.onResize = () => { if (state?.fit) render(); };
    window.addEventListener('resize', state.onResize);
    await render();
    return pdf.numPages;
}

export function setTool(tool, color, width, fontSize) {
    if (!state) return;
    commitEditor();
    Object.assign(state, { tool, color, width, fontSize });
    state.container.dataset.tool = state.readOnly ? 'none' : tool;
}

export function setReadOnly(readOnly) {
    if (!state) return;
    state.readOnly = readOnly;
    state.container.dataset.tool = readOnly ? 'none' : state.tool;
}

export function zoom(factor) {
    if (!state) return;
    state.fit = false;
    state.scale = Math.min(4, Math.max(0.3, currentScale() * factor));
    render();
}

export function fitWidth() {
    if (!state) return;
    state.fit = true;
    render();
}

export function undo() {
    if (!state || state.readOnly) return;
    const op = state.undo.pop();
    if (!op) return;
    revert(op);
    state.redo.push(op);
    changed();
}

export function redo() {
    if (!state || state.readOnly) return;
    const op = state.redo.pop();
    if (!op) return;
    apply(op);
    state.undo.push(op);
    changed();
}

export function clearPage(index) {
    if (!state || state.readOnly) return;
    const removed = state.annotations.map((a, i) => ({ a, i })).filter(x => x.a.page === index);
    if (removed.length === 0) return;
    state.annotations = state.annotations.filter(a => a.page !== index);
    pushUndo({ kind: 'batchRemove', items: removed });
    changed();
}

export function destroy() {
    if (!state) return;
    window.removeEventListener('resize', state.onResize);
    state.pdf?.destroy();
    state.container.innerHTML = '';
    state = null;
}

// ── внутреннее ──────────────────────────────────────────────────────

function safeParse(json) {
    try {
        const v = JSON.parse(json || '[]');
        return Array.isArray(v) ? v : [];
    } catch {
        return [];
    }
}

function currentScale() {
    if (!state.fit) return state.scale;
    const maxWidth = Math.max(...state.pages.map(p => p.base.width));
    return Math.max(0.3, (state.container.clientWidth - 32) / maxWidth);
}

async function render() {
    const scale = currentScale();
    state.scaleUsed = scale;
    const dpr = window.devicePixelRatio || 1;
    for (const p of state.pages) {
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
            canvasContext: p.canvas.getContext('2d'),
            viewport: vp,
            transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null,
        });
        p.renderTask.promise.catch(() => { });
        drawOverlay(p);
    }
}

function drawOverlay(p) {
    const ctx = p.overlay.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, p.overlay.width, p.overlay.height);
    ctx.setTransform(dpr * state.scaleUsed, 0, 0, dpr * state.scaleUsed, 0, 0);
    for (const a of state.annotations) {
        if (a.page !== p.index) continue;
        ctx.globalAlpha = a.opacity || 1;
        if (a.type === 'stroke') {
            const pts = a.points || [];
            ctx.strokeStyle = a.color;
            ctx.fillStyle = a.color;
            ctx.lineWidth = a.width;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            if (pts.length < 4) {
                ctx.beginPath();
                ctx.arc(pts[0], pts[1], a.width / 2, 0, Math.PI * 2);
                ctx.fill();
            } else {
                ctx.beginPath();
                ctx.moveTo(pts[0], pts[1]);
                for (let i = 2; i < pts.length; i += 2) ctx.lineTo(pts[i], pts[i + 1]);
                ctx.stroke();
            }
        } else if (a.type === 'text') {
            ctx.fillStyle = a.color;
            ctx.font = `${a.size}px "DejaVu Sans", Arial, sans-serif`;
            ctx.textBaseline = 'top';
            String(a.text).split('\n').forEach((line, i) => ctx.fillText(line, a.x, a.y + i * a.size * 1.2));
        }
    }
    ctx.globalAlpha = 1;
}

function toPdf(p, e) {
    const r = p.overlay.getBoundingClientRect();
    return { x: round((e.clientX - r.left) / state.scaleUsed), y: round((e.clientY - r.top) / state.scaleUsed) };
}

const round = v => Math.round(v * 10) / 10;

function attach(p) {
    const o = p.overlay;
    o.addEventListener('pointerdown', e => {
        if (!state || state.readOnly || e.button !== 0) return;
        const pt = toPdf(p, e);
        if (state.tool === 'pen' || state.tool === 'marker') {
            commitEditor();
            const marker = state.tool === 'marker';
            const a = {
                type: 'stroke', page: p.index, color: marker ? '#ffd400' : state.color,
                width: marker ? Math.max(8, state.width * 6) : state.width, opacity: marker ? 0.35 : 1,
                points: [pt.x, pt.y],
            };
            state.annotations.push(a);
            state.drawing = { p, a };
            o.setPointerCapture(e.pointerId);
            drawOverlay(p);
        } else if (state.tool === 'text') {
            e.preventDefault();
            commitEditor();
            openEditor(p, pt);
        } else if (state.tool === 'eraser') {
            const index = hitTest(p, pt);
            if (index >= 0) {
                const [a] = state.annotations.splice(index, 1);
                pushUndo({ kind: 'remove', a, index });
                changed();
            }
        }
    });
    o.addEventListener('pointermove', e => {
        if (!state?.drawing || state.drawing.p !== p) return;
        const pt = toPdf(p, e);
        const pts = state.drawing.a.points;
        if (Math.hypot(pts[pts.length - 2] - pt.x, pts[pts.length - 1] - pt.y) < 0.8) return;
        pts.push(pt.x, pt.y);
        drawOverlay(p);
    });
    const end = () => {
        if (!state?.drawing) return;
        const { a } = state.drawing;
        state.drawing = null;
        pushUndo({ kind: 'add', a });
        changed();
    };
    o.addEventListener('pointerup', end);
    o.addEventListener('pointercancel', end);
}

function openEditor(p, pt) {
    const ta = document.createElement('textarea');
    ta.className = 'pdf-text-editor';
    ta.style.left = `${pt.x * state.scaleUsed}px`;
    ta.style.top = `${pt.y * state.scaleUsed}px`;
    ta.style.color = state.color;
    ta.style.fontSize = `${state.fontSize * state.scaleUsed}px`;
    ta.placeholder = 'Текст… (Ctrl+Enter — готово, Esc — отмена)';
    p.wrap.append(ta);
    state.editor = { p, pt, ta };
    ta.addEventListener('keydown', e => {
        if (e.key === 'Escape') {
            ta.value = '';
            commitEditor();
        } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            commitEditor();
        }
    });
    ta.addEventListener('blur', () => setTimeout(commitEditor, 0));
    setTimeout(() => ta.focus(), 0);
}

function commitEditor() {
    const ed = state?.editor;
    if (!ed) return;
    state.editor = null;
    const text = ed.ta.value.trimEnd();
    ed.ta.remove();
    if (!text) return;
    const a = { type: 'text', page: ed.p.index, x: ed.pt.x, y: ed.pt.y, text, color: state.color, size: state.fontSize, opacity: 1 };
    state.annotations.push(a);
    pushUndo({ kind: 'add', a });
    changed();
}

function hitTest(p, pt) {
    for (let i = state.annotations.length - 1; i >= 0; i--) {
        const a = state.annotations[i];
        if (a.page !== p.index) continue;
        if (a.type === 'stroke') {
            const tol = Math.max(6 / state.scaleUsed, a.width / 2 + 2);
            const pts = a.points;
            if (pts.length < 4 && Math.hypot(pts[0] - pt.x, pts[1] - pt.y) <= tol) return i;
            for (let j = 2; j < pts.length; j += 2)
                if (segmentDistance(pt, pts[j - 2], pts[j - 1], pts[j], pts[j + 1]) <= tol) return i;
        } else {
            const lines = String(a.text).split('\n');
            const w = Math.max(...lines.map(l => l.length)) * a.size * 0.6;
            const h = lines.length * a.size * 1.2;
            if (pt.x >= a.x - 3 && pt.x <= a.x + w + 3 && pt.y >= a.y - 3 && pt.y <= a.y + h + 3) return i;
        }
    }
    return -1;
}

function segmentDistance(p, x1, y1, x2, y2) {
    const dx = x2 - x1, dy = y2 - y1;
    const len = dx * dx + dy * dy;
    const t = len === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - x1) * dx + (p.y - y1) * dy) / len));
    return Math.hypot(p.x - (x1 + t * dx), p.y - (y1 + t * dy));
}

function pushUndo(op) {
    state.undo.push(op);
    state.redo = [];
}

function apply(op) {
    if (op.kind === 'add') state.annotations.push(op.a);
    else if (op.kind === 'remove') state.annotations.splice(state.annotations.indexOf(op.a), 1);
    else if (op.kind === 'batchRemove') state.annotations = state.annotations.filter(a => !op.items.some(x => x.a === a));
}

function revert(op) {
    if (op.kind === 'add') state.annotations.splice(state.annotations.indexOf(op.a), 1);
    else if (op.kind === 'remove') state.annotations.splice(Math.min(op.index, state.annotations.length), 0, op.a);
    else if (op.kind === 'batchRemove') for (const x of op.items) state.annotations.splice(Math.min(x.i, state.annotations.length), 0, x.a);
}

function changed() {
    for (const p of state.pages) drawOverlay(p);
    state.dotnet.invokeMethodAsync('OnAnnotationsChanged', JSON.stringify(state.annotations));
}
