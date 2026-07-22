function apiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_URL || 'http://localhost:5005'
  return raw.endsWith('/') ? raw.slice(0, -1) : raw
}

export function resolveMediaUrl(url: string | null | undefined): string {
  if (!url) return ''
  if (url.startsWith('/') && !url.startsWith('//')) return `${apiBaseUrl()}${url}`
  return url
}

export function resolveResponseMediaUrls<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => resolveResponseMediaUrls(item)) as T
  }

  if (value && typeof value === 'object') {
    const result: Record<string, unknown> = {}
    for (const [key, nested] of Object.entries(value)) {
      if ((key === 'imageUrl' || key === 'avatarUrl' || key === 'logoUrl' || key === 'photoUrl' || key === 'menuBackgroundUrl') && typeof nested === 'string') {
        result[key] = resolveMediaUrl(nested)
      } else {
        result[key] = resolveResponseMediaUrls(nested)
      }
    }
    return result as T
  }

  return value
}
