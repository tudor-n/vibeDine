// Validation constants for menu Categories and Products.
// Kept in sync with backend/RestaurantApi/Controllers/MenuManagementController.cs.
export const MENU_VALIDATION = {
  NAME_MIN: 1,
  NAME_MAX: 120,
  DESCRIPTION_MAX: 1000,
  IMAGE_URL_MAX: 500,
  PRICE_MIN_EXCLUSIVE: 0,
  PRICE_MAX: 99999999.99,
  IMPORT_MAX_FILES: 10,
  IMPORT_MAX_FILE_BYTES: 5 * 1024 * 1024,
  IMPORT_ALLOWED_MIME: ['image/jpeg', 'image/png', 'image/webp', 'image/heic'] as const,
} as const

export type AllowedImportMime = (typeof MENU_VALIDATION.IMPORT_ALLOWED_MIME)[number]
