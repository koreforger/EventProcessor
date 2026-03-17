<template>
  <div class="app-layout">
    <aside class="sidebar">
      <div class="sidebar-header">
        <div class="logo">
          <span class="logo-icon">◆</span>
          <span class="logo-text">EventProcessor</span>
        </div>
      </div>

      <nav class="sidebar-nav">
        <div v-for="section in navSections" :key="section.title" class="nav-section">
          <div class="nav-section-title">{{ section.title }}</div>
          <router-link
            v-for="item in section.items"
            :key="item.path"
            :to="item.path"
            class="nav-item"
            active-class="nav-item--active"
          >
            <span class="nav-icon">{{ item.icon }}</span>
            <span class="nav-label">{{ item.label }}</span>
          </router-link>
        </div>
      </nav>

      <div class="sidebar-footer">
        <ConnectionBadge />
      </div>
    </aside>

    <main class="main-content">
      <header class="content-header">
        <h1 class="page-title">{{ currentPageTitle }}</h1>
      </header>
      <div class="content-body">
        <router-view />
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import ConnectionBadge from './components/ConnectionBadge.vue'

const route = useRoute()

const navSections = [
  {
    title: 'Technical',
    items: [
      { path: '/technical/settings', label: 'Settings', icon: '⚙' },
      { path: '/technical/metrics', label: 'Metrics', icon: '📊' },
    ],
  },
  // Future: Operational section
  // {
  //   title: 'Operational',
  //   items: [
  //     { path: '/operational/sessions', label: 'Sessions', icon: '🔒' },
  //     { path: '/operational/decisions', label: 'Decisions', icon: '⚖' },
  //   ],
  // },
]

const currentPageTitle = computed(() => {
  return (route.name as string) ?? 'Dashboard'
})
</script>

<style scoped>
.app-layout {
  display: flex;
  height: 100vh;
  overflow: hidden;
}

.sidebar {
  width: var(--nav-width);
  background: var(--bg-secondary);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
}

.sidebar-header {
  height: var(--header-height);
  display: flex;
  align-items: center;
  padding: 0 16px;
  border-bottom: 1px solid var(--border);
}

.logo {
  display: flex;
  align-items: center;
  gap: 8px;
}

.logo-icon {
  color: var(--accent);
  font-size: 18px;
}

.logo-text {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.sidebar-nav {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}

.nav-section {
  margin-bottom: 8px;
}

.nav-section-title {
  padding: 8px 16px 4px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-secondary);
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  margin: 1px 8px;
  border-radius: 6px;
  color: var(--text-secondary);
  text-decoration: none;
  font-size: 13px;
  transition: background 0.15s, color 0.15s;
}

.nav-item:hover {
  background: var(--bg-surface);
  color: var(--text-primary);
}

.nav-item--active {
  background: var(--bg-surface);
  color: var(--accent);
}

.nav-icon {
  font-size: 14px;
  width: 20px;
  text-align: center;
}

.sidebar-footer {
  padding: 12px 16px;
  border-top: 1px solid var(--border);
}

.main-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.content-header {
  height: var(--header-height);
  display: flex;
  align-items: center;
  padding: 0 24px;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.page-title {
  font-size: 16px;
  font-weight: 600;
}

.content-body {
  flex: 1;
  overflow-y: auto;
  padding: 24px;
}
</style>
