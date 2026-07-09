// Estimate whether an uploaded logo is predominantly dark, to pick a legible
// Auto plate. Pure luminance math is split out so it is unit-testable without a DOM.

function channel(v: number): number {
  const s = v / 255
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
}

/** Alpha-weighted mean relative luminance (0..1), or null if nothing opaque. */
export function averageLuminance(rgba: Uint8ClampedArray): number | null {
  let sum = 0
  let weight = 0
  for (let i = 0; i < rgba.length; i += 4) {
    const a = rgba[i + 3] / 255
    if (a === 0) continue
    const lum = 0.2126 * channel(rgba[i]) + 0.7152 * channel(rgba[i + 1]) + 0.0722 * channel(rgba[i + 2])
    sum += lum * a
    weight += a
  }
  return weight === 0 ? null : sum / weight
}

/** Draw the logo to an offscreen canvas and decide if it is dark. null on failure. */
export function computeLogoIsDark(file: File): Promise<boolean | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      try {
        const w = Math.min(img.naturalWidth || 64, 64)
        const h = Math.min(img.naturalHeight || 64, 64)
        const canvas = document.createElement('canvas')
        canvas.width = w
        canvas.height = h
        const ctx = canvas.getContext('2d')
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, w, h)
        const avg = averageLuminance(ctx.getImageData(0, 0, w, h).data)
        resolve(avg === null ? null : avg < 0.5)
      } catch {
        resolve(null)
      } finally {
        URL.revokeObjectURL(url)
      }
    }
    img.onerror = () => { URL.revokeObjectURL(url); resolve(null) }
    img.src = url
  })
}
