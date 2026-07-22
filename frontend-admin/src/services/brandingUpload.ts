import {
  BRANDING_LOGO_MAX_BYTES,
  BRANDING_PHOTO_MAX_BYTES,
  BRANDING_MENU_BG_MAX_BYTES,
} from '@shared/branding/limits'

export type BrandingAssetKind = 'logo' | 'photo' | 'menu-background'

const LIMIT: Record<BrandingAssetKind, number> = {
  logo: BRANDING_LOGO_MAX_BYTES,
  photo: BRANDING_PHOTO_MAX_BYTES,
  'menu-background': BRANDING_MENU_BG_MAX_BYTES,
}

const LABEL: Record<BrandingAssetKind, string> = {
  logo: 'Logo-ul',
  photo: 'Fotografia',
  'menu-background': 'Fundalul de meniu',
}

/**
 * Guard an upload before it hits the network. Oversize files rejected by the
 * server's RequestSizeLimit surface cross-origin as an opaque "Failed to fetch"
 * (the connection is reset / the 413 carries no CORS headers), so we catch it
 * here and throw a legible Romanian message instead.
 */
export function assertBrandingAssetSize(kind: BrandingAssetKind, file: { size: number }): void {
  const max = LIMIT[kind]
  if (file.size > max) {
    throw new Error(`${LABEL[kind]} depășește ${Math.round(max / (1024 * 1024))} MB.`)
  }
}
