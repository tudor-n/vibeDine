// Mirrors the PlatformApi DTOs (camelCase JSON).

export interface PlatformAdmin {
  id: string
  email: string
  fullName: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  admin: PlatformAdmin
}

export interface Metrics {
  organizations: number
  locations: number
  activeLocations: number
  inactiveLocations: number
  admins: number
  hires: number
  pendingHires: number
}

export interface UserSummary {
  id: string
  fullName: string
  email: string | null
}

export interface OrganizationListItem {
  id: string
  name: string
  slug: string
  createdAt: string
  locationCount: number
  adminCount: number
  hireCount: number
  owner: UserSummary | null
}

export interface Member {
  membershipId: string
  userId: string
  fullName: string
  email: string | null
  role: string
  status: string
  restaurantId: string | null
  createdAt: string
}

export interface LocationListItem {
  id: string
  organizationId: string
  organizationName: string
  name: string
  slug: string
  address: string | null
  isActive: boolean
  timezone: string
  currency: string
  locale: string
  createdAt: string
  adminCount: number
  hireCount: number
}

export interface LocationDetail {
  id: string
  organizationId: string
  organizationName: string
  name: string
  slug: string
  address: string | null
  isActive: boolean
  timezone: string
  currency: string
  locale: string
  createdAt: string
  admins: Member[]
  hires: Member[]
}

export interface OrganizationDetail {
  id: string
  name: string
  slug: string
  createdAt: string
  owners: UserSummary[]
  locations: LocationListItem[]
}

export interface RegistrationLink {
  id: string
  token: string
  type: string
  organizationId: string | null
  organizationName: string | null
  label: string | null
  intendedEmail: string | null
  status: string
  url: string
  expiresAt: string
  createdAt: string
  usedAt: string | null
}
