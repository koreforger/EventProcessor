<template>
  <div class="metric-chart">
    <div class="chart-header">
      <span class="chart-title">{{ title }}</span>
      <span class="chart-latest" :style="{ color }">
        {{ latestValue }}
      </span>
    </div>
    <DxChart
      :data-source="chartData"
      :height="220"
    >
      <DxCommonSeriesSettings type="area" argument-field="time" />
      <DxSeries
        value-field="value"
        :name="title"
        :color="color"
      >
        <DxPoint :visible="false" />
      </DxSeries>
      <DxArgumentAxis>
        <DxLabel :visible="true" format="shortTime" />
      </DxArgumentAxis>
      <DxValueAxis>
        <DxLabel :visible="true" />
      </DxValueAxis>
      <DxLegend :visible="false" />
      <DxTooltip :enabled="true" :shared="true" />
      <DxAnimation :enabled="false" />
    </DxChart>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import {
  DxChart,
  DxSeries,
  DxCommonSeriesSettings,
  DxArgumentAxis,
  DxValueAxis,
  DxLabel,
  DxLegend,
  DxTooltip,
  DxAnimation,
  DxPoint,
} from 'devextreme-vue/chart'
import type { MetricsSnapshot } from '../types'

const props = defineProps<{
  title: string
  metricKey: string
  snapshots: MetricsSnapshot[]
  color: string
  mode: 'counter' | 'rate'
}>()

const chartData = computed(() => {
  return props.snapshots.map((s) => ({
    time: new Date(s.timestamp),
    value: props.mode === 'rate'
      ? (s.rates[props.metricKey + '.rate'] ?? 0)
      : (s.counters[props.metricKey] ?? 0),
  }))
})

const latestValue = computed(() => {
  if (props.snapshots.length === 0) return '—'
  const last = props.snapshots[props.snapshots.length - 1]
  if (props.mode === 'rate') {
    return (last.rates[props.metricKey + '.rate'] ?? 0).toFixed(1) + '/s'
  }
  return (last.counters[props.metricKey] ?? 0).toLocaleString()
})
</script>

<style scoped>
.metric-chart {
  background: var(--bg-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
}

.chart-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 8px;
}

.chart-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.chart-latest {
  font-size: 18px;
  font-weight: 700;
}
</style>
