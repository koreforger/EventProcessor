import { ref, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'

const connections = new Map<string, HubConnection>()
const connectionStates = ref<Record<string, string>>({})

export function useSignalR(hubUrl: string) {
  const state = ref<string>('Disconnected')

  let connection = connections.get(hubUrl)

  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.onreconnecting(() => {
      state.value = 'Reconnecting'
      connectionStates.value[hubUrl] = 'Reconnecting'
    })

    connection.onreconnected(() => {
      state.value = 'Connected'
      connectionStates.value[hubUrl] = 'Connected'
    })

    connection.onclose(() => {
      state.value = 'Disconnected'
      connectionStates.value[hubUrl] = 'Disconnected'
    })

    connections.set(hubUrl, connection)
  }

  async function start() {
    if (connection!.state === HubConnectionState.Disconnected) {
      try {
        await connection!.start()
        state.value = 'Connected'
        connectionStates.value[hubUrl] = 'Connected'
      } catch {
        state.value = 'Error'
        connectionStates.value[hubUrl] = 'Error'
        // Retry after 5s
        setTimeout(start, 5000)
      }
    }
  }

  function on<T>(event: string, callback: (data: T) => void) {
    connection!.on(event, callback)
  }

  onUnmounted(() => {
    // Don't disconnect — shared connection persists across route changes
  })

  return { connection: connection!, state, start, on }
}

export function useConnectionStates() {
  return connectionStates
}
