import { ref, onMounted, computed } from 'vue'
import { useSignalR } from './useSignalR'
import type { MetricsSnapshot } from '../types'

const BUFFER_SIZE = 300 // 5 minutes at 1 snapshot/sec

export function useMetrics() {
  const snapshots = ref<MetricsSnapshot[]>([])
  const latest = computed(() => snapshots.value[snapshots.value.length - 1] ?? null)
  const loading = ref(true)

  const { start, on } = useSignalR('/hub/metrics')

  async function loadInitial() {
    try {
      const res = await fetch('/api/metrics')
      const snapshot: MetricsSnapshot = await res.json()
      snapshots.value.push(snapshot)
    } catch {
      // Will populate via SignalR
    } finally {
      loading.value = false
    }
  }

  onMounted(async () => {
    await loadInitial()

    on<MetricsSnapshot>('MetricsSnapshot', (snapshot) => {
      snapshots.value.push(snapshot)
      // Ring buffer — drop oldest when over size
      if (snapshots.value.length > BUFFER_SIZE) {
        snapshots.value = snapshots.value.slice(-BUFFER_SIZE)
      }
    })

    await start()
  })

  return { snapshots, latest, loading }
}
