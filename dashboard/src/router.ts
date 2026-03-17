import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/technical/settings',
    },
    {
      path: '/technical',
      redirect: '/technical/settings',
    },
    {
      path: '/technical/settings',
      name: 'Settings',
      component: () => import('./views/SettingsView.vue'),
    },
    {
      path: '/technical/metrics',
      name: 'Metrics',
      component: () => import('./views/MetricsView.vue'),
    },
  ],
})

export default router
