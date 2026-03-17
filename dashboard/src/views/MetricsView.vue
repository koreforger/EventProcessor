<template>
  <div class="metrics-view">
    <div class="summary-cards" v-if="latest">
      <div class="card" v-for="(value, name) in latest.counters" :key="name">
        <div class="card-label">{{ formatLabel(name) }}</div>
        <div class="card-value">{{ value.toLocaleString() }}</div>
        <div class="card-rate" v-if="latest.rates[name + '.rate'] !== undefined">
          {{ latest.rates[name + '.rate'].toFixed(1) }}/s
        </div>
      </div>
    </div>

    <div class="charts-grid">
      <MetricChart
        title="Messages Consumed"
        metric-key="messages.consumed"
        :snapshots="snapshots"
        color="#89b4fa"
        mode="rate"
      />
      <MetricChart
        title="Batches Processed"
        metric-key="batches.processed"
        :snapshots="snapshots"
        color="#a6e3a1"
        mode="rate"
      />
      <MetricChart
        title="JEX Extractions"
        metric-key="jex.extractions"
        :snapshots="snapshots"
        color="#f9e2af"
        mode="rate"
      />
      <MetricChart
        title="Errors"
        metric-key="errors.total"
        :snapshots="snapshots"
        color="#f38ba8"
        mode="rate"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMetrics } from '../composables/useMetrics'
import MetricChart from '../components/MetricChart.vue'

const { snapshots, latest } = useMetrics()

function formatLabel(key: string): string {
  return key.replace(/\./g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}
</script>

<style scoped>
.metrics-view {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.summary-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 12px;
}

.card {
  background: var(--bg-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
}

.card-label {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-secondary);
  margin-bottom: 4px;
}

.card-value {
  font-size: 24px;
  font-weight: 700;
  color: var(--text-primary);
}

.card-rate {
  font-size: 12px;
  color: var(--accent);
  margin-top: 2px;
}

.charts-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(480px, 1fr));
  gap: 16px;
}
</style>
