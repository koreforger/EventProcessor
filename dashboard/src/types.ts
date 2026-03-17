export interface SettingEntry {
  key: string
  databaseValue: string | null
  activeValue: string | null
  status: 'synced' | 'stale' | 'missing'
  lastModified?: string
}

export interface SettingsChangedEvent {
  timestamp: string
  changes: SettingEntry[]
}

export interface MetricsSnapshot {
  timestamp: string
  counters: Record<string, number>
  gauges: Record<string, number>
  rates: Record<string, number>
}
