// Menu-background visibility styles. The manager picks how prominently the
// uploaded menu-background photo shows behind the ordering screens; the value
// maps to the opacity of the paper-tinted scrim painted over the photo (higher
// scrim = more faded / more readable).
//
// HAND-SYNCED with the C# validator in
// backend/RestaurantApi/Controllers/BrandingController.cs (MenuBackgroundStyles).
// Change both together.

export type MenuBackgroundStyle = 'visible' | 'medium' | 'subtle'

export const MENU_BACKGROUND_STYLES: readonly MenuBackgroundStyle[] = ['visible', 'medium', 'subtle']

// Used when the restaurant hasn't picked one yet (null/unknown).
export const DEFAULT_MENU_BACKGROUND_STYLE: MenuBackgroundStyle = 'medium'

const SCRIM_OPACITY: Record<MenuBackgroundStyle, number> = {
  visible: 0.15,
  medium: 0.45,
  subtle: 0.72,
}

/** Paper-scrim opacity to paint over the menu-background photo for a given style. */
export function menuBackgroundScrimOpacity(style: string | null | undefined): number {
  return SCRIM_OPACITY[(style as MenuBackgroundStyle)] ?? SCRIM_OPACITY[DEFAULT_MENU_BACKGROUND_STYLE]
}

export function isMenuBackgroundStyle(v: string | null | undefined): v is MenuBackgroundStyle {
  return v != null && MENU_BACKGROUND_STYLES.includes(v as MenuBackgroundStyle)
}
