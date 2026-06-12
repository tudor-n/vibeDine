import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '@/stores/useAuthStore'

export interface StaffHubClient {
  start(): Promise<void>
  stop(): Promise<void>
  joinAdminGroup(restaurantId: string): Promise<void>
  leaveAdminGroup(restaurantId: string): Promise<void>
  joinRoleGroup(restaurantId: string, role: string): Promise<void>
  leaveRoleGroup(restaurantId: string, role: string): Promise<void>
  on<T>(event: string, handler: (payload: T) => void): void
  off(event: string): void
  onReconnected(handler: (connectionId?: string) => void | Promise<void>): void
}

export function createStaffHubClient(): StaffHubClient {
  const auth = useAuthStore()
  const baseUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')
  const hubUrl = `${baseUrl}/hubs/staff`

  const conn = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => auth.accessToken ?? '',
    })
    .withAutomaticReconnect()
    .build()

  return {
    start: () => conn.start(),
    stop: () => conn.stop(),
    joinAdminGroup: (rid) => conn.invoke('JoinRoleGroup', rid, 'admin'),
    leaveAdminGroup: (rid) => conn.invoke('LeaveRoleGroup', rid, 'admin'),
    joinRoleGroup: (rid, role) => conn.invoke('JoinRoleGroup', rid, role),
    leaveRoleGroup: (rid, role) => conn.invoke('LeaveRoleGroup', rid, role),
    on: (event, handler) => conn.on(event, handler as (...args: unknown[]) => void),
    off: (event) => conn.off(event),
    onReconnected: (handler) => conn.onreconnected((connectionId) => {
      void handler(connectionId ?? undefined)
    }),
  }
}
