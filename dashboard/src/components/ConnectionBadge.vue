<template>
  <div class="connection-badge">
    <span class="status-dot" :class="statusClass"></span>
    <span class="status-text">{{ statusLabel }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useConnectionStates } from '../composables/useSignalR'

const states = useConnectionStates()

const overallState = computed(() => {
  const values = Object.values(states.value)
  if (values.length === 0) return 'Disconnected'
  if (values.every((s) => s === 'Connected')) return 'Connected'
  if (values.some((s) => s === 'Reconnecting')) return 'Reconnecting'
  if (values.some((s) => s === 'Error')) return 'Error'
  return 'Disconnected'
})

const statusClass = computed(() => {
  switch (overallState.value) {
    case 'Connected': return 'dot--connected'
    case 'Reconnecting': return 'dot--reconnecting'
    case 'Error': return 'dot--error'
    default: return 'dot--disconnected'
  }
})

const statusLabel = computed(() => overallState.value)
</script>

<style scoped>
.connection-badge {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--text-secondary);
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.dot--connected { background: #a6e3a1; }
.dot--reconnecting { background: #f9e2af; animation: pulse 1s infinite; }
.dot--error { background: #f38ba8; }
.dot--disconnected { background: #585b70; }

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
