export interface BrandPreset {
  key: string
  name: string
  primary: string
  secondary: string
  background: string
}

export const BRAND_PRESETS: BrandPreset[] = [
  { key: 'supporter', name: 'Supporter', primary: '#E51F4E', secondary: '#E8A33B', background: '#F5EEE3' },
  { key: 'classic',   name: 'vibeDine',  primary: '#FF4A1C', secondary: '#D9F54A', background: '#F4EEE2' },
  { key: 'oliva',     name: 'Oliva',     primary: '#5E7A4C', secondary: '#C96A3E', background: '#F3F0E6' },
  { key: 'azur',      name: 'Azur',      primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' },
]

/**
 * Local editor shape shared by the Settings Brand tab (Task 13) and the
 * onboarding brand step (Task 14). Mirrors `RestaurantBranding` (shared
 * types) but with non-nullable colour fields — the editor always has a
 * concrete draft colour (seeded from a preset) to bind `<input type="color">`
 * against, even before anything has been saved.
 */
export interface BrandDraft {
  enabled: boolean
  primary: string
  secondary: string
  background: string
  presetKey: string | null
  logoUrl: string | null
  photoUrl: string | null
  logoBackground: string | null
  logoIsDark: boolean | null
}
