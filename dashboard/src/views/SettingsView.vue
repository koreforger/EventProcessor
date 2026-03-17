<template>
  <div class="settings-view">
    <div class="toolbar">
      <span class="last-update" v-if="lastUpdate">
        Last DB change: {{ lastUpdate.toLocaleTimeString() }}
      </span>
      <button class="btn-refresh" @click="refresh" :disabled="loading">
        ↻ Refresh
      </button>
    </div>

    <DxDataGrid
      :data-source="entries"
      :show-borders="true"
      :row-alternation-enabled="true"
      :allow-column-reordering="true"
      :allow-column-resizing="true"
      column-resizing-mode="widget"
      :word-wrap-enabled="true"
      key-expr="key"
      :height="gridHeight"
      @row-prepared="onRowPrepared"
    >
      <DxSearchPanel :visible="true" :width="300" placeholder="Filter settings..." />
      <DxGroupPanel :visible="true" />
      <DxColumnChooser :enabled="true" />
      <DxPaging :page-size="50" />
      <DxPager :show-page-size-selector="true" :allowed-page-sizes="[25, 50, 100]" />

      <DxColumn
        name="keyGroup"
        caption="Group"
        :group-index="0"
        :calculate-cell-value="groupByPrefix"
        :visible="false"
      />
      <DxColumn data-field="key" caption="Setting" :min-width="250" />
      <DxColumn data-field="databaseValue" caption="Database Value" :min-width="200" />
      <DxColumn data-field="activeValue" caption="Active Value" :min-width="200" />
      <DxColumn
        data-field="status"
        caption="Status"
        :width="100"
        alignment="center"
        cell-template="statusCell"
      />

      <template #statusCell="{ data: cellData }">
        <span class="status-badge" :class="'status--' + cellData.value">
          {{ cellData.value }}
        </span>
      </template>
    </DxDataGrid>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import {
  DxDataGrid,
  DxColumn,
  DxSearchPanel,
  DxGroupPanel,
  DxColumnChooser,
  DxPaging,
  DxPager,
} from 'devextreme-vue/data-grid'
import { useSettings } from '../composables/useSettings'

const { entries, loading, lastUpdate, refresh } = useSettings()

const gridHeight = computed(() => `calc(100vh - var(--header-height) - 100px)`)

function groupByPrefix(rowData: { key: string }) {
  const parts = rowData.key.split(':')
  return parts.length >= 2 ? `${parts[0]}:${parts[1]}` : parts[0]
}

function onRowPrepared(e: { rowType: string; data?: { status: string }; rowElement: HTMLElement }) {
  if (e.rowType === 'data' && e.data) {
    if (e.data.status === 'stale') {
      e.rowElement.style.backgroundColor = 'rgba(249, 226, 175, 0.08)'
    } else if (e.data.status === 'missing') {
      e.rowElement.style.backgroundColor = 'rgba(243, 139, 168, 0.08)'
    }
  }
}
</script>

<style scoped>
.settings-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 16px;
}

.last-update {
  font-size: 12px;
  color: var(--text-secondary);
}

.btn-refresh {
  padding: 6px 16px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--bg-surface);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 13px;
  transition: background 0.15s;
}

.btn-refresh:hover { background: var(--border); }
.btn-refresh:disabled { opacity: 0.5; cursor: default; }

.status-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
}

.status--synced { background: rgba(166, 227, 161, 0.15); color: #a6e3a1; }
.status--stale  { background: rgba(249, 226, 175, 0.15); color: #f9e2af; }
.status--missing { background: rgba(243, 139, 168, 0.15); color: #f38ba8; }
</style>
