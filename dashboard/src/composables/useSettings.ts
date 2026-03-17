import { ref, onMounted } from 'vue'
import { useSignalR } from './useSignalR'
import type { SettingEntry, SettingsChangedEvent } from '../types'

export function useSettings() {
  const entries = ref<SettingEntry[]>([])
  const loading = ref(true)
  const lastUpdate = ref<Date | null>(null)

  const { start, on } = useSignalR('/hub/settings')

  async function loadInitial() {
    try {
      const res = await fetch('/api/settings')
      entries.value = await res.json()
    } catch {
      // Will retry via SignalR
    } finally {
      loading.value = false
    }
  }

  onMounted(async () => {
    await loadInitial()

    on<SettingsChangedEvent>('SettingsChanged', (event) => {
      lastUpdate.value = new Date(event.timestamp)

      for (const change of event.changes) {
        const idx = entries.value.findIndex((e) => e.key === change.key)
        if (idx >= 0) {
          entries.value[idx] = change
        } else {
          entries.value.push(change)
        }
      }

      // Sort by key after update
      entries.value.sort((a, b) => a.key.localeCompare(b.key))
    })

    await start()
  })

  async function refresh() {
    loading.value = true
    await loadInitial()
  }

  return { entries, loading, lastUpdate, refresh }
}
