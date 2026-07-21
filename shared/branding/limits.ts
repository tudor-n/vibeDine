// Branding asset upload size caps. HAND-SYNCED with the C# side —
// backend/RestaurantApi/Controllers/BrandingController.cs (MaxLogoBytes /
// MaxPhotoBytes). Change both together, or the client guard and the server
// limit drift apart.
export const BRANDING_LOGO_MAX_BYTES = 5 * 1024 * 1024 // 5 MB
export const BRANDING_PHOTO_MAX_BYTES = 10 * 1024 * 1024 // 10 MB
export const BRANDING_MENU_BG_MAX_BYTES = 10 * 1024 * 1024 // 10 MB
