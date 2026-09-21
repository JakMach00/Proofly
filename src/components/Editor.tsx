import { useCallback, useEffect, useRef, useState } from 'react';
import { canvasToBlob, loadImage, makeThumb, uid } from '../lib/capture';
import type { Annotation, Rect, ShapeAnnotation, ShapeType, Shot, ToolId } from '../types';

interface Props {
  shot: Shot;
  index: number;
  total: number;
  onSave: (blob: Blob, thumbUrl: string) => void;
  onClose: () => void;
  onNavigate: (delta: number) => void;
}

const COLORS = ['#ff3b30', '#ffcc00', '#34c759', '#0a84ff', '#ffffff', '#111111'];
const DEFAULT_COLOR = '#ff3b30';
const HIGHLIGHT_COLOR = '#ffcc00';
const FONTS = [
  { id: 'Arial, sans-serif', label: 'Arial' },
  { id: '"Segoe UI", system-ui, sans-serif', label: 'Segoe UI' },
  { id: 'Georgia, serif', label: 'Georgia' },
  { id: '"Cascadia Mono", Consolas, monospace', label: 'Monospace' },
  { id: 'Impact, "Arial Black", sans-serif', label: 'Impact' },
];
const TOOLS: { id: ToolId; label: string }[] = [
  { id: 'select', label: 'Select' },
  { id: 'arrow', label: 'Arrow' },
  { id: 'rect', label: 'Box' },
  { id: 'ellipse', label: 'Ellipse' },
  { id: 'step', label: 'Step' },
  { id: 'text', label: 'Text' },
  { id: 'highlight', label: 'Highlight' },
  { id: 'redact', label: 'Redact' },
  { id: 'crop', label: 'Crop' },
];

/** Handle radius in screen pixels, converted to image pixels when hit tested. */
const HANDLE_SCREEN_RADIUS = 6;

type PendingNav = { kind: 'nav'; delta: number } | { kind: 'close' } | null;

/** One undo step. The crop is part of it, so undo also reverses a crop. */
interface Snapshot {
  annotations: Annotation[];
  nextStep: number;
  crop: Rect | null;
  /** Whether the editor was clean before this step, restored on undo. */
  dirty: boolean;
}

function isShape(a: Annotation): a is ShapeAnnotation {
  return a.type !== 'step' && a.type !== 'text';
}

function stepRadius(width: number): number {
  return Math.round(width * 4 + 12);
}

function textLines(a: { text: string }): string[] {
  return (a.text || ' ').split('\n');
}

function handlePoints(a: Annotation): { id: string; x: number; y: number }[] {
  if (!isShape(a)) return [];
  if (a.type === 'arrow') {
    return [
      { id: 'p1', x: a.x1, y: a.y1 },
      { id: 'p2', x: a.x2, y: a.y2 },
    ];
  }
  return [
    { id: 'x1y1', x: a.x1, y: a.y1 },
    { id: 'x2y1', x: a.x2, y: a.y1 },
    { id: 'x1y2', x: a.x1, y: a.y2 },
    { id: 'x2y2', x: a.x2, y: a.y2 },
  ];
}

function moveHandle(a: ShapeAnnotation, id: string, p: { x: number; y: number }): ShapeAnnotation {
  switch (id) {
    case 'p1':
    case 'x1y1':
      return { ...a, x1: p.x, y1: p.y };
    case 'p2':
    case 'x2y2':
      return { ...a, x2: p.x, y2: p.y };
    case 'x2y1':
      return { ...a, x2: p.x, y1: p.y };
    case 'x1y2':
      return { ...a, x1: p.x, y2: p.y };
    default:
      return a;
  }
}

function drawArrow(ctx: CanvasRenderingContext2D, a: ShapeAnnotation) {
  const head = Math.max(10, a.width * 4);
  const angle = Math.atan2(a.y2 - a.y1, a.x2 - a.x1);
  const length = Math.hypot(a.x2 - a.x1, a.y2 - a.y1);
  const bodyEnd = Math.max(0, length - head * 0.9);
  const ex = a.x1 + Math.cos(angle) * bodyEnd;
  const ey = a.y1 + Math.sin(angle) * bodyEnd;

  ctx.strokeStyle = a.color;
  ctx.fillStyle = a.color;
  ctx.lineWidth = a.width;
  ctx.lineCap = 'round';
  ctx.beginPath();
  ctx.moveTo(a.x1, a.y1);
  ctx.lineTo(ex, ey);
  ctx.stroke();

  ctx.beginPath();
  ctx.moveTo(a.x2, a.y2);
  ctx.lineTo(
    a.x2 - Math.cos(angle - Math.PI / 7) * head,
    a.y2 - Math.sin(angle - Math.PI / 7) * head,
  );
  ctx.lineTo(
    a.x2 - Math.cos(angle + Math.PI / 7) * head,
    a.y2 - Math.sin(angle + Math.PI / 7) * head,
  );
  ctx.closePath();
  ctx.fill();
}

function drawAnnotation(ctx: CanvasRenderingContext2D, a: Annotation) {
  ctx.save();
  if (a.type === 'step') {
    const r = a.radius;
    ctx.fillStyle = a.color;
    ctx.beginPath();
    ctx.arc(a.x, a.y, r, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = Math.max(2, r * 0.12);
    ctx.stroke();
    ctx.fillStyle = '#ffffff';
    ctx.font = `bold ${Math.round(r * 1.25)}px Arial, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(String(a.n), a.x, a.y + r * 0.05);
  } else if (a.type === 'text') {
    ctx.font = `bold ${a.size}px ${a.font}`;
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    const lineHeight = a.size * 1.25;
    textLines(a).forEach((line, i) => {
      const y = a.y + i * lineHeight;
      if (a.outline) {
        ctx.lineWidth = Math.max(3, a.size * 0.16);
        ctx.strokeStyle = 'rgba(0,0,0,0.7)';
        ctx.lineJoin = 'round';
        ctx.strokeText(line, a.x, y);
      }
      ctx.fillStyle = a.color;
      ctx.fillText(line, a.x, y);
    });
  } else {
    const x = Math.min(a.x1, a.x2);
    const y = Math.min(a.y1, a.y2);
    const w = Math.abs(a.x2 - a.x1);
    const h = Math.abs(a.y2 - a.y1);
    if (a.type === 'arrow') {
      drawArrow(ctx, a);
    } else if (a.type === 'rect') {
      ctx.strokeStyle = a.color;
      ctx.lineWidth = a.width;
      ctx.strokeRect(x, y, w, h);
    } else if (a.type === 'ellipse') {
      ctx.strokeStyle = a.color;
      ctx.lineWidth = a.width;
      ctx.beginPath();
      ctx.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
      ctx.stroke();
    } else if (a.type === 'highlight') {
      ctx.globalAlpha = 0.32;
      ctx.fillStyle = a.color;
      ctx.fillRect(x, y, w, h);
    } else if (a.type === 'redact') {
      // A solid block, no stroke: this is a cover, not an outline.
      ctx.fillStyle = '#000000';
      ctx.fillRect(x, y, w, h);
    }
  }
  ctx.restore();
}

function bounds(ctx: CanvasRenderingContext2D, a: Annotation) {
  if (a.type === 'step') {
    return { x: a.x - a.radius, y: a.y - a.radius, w: a.radius * 2, h: a.radius * 2 };
  }
  if (a.type === 'text') {
    ctx.save();
    ctx.font = `bold ${a.size}px ${a.font}`;
    const lines = textLines(a);
    const w = Math.max(...lines.map((line) => ctx.measureText(line || ' ').width));
    ctx.restore();
    return { x: a.x, y: a.y, w, h: lines.length * a.size * 1.25 };
  }
  const pad = a.width + 6;
  return {
    x: Math.min(a.x1, a.x2) - pad,
    y: Math.min(a.y1, a.y2) - pad,
    w: Math.abs(a.x2 - a.x1) + pad * 2,
    h: Math.abs(a.y2 - a.y1) + pad * 2,
  };
}

export default function Editor({ shot, index, total, onSave, onClose, onNavigate }: Props) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const imageRef = useRef<HTMLImageElement | null>(null);
  const dragRef = useRef<{
    mode: 'create' | 'move' | 'handle' | 'crop';
    id: string;
    handle?: string;
    ox: number;
    oy: number;
    /** Set once the pointer actually moves during the gesture. */
    moved?: boolean;
    /** The gesture placed this annotation, so it is a change even unmoved. */
    created?: boolean;
  } | null>(null);
  const textEditRef = useRef<string | null>(null);
  /** The field laid over the canvas while a text label is being edited. */
  const inlineRef = useRef<HTMLTextAreaElement | null>(null);
  /**
   * A text box placed by the current edit session, with the undo index of the
   * state before it. Leaving it empty rolls the placement back entirely.
   */
  const placedTextRef = useRef<{ id: string; historyIndex: number } | null>(null);
  /**
   * Mirrors editingId synchronously. Removing a focused field can fire a second
   * blur, and without this the empty-box rollback would run twice.
   */
  const editingRef = useRef<string | null>(null);

  const [annotations, setAnnotations] = useState<Annotation[]>([]);
  const [history, setHistory] = useState<Snapshot[]>([]);
  const [crop, setCrop] = useState<Rect | null>(null);
  const [cropDraft, setCropDraft] = useState<Rect | null>(null);
  const [tool, setTool] = useState<ToolId>('arrow');
  const [color, setColor] = useState(DEFAULT_COLOR);
  const [width, setWidth] = useState(5);
  const [fontSize, setFontSize] = useState(34);
  const [font, setFont] = useState(FONTS[0].id);
  const [outline, setOutline] = useState(true);
  const [nextStep, setNextStep] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [pendingNav, setPendingNav] = useState<PendingNav>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [, setLayoutTick] = useState(0);

  const selected = annotations.find((a) => a.id === selectedId) || null;

  /** Records the state before a change so undo can step back through it. */
  const push = useCallback(
    (coalesceKey?: string) => {
      if (coalesceKey && textEditRef.current === coalesceKey) return;
      textEditRef.current = coalesceKey ?? null;
      setHistory((prev) => [...prev.slice(-49), { annotations, nextStep, crop, dirty }]);
      setDirty(true);
    },
    [annotations, nextStep, crop, dirty],
  );

  const viewScale = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return 1;
    const box = canvas.getBoundingClientRect();
    if (box.width === 0) return 1;
    return canvas.width / box.width;
  }, []);

  const redraw = useCallback(
    (withSelection: boolean) => {
      const canvas = canvasRef.current;
      const img = imageRef.current;
      if (!canvas || !img) return;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;

      const area = crop ?? { x: 0, y: 0, w: img.naturalWidth, h: img.naturalHeight };
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, area.x, area.y, area.w, area.h, 0, 0, canvas.width, canvas.height);
      // Annotations are kept in the coordinates of the original image, so a
      // crop never has to move them.
      ctx.translate(-area.x, -area.y);
      // While a label is edited in place the field over the canvas shows it,
      // so it is not painted twice. Saving passes false and paints everything.
      for (const a of annotations) {
        if (withSelection && a.id === editingId) continue;
        drawAnnotation(ctx, a);
      }

      if (!withSelection) {
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        return;
      }

      const scale = viewScale();
      if (cropDraft) {
        ctx.save();
        ctx.strokeStyle = '#00e5ff';
        ctx.setLineDash([9 * scale, 6 * scale]);
        ctx.lineWidth = 2 * scale;
        ctx.strokeRect(cropDraft.x, cropDraft.y, cropDraft.w, cropDraft.h);
        ctx.restore();
      }

      const sel = annotations.find((a) => a.id === selectedId && a.id !== editingId);
      if (sel) {
        const b = bounds(ctx, sel);
        ctx.save();
        ctx.strokeStyle = '#00e5ff';
        ctx.setLineDash([8 * scale, 6 * scale]);
        ctx.lineWidth = 1.5 * scale;
        ctx.strokeRect(b.x, b.y, b.w, b.h);
        ctx.setLineDash([]);
        const r = HANDLE_SCREEN_RADIUS * scale;
        for (const point of handlePoints(sel)) {
          ctx.beginPath();
          ctx.arc(point.x, point.y, r, 0, Math.PI * 2);
          ctx.fillStyle = '#ffffff';
          ctx.fill();
          ctx.lineWidth = 1.5 * scale;
          ctx.strokeStyle = '#00e5ff';
          ctx.stroke();
        }
        ctx.restore();
      }
      ctx.setTransform(1, 0, 0, 1, 0, 0);
    },
    [annotations, selectedId, crop, cropDraft, viewScale, editingId],
  );

  useEffect(() => {
    let cancelled = false;
    setReady(false);
    setAnnotations([]);
    setHistory([]);
    setCrop(null);
    setCropDraft(null);
    setSelectedId(null);
    setNextStep(1);
    setDirty(false);
    setEditingId(null);
    editingRef.current = null;
    placedTextRef.current = null;
    loadImage(shot.url)
      .then((img) => {
        if (cancelled) return;
        imageRef.current = img;
        const canvas = canvasRef.current;
        if (canvas) {
          canvas.width = img.naturalWidth;
          canvas.height = img.naturalHeight;
        }
        setReady(true);
      })
      .catch(() => setReady(false));
    return () => {
      cancelled = true;
    };
  }, [shot.url]);

  // Resizing the canvas has to happen before the next paint, not during it.
  useEffect(() => {
    const canvas = canvasRef.current;
    const img = imageRef.current;
    if (!canvas || !img || !ready) return;
    const area = crop ?? { x: 0, y: 0, w: img.naturalWidth, h: img.naturalHeight };
    if (canvas.width !== Math.round(area.w) || canvas.height !== Math.round(area.h)) {
      canvas.width = Math.round(area.w);
      canvas.height = Math.round(area.h);
    }
    redraw(true);
  }, [ready, crop, redraw]);

  const toImage = (event: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const img = imageRef.current;
    if (!canvas || !img) return { x: 0, y: 0 };
    const box = canvas.getBoundingClientRect();
    const area = crop ?? { x: 0, y: 0, w: img.naturalWidth, h: img.naturalHeight };
    return {
      x: area.x + ((event.clientX - box.left) / box.width) * canvas.width,
      y: area.y + ((event.clientY - box.top) / box.height) * canvas.height,
    };
  };

  const hitHandle = (x: number, y: number): string | null => {
    if (!selected) return null;
    const r = HANDLE_SCREEN_RADIUS * viewScale() * 1.6;
    for (const point of handlePoints(selected)) {
      if (Math.hypot(point.x - x, point.y - y) <= r) return point.id;
    }
    return null;
  };

  const hitTest = (x: number, y: number): Annotation | null => {
    const ctx = canvasRef.current?.getContext('2d');
    if (!ctx) return null;
    for (let i = annotations.length - 1; i >= 0; i -= 1) {
      const b = bounds(ctx, annotations[i]);
      if (x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h) return annotations[i];
    }
    return null;
  };

  const handleDown = (event: React.MouseEvent<HTMLCanvasElement>) => {
    if (!ready) return;
    // Clicking away from a label being edited commits it and does nothing
    // else, the way a text box in any drawing program behaves.
    if (editingId) {
      event.preventDefault();
      finishEditing();
      return;
    }
    const p = toImage(event);

    if (tool === 'crop') {
      setCropDraft({ x: p.x, y: p.y, w: 0, h: 0 });
      dragRef.current = { mode: 'crop', id: 'crop', ox: p.x, oy: p.y };
      return;
    }

    const handle = hitHandle(p.x, p.y);
    if (handle && selected) {
      push();
      dragRef.current = { mode: 'handle', id: selected.id, handle, ox: p.x, oy: p.y };
      return;
    }

    if (tool === 'select') {
      const hit = hitTest(p.x, p.y);
      setSelectedId(hit ? hit.id : null);
      if (hit) {
        push();
        dragRef.current = { mode: 'move', id: hit.id, ox: p.x, oy: p.y };
      }
      return;
    }

    // Clicking an existing step or text label with the same tool picks it up
    // instead of stacking another one on top.
    if (tool === 'step' || tool === 'text') {
      const existing = hitTest(p.x, p.y);
      if (existing && existing.type === tool) {
        setSelectedId(existing.id);
        push();
        dragRef.current = { mode: 'move', id: existing.id, ox: p.x, oy: p.y };
        return;
      }
    }

    push();

    if (tool === 'step') {
      const a: Annotation = {
        id: uid(),
        type: 'step',
        color,
        width,
        x: p.x,
        y: p.y,
        n: nextStep,
        radius: stepRadius(width),
      };
      setAnnotations((prev) => [...prev, a]);
      setNextStep((n) => n + 1);
      setSelectedId(a.id);
      dragRef.current = { mode: 'move', id: a.id, ox: p.x, oy: p.y, created: true };
      return;
    }

    if (tool === 'text') {
      const a: Annotation = {
        id: uid(),
        type: 'text',
        color,
        width,
        x: p.x,
        y: p.y,
        text: '',
        size: fontSize,
        font,
        outline,
      };
      // The undo step for this placement sits at the current history length.
      placedTextRef.current = { id: a.id, historyIndex: history.length };
      setAnnotations((prev) => [...prev, a]);
      setSelectedId(a.id);
      editingRef.current = a.id;
      setEditingId(a.id);
      // Keeps the browser from moving focus to the page, which would steal it
      // from the field that is about to appear.
      event.preventDefault();
      return;
    }

    const a: ShapeAnnotation = {
      id: uid(),
      type: tool as ShapeType,
      color,
      width,
      x1: p.x,
      y1: p.y,
      x2: p.x,
      y2: p.y,
    };
    setAnnotations((prev) => [...prev, a]);
    setSelectedId(a.id);
    dragRef.current = { mode: 'create', id: a.id, ox: p.x, oy: p.y };
  };

  const handleMove = (event: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const drag = dragRef.current;
    const p = toImage(event);

    if (!drag) {
      if (canvas) {
        if (hitHandle(p.x, p.y)) canvas.style.cursor = 'grab';
        else if (tool === 'select') canvas.style.cursor = 'default';
        else canvas.style.cursor = 'crosshair';
      }
      return;
    }

    if (drag.mode === 'crop') {
      setCropDraft({
        x: Math.min(drag.ox, p.x),
        y: Math.min(drag.oy, p.y),
        w: Math.abs(p.x - drag.ox),
        h: Math.abs(p.y - drag.oy),
      });
      return;
    }

    drag.moved = true;
    setAnnotations((prev) =>
      prev.map((a) => {
        if (a.id !== drag.id) return a;
        if (drag.mode === 'handle' && isShape(a) && drag.handle) {
          return moveHandle(a, drag.handle, p);
        }
        if (drag.mode === 'create' && isShape(a)) {
          return { ...a, x2: p.x, y2: p.y };
        }
        const dx = p.x - drag.ox;
        const dy = p.y - drag.oy;
        if (isShape(a)) {
          return { ...a, x1: a.x1 + dx, y1: a.y1 + dy, x2: a.x2 + dx, y2: a.y2 + dy };
        }
        return { ...a, x: a.x + dx, y: a.y + dy };
      }),
    );
    if (drag.mode === 'move') dragRef.current = { ...drag, ox: p.x, oy: p.y };
  };

  const handleUp = () => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (!drag) return;

    if (drag.mode === 'crop') {
      const draft = cropDraft;
      setCropDraft(null);
      const img = imageRef.current;
      if (!draft || !img || draft.w < 12 || draft.h < 12) return;
      push();
      setCrop({
        x: Math.max(0, Math.round(draft.x)),
        y: Math.max(0, Math.round(draft.y)),
        w: Math.min(img.naturalWidth - Math.max(0, draft.x), Math.round(draft.w)),
        h: Math.min(img.naturalHeight - Math.max(0, draft.y), Math.round(draft.h)),
      });
      setTool('select');
      return;
    }

    // Takes back the undo step of a gesture that turned out to change nothing,
    // together with the unsaved flag it raised.
    const dropLastStep = () =>
      setHistory((prev) => {
        const last = prev[prev.length - 1];
        if (last) setDirty(last.dirty);
        return prev.slice(0, -1);
      });

    // Selecting or grabbing something without moving it is not an edit.
    if ((drag.mode === 'move' || drag.mode === 'handle') && !drag.moved && !drag.created) {
      dropLastStep();
      return;
    }

    if (drag.mode !== 'create') return;
    const shape = annotations.find((a) => a.id === drag.id);
    const tooSmall =
      shape !== undefined &&
      isShape(shape) &&
      Math.abs(shape.x2 - shape.x1) <= 3 &&
      Math.abs(shape.y2 - shape.y1) <= 3;
    if (tooSmall) {
      setAnnotations((prev) => prev.filter((a) => a.id !== drag.id));
      setSelectedId(null);
      dropLastStep();
    }
  };

  /** Steps back through everything: shapes, edits, deletions and crops. */
  const undo = useCallback(() => {
    setHistory((prev) => {
      if (prev.length === 0) return prev;
      const last = prev[prev.length - 1];
      setAnnotations(last.annotations);
      setNextStep(last.nextStep);
      setCrop(last.crop);
      setSelectedId(null);
      setDirty(last.dirty);
      textEditRef.current = null;
      return prev.slice(0, -1);
    });
  }, []);

  const removeSelected = useCallback(() => {
    if (!selectedId) return;
    push();
    setAnnotations((prev) => prev.filter((a) => a.id !== selectedId));
    setSelectedId(null);
  }, [selectedId, push]);

  const updateSelected = (patch: (a: Annotation) => Annotation, coalesceKey?: string) => {
    if (!selectedId) return;
    push(coalesceKey);
    setAnnotations((prev) => prev.map((a) => (a.id === selectedId ? patch(a) : a)));
  };

  const applyColor = (value: string) => {
    setColor(value);
    updateSelected((a) => ({ ...a, color: value }));
  };

  const applyWidth = (value: number) => {
    setWidth(value);
    updateSelected((a) =>
      a.type === 'step' ? { ...a, width: value, radius: stepRadius(value) } : { ...a, width: value },
    );
  };

  const applyFontSize = (value: number) => {
    const size = Math.max(10, Math.min(200, value || 10));
    setFontSize(size);
    updateSelected((a) => (a.type === 'text' ? { ...a, size } : a));
  };

  const applyFont = (value: string) => {
    setFont(value);
    updateSelected((a) => (a.type === 'text' ? { ...a, font: value } : a));
  };

  const applyOutline = (value: boolean) => {
    setOutline(value);
    updateSelected((a) => (a.type === 'text' ? { ...a, outline: value } : a));
  };

  const applyStepNumber = (value: number) => {
    const n = Math.max(1, value || 1);
    if (selected && selected.type === 'step') {
      updateSelected((a) => (a.type === 'step' ? { ...a, n } : a));
      return;
    }
    setNextStep(n);
  };

  const chooseTool = (next: ToolId) => {
    // The highlighter is a marker pen, so it starts yellow, and leaving it
    // returns to the normal annotation colour.
    if (next === 'highlight') setColor(HIGHLIGHT_COLOR);
    else if (tool === 'highlight') setColor(DEFAULT_COLOR);
    setTool(next);
    if (next !== 'select') setCropDraft(null);
  };

  const resetCrop = () => {
    push();
    setCrop(null);
  };

  const save = useCallback(async () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    redraw(false);
    const blob = await canvasToBlob(canvas);
    onSave(blob, makeThumb(canvas));
    setDirty(false);
    redraw(true);
  }, [onSave, redraw]);

  const requestLeave = useCallback(
    (target: PendingNav) => {
      if (!target) return;
      if (dirty) {
        setPendingNav(target);
        return;
      }
      if (target.kind === 'close') onClose();
      else onNavigate(target.delta);
    },
    [dirty, onClose, onNavigate],
  );

  const resolvePending = useCallback(
    async (action: 'save' | 'discard') => {
      const target = pendingNav;
      setPendingNav(null);
      if (!target) return;
      if (action === 'save') await save();
      if (target.kind === 'close') onClose();
      else onNavigate(target.delta);
    },
    [pendingNav, save, onClose, onNavigate],
  );

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
        if (event.ctrlKey && event.key.toLowerCase() === 's') {
          event.preventDefault();
          void save();
        }
        return;
      }
      if (pendingNav) return;
      if (event.key === 'Escape') requestLeave({ kind: 'close' });
      else if (event.key === 'Delete' || event.key === 'Backspace') removeSelected();
      else if (event.ctrlKey && event.key.toLowerCase() === 'z') undo();
      else if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        void save();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [pendingNav, requestLeave, removeSelected, undo, save]);

  // Focus lands after the browser has finished handling the click, otherwise
  // the default mousedown behaviour would take it straight back.
  useEffect(() => {
    if (!editingId) return;
    const frame = window.requestAnimationFrame(() => {
      const field = inlineRef.current;
      if (!field) return;
      field.focus();
      field.setSelectionRange(field.value.length, field.value.length);
    });
    return () => window.cancelAnimationFrame(frame);
  }, [editingId]);

  // The overlay is positioned in screen pixels, so it has to follow the canvas
  // whenever the window is resized.
  useEffect(() => {
    const onResize = () => setLayoutTick((n) => n + 1);
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  function finishEditing() {
    const id = editingRef.current;
    if (!id) return;
    editingRef.current = null;
    setEditingId(null);
    textEditRef.current = null;
    const target = annotations.find((a) => a.id === id);
    const empty = !target || target.type !== 'text' || target.text.trim() === '';
    const placed = placedTextRef.current;
    placedTextRef.current = null;
    if (!empty) return;

    if (placed && placed.id === id) {
      // A box that never received any text leaves no trace, not even in undo.
      setHistory((prev) => {
        const before = prev[placed.historyIndex];
        if (before) {
          setAnnotations(before.annotations);
          setNextStep(before.nextStep);
          setCrop(before.crop);
          setDirty(before.dirty);
        } else {
          setAnnotations((list) => list.filter((a) => a.id !== id));
        }
        return prev.slice(0, placed.historyIndex);
      });
    } else {
      push();
      setAnnotations((list) => list.filter((a) => a.id !== id));
    }
    setSelectedId(null);
  }

  /** Double clicking a text label opens it for editing in place. */
  const handleDoubleClick = (event: React.MouseEvent<HTMLCanvasElement>) => {
    const p = toImage(event);
    const hit = hitTest(p.x, p.y);
    if (!hit || hit.type !== 'text') return;
    event.preventDefault();
    placedTextRef.current = null;
    setSelectedId(hit.id);
    editingRef.current = hit.id;
    setEditingId(hit.id);
  };

  /** Where the inline field sits and how big it is, in screen pixels. */
  const inlineLayout = (() => {
    const target = annotations.find((a) => a.id === editingId);
    const canvas = canvasRef.current;
    const img = imageRef.current;
    if (!target || target.type !== 'text' || !canvas || !img) return null;
    const box = canvas.getBoundingClientRect();
    if (box.width === 0) return null;
    const k = canvas.width / box.width;
    const area = crop ?? { x: 0, y: 0, w: img.naturalWidth, h: img.naturalHeight };
    const ctx = canvas.getContext('2d');
    let widest = 0;
    if (ctx) {
      ctx.save();
      ctx.font = `bold ${target.size}px ${target.font}`;
      widest = Math.max(...textLines(target).map((line) => ctx.measureText(line || ' ').width));
      ctx.restore();
    }
    const fontPx = target.size / k;
    const lines = textLines(target).length;
    return {
      target,
      left: (target.x - area.x) / k,
      // Canvas text hangs from the top of its em box, a CSS line box adds half
      // a leading above it, so the field is nudged up to line up exactly.
      top: (target.y - area.y) / k - fontPx * 0.125,
      width: Math.max(fontPx * 2, widest / k + fontPx),
      height: lines * fontPx * 1.25,
      fontPx,
    };
  })();

  const stepFieldValue = selected && selected.type === 'step' ? selected.n : nextStep;
  const textActive = tool === 'text' || (selected !== null && selected.type === 'text');
  const stepActive = tool === 'step' || (selected !== null && selected.type === 'step');
  // Redaction and highlighting are filled blocks, so a stroke width means
  // nothing for either of them.
  const solidTool = (id: ToolId | undefined) => id === 'redact' || id === 'highlight';
  const widthActive = !solidTool(tool) && !solidTool(selected?.type as ToolId | undefined);

  return (
    <div className="modal">
      <div className="modal-bar">
        <button onClick={() => requestLeave({ kind: 'nav', delta: -1 })} disabled={index <= 0}>
          Previous
        </button>
        <span className="mono">
          {index + 1} / {total}
        </span>
        <button
          onClick={() => requestLeave({ kind: 'nav', delta: 1 })}
          disabled={index >= total - 1}
        >
          Next
        </button>
        <span className="spacer" />
        <button className="primary" onClick={() => void save()} disabled={!dirty}>
          Save changes
        </button>
        <button onClick={() => requestLeave({ kind: 'close' })}>Close</button>
      </div>

      <div className="editor-tools">
        {TOOLS.map((t) => (
          <button
            key={t.id}
            className={tool === t.id ? 'tool active' : 'tool'}
            onClick={() => chooseTool(t.id)}
          >
            {t.label}
          </button>
        ))}
        {crop ? (
          <button onClick={resetCrop} title="Restore the full screenshot">
            Reset crop
          </button>
        ) : null}
        <span className="sep" />
        {COLORS.map((c) => (
          <button
            key={c}
            className={color === c ? 'swatch active' : 'swatch'}
            style={{ background: c }}
            onClick={() => applyColor(c)}
            title={c}
          />
        ))}
        <span className="spacer" />
        <button onClick={undo} disabled={history.length === 0}>
          Undo
        </button>
        <button onClick={removeSelected} disabled={!selectedId}>
          Delete selected
        </button>
        {dirty ? <span className="dirty">Unsaved changes</span> : null}
      </div>

      <div className="editor-tools">
        {widthActive ? (
          <label className="inline">
            Thickness
            <input
              type="range"
              min={2}
              max={14}
              value={width}
              onChange={(e) => applyWidth(Number(e.target.value))}
            />
          </label>
        ) : (
          <span className="inline">Solid fill, no thickness</span>
        )}
        {stepActive ? (
          <label className="inline">
            {selected && selected.type === 'step' ? 'Step number' : 'Next step number'}
            <input
              type="number"
              min={1}
              value={stepFieldValue}
              onChange={(e) => applyStepNumber(Number(e.target.value))}
            />
          </label>
        ) : null}
        {textActive ? (
          <>
            <span className="sep" />
            <label className="inline">
              Font
              <select value={selected && selected.type === 'text' ? selected.font : font} onChange={(e) => applyFont(e.target.value)}>
                {FONTS.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="inline">
              Size
              <input
                type="number"
                min={10}
                max={200}
                step={2}
                value={selected && selected.type === 'text' ? selected.size : fontSize}
                onChange={(e) => applyFontSize(Number(e.target.value))}
              />
            </label>
            <label className="check">
              <input
                type="checkbox"
                checked={selected && selected.type === 'text' ? selected.outline : outline}
                onChange={(e) => applyOutline(e.target.checked)}
              />
              Outline
            </label>
          </>
        ) : null}
      </div>

      <div className="modal-body">
        <div className="canvas-wrap">
          <canvas
            ref={canvasRef}
            className="editor-canvas"
            onMouseDown={handleDown}
            onMouseMove={handleMove}
            onMouseUp={handleUp}
            onMouseLeave={handleUp}
            onDoubleClick={handleDoubleClick}
          />
          {inlineLayout ? (
            <textarea
              ref={inlineRef}
              // Focus on mount is the dependable path, the effect above only
              // covers the case where something takes focus away right after.
              autoFocus
              onFocus={(e) => {
                const length = e.currentTarget.value.length;
                e.currentTarget.setSelectionRange(length, length);
              }}
              className={inlineLayout.target.outline ? 'inline-text outlined' : 'inline-text'}
              wrap="off"
              spellCheck={false}
              value={inlineLayout.target.text}
              placeholder="Type"
              style={{
                left: inlineLayout.left,
                top: inlineLayout.top,
                width: inlineLayout.width,
                height: inlineLayout.height,
                fontSize: inlineLayout.fontPx,
                fontFamily: inlineLayout.target.font,
                color: inlineLayout.target.color,
                caretColor: inlineLayout.target.color,
              }}
              onChange={(e) => {
                const value = e.target.value;
                const id = inlineLayout.target.id;
                push(`text:${id}`);
                setAnnotations((prev) =>
                  prev.map((a) => (a.id === id && a.type === 'text' ? { ...a, text: value } : a)),
                );
              }}
              onKeyDown={(e) => {
                // Enter is a new line. Escape, or clicking elsewhere, finishes.
                if (e.key === 'Escape') {
                  e.preventDefault();
                  e.stopPropagation();
                  finishEditing();
                }
              }}
              onBlur={() => finishEditing()}
            />
          ) : null}
        </div>
      </div>

      {pendingNav ? (
        <div className="confirm-backdrop">
          <div className="confirm">
            <h3>Unsaved changes</h3>
            <p>
              This screenshot has changes that have not been saved. Leaving now discards them.
            </p>
            <div className="confirm-actions">
              <button className="primary" onClick={() => void resolvePending('save')}>
                Save and continue
              </button>
              <button className="danger" onClick={() => void resolvePending('discard')}>
                Discard changes
              </button>
              <button onClick={() => setPendingNav(null)}>Cancel</button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
