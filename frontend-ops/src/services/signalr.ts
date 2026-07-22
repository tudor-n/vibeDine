import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { useAuthStore } from '@/stores/useAuthStore'
import type { LogEntry } from '@/types/ops'
import { BASE_URL } from './api-client'

/**
 * Live log tail over SignalR. The server keeps a single upstream poller per deployment and fans
 * lines out to subscribers, so this just joins a group and renders what arrives.
 */
export function createLogStream(onBatch: (entries: LogEntry[]) => void): {
  start: (connectionId: string, deploymentId: string, logType: string) => Promise<void>
  stop: () => Promise<void>
} {
  let connection: HubConnection | null = null
  let current: { connectionId: string; deploymentId: string; logType: string } | null = null

  async function start(connectionId: string, deploymentId: string, logType: string) {
    await stop()
    connection = new HubConnectionBuilder()
      .withUrl(`${BASE_URL}/hubs/logs`, {
        accessTokenFactory: () => useAuthStore().accessToken ?? '',
        withCredentials: false, // Bearer token only; no cookies (matches server CORS)
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('LogBatch', (entries: LogEntry[]) => onBatch(entries))
    await connection.start()
    await connection.invoke('Subscribe', connectionId, deploymentId, logType)
    current = { connectionId, deploymentId, logType }
  }

  async function stop() {
    if (connection) {
      try {
        if (current) {
          await connection.invoke('Unsubscribe', current.connectionId, current.deploymentId, current.logType)
        }
        await connection.stop()
      } catch {
        /* ignore teardown errors */
      }
      connection = null
      current = null
    }
  }

  return { start, stop }
}
