import { ref, onMounted, computed } from 'vue'
import { useSignalR } from './useSignalR'
import type { MetricsSnapshot } from '../types'

const BUFFER_SIZE = 300 // 5 minutes at 1 snapshot/sec

// Module-level shared state — all callers share the same buffer.
const snapshots = ref<MetricsSnapshot[]>([])
const latest = computed(() => snapshots.value[snapshots.value.length - 1] ?? null)
let initialLoaded = false

export function useMetrics() {
  const loading = ref(!initialLoaded)
  const { start, on } = useSignalR('/hub/metrics')

  async function loadInitial() {
    if (initialLoaded) return
    try {
      const res = await fetch('/api/metrics')
      const snapshot: MetricsSnapshot = await res.json()
      snapshots.value.push(snapshot)
      initialLoaded = true
    } catch {
      // Will populate via SignalR
    } finally {
      loading.value = false
    }
  }

  onMounted(async () => {
    await loadInitial()

    on<MetricsSnapshot>('MetricsSnapshot', (snapshot) => {
      // Mutate the array in-place: push new, shift old when over limit.
      // Keeping the same array reference prevents full chart redraws.
      snapshots.value.push(snapshot)
      if (snapshots.value.length > BUFFER_SIZE) {
        snapshots.value.splice(0, snapshots.value.length - BUFFER_SIZE)
      }
    })

    await start()
  })

  return { snapshots, latest, loading }
}

