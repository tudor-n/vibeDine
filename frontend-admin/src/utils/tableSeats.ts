// Floor-plan table geometry. A table has a capacity (how many people) and a shape;
// the icon reflects both — the tabletop is drawn in the chosen shape and exactly
// `capacity` chairs are placed around it. Round tables spread chairs evenly on a
// circle; square tables put them on four sides; rectangle/oval tables are wider
// and bias chairs onto the two long sides with one at each end. Positions are
// percentage-based (with a fixed-px gap via calc) so the same slots render at any
// tabletop size — the small canvas chip and the larger dialog previews alike.

export const MIN_CAPACITY = 1
export const MAX_CAPACITY = 12
export const DEFAULT_CAPACITY = 4

export type TableShape = 'round' | 'square' | 'rectangle' | 'oval'
export const TABLE_SHAPES: TableShape[] = ['round', 'square', 'rectangle', 'oval']
// Square keeps existing tables looking as they did before shapes existed.
export const DEFAULT_SHAPE: TableShape = 'square'

export function clampCapacity(value: number): number {
  const n = Math.round(value || 0)
  return Math.max(MIN_CAPACITY, Math.min(MAX_CAPACITY, n))
}

export function normalizeShape(value: string | null | undefined): TableShape {
  return (TABLE_SHAPES as string[]).includes(value ?? '') ? (value as TableShape) : DEFAULT_SHAPE
}

export interface SeatSlot {
  key: string
  style: Record<string, string>
}

// Tabletop pixel dimensions + border-radius for a base size and shape. Rectangle
// and oval are wider than tall; oval is a stadium (fully rounded ends).
export interface TabletopDims {
  w: number
  h: number
  radius: string
}
export function tabletopDims(baseSize: number, shape: TableShape): TabletopDims {
  if (shape === 'round') return { w: baseSize, h: baseSize, radius: '50%' }
  if (shape === 'square') return { w: baseSize, h: baseSize, radius: '26%' }
  const w = Math.round(baseSize * 1.35)
  const h = Math.round(baseSize * 0.74)
  if (shape === 'rectangle') return { w, h, radius: '14%' }
  return { w, h, radius: '999px' } // oval → stadium
}

// Chair thickness (seat back) and gap between chair and tabletop, for edge layouts.
const THICK = '6px'
const GAP = '5px'

export function seatSlots(capacity: number, shape: TableShape = DEFAULT_SHAPE): SeatSlot[] {
  const n = clampCapacity(capacity)
  return shape === 'round' ? roundSlots(n) : edgeSlots(n, shape)
}

type Side = 'top' | 'bottom' | 'left' | 'right'

function edgeSlots(n: number, shape: TableShape): SeatSlot[] {
  const wide = shape === 'rectangle' || shape === 'oval'
  const counts: Record<Side, number> = { top: 0, bottom: 0, left: 0, right: 0 }

  if (wide) {
    // Long sides carry the crowd; short ends get one chair each once there's room.
    if (n <= 2) {
      counts.top = Math.ceil(n / 2)
      counts.bottom = Math.floor(n / 2)
    } else {
      counts.left = 1
      counts.right = 1
      const rest = n - 2
      counts.top = Math.ceil(rest / 2)
      counts.bottom = Math.floor(rest / 2)
    }
  } else {
    // Square: spread as evenly as possible, extras favouring top→bottom→left→right.
    const base = Math.floor(n / 4)
    const order: Side[] = ['top', 'bottom', 'left', 'right']
    order.forEach((s) => (counts[s] = base))
    for (let i = 0; i < n % 4; i++) counts[order[i]]++
  }

  const slots: SeatSlot[] = []
  for (const side of ['top', 'bottom', 'left', 'right'] as Side[]) {
    const c = counts[side]
    if (c === 0) continue
    const lenPct = Math.min(42, 62 / (c + 1))
    const len = `${lenPct}%`
    for (let i = 0; i < c; i++) {
      const pos = `${((i + 1) / (c + 1)) * 100}%`
      let style: Record<string, string>
      if (side === 'top') {
        style = { left: pos, top: '0%', width: len, height: THICK, transform: `translate(-50%, calc(-50% - ${GAP}))` }
      } else if (side === 'bottom') {
        style = { left: pos, top: '100%', width: len, height: THICK, transform: `translate(-50%, calc(-50% + ${GAP}))` }
      } else if (side === 'left') {
        style = { top: pos, left: '0%', width: THICK, height: len, transform: `translate(calc(-50% - ${GAP}), -50%)` }
      } else {
        style = { top: pos, left: '100%', width: THICK, height: len, transform: `translate(calc(-50% + ${GAP}), -50%)` }
      }
      slots.push({ key: `${side}-${i}`, style })
    }
  }
  return slots
}

// Evenly space chairs on the circle, each rotated to face the centre.
function roundSlots(n: number): SeatSlot[] {
  const OUT = 9 // px each chair centre sits beyond the tabletop edge
  const CW = 15 // chair width (px)
  const CH = 10 // chair depth (px)
  const slots: SeatSlot[] = []
  for (let i = 0; i < n; i++) {
    const a = -Math.PI / 2 + (i * 2 * Math.PI) / n
    const cos = Math.cos(a)
    const sin = Math.sin(a)
    const deg = (a * 180) / Math.PI + 90
    slots.push({
      key: `r-${i}`,
      style: {
        left: axis(cos),
        top: axis(sin),
        width: `${CW}px`,
        height: `${CH}px`,
        borderRadius: '3px',
        transform: `translate(-50%, -50%) rotate(${deg.toFixed(1)}deg)`,
      },
    })
  }
  return slots
}

// A circle-perimeter coordinate: centre (50%) plus half-width along the axis
// (50% * cos/sin) plus a fixed-px push outward. Percentage and px share the sign
// of cos/sin, so the calc() stays valid for every quadrant.
function axis(c: number): string {
  const sign = c >= 0 ? '+' : '-'
  return `calc(50% ${sign} ${Math.abs(50 * c).toFixed(2)}% ${sign} ${Math.abs(9 * c).toFixed(2)}px)`
}
