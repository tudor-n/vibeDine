function apiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_URL ?? ''
  return raw.endsWith('/') ? raw.slice(0, -1) : raw
}

export function resolveMediaUrl(url: string | null | undefined): string {
  if (!url) return ''
  if (url.startsWith('/') && !url.startsWith('//')) return `${apiBaseUrl()}${url}`
  return url
}
