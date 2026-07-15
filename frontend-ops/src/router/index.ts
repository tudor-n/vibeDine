import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true },
    },
    { path: '/', name: 'fleet', component: () => import('@/views/FleetView.vue') },
    { path: '/connections', name: 'connections', component: () => import('@/views/ConnectionsView.vue') },
    { path: '/metrics', name: 'metrics', component: () => import('@/views/FleetMetricsView.vue') },
    {
      path: '/connections/:id',
      name: 'connection-detail',
      component: () => import('@/views/ConnectionDetailView.vue'),
    },
    { path: '/audit', name: 'audit', component: () => import('@/views/AuditView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  const isPublic = to.meta.public === true

  if (!auth.isAuthenticated && !isPublic) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (auth.isAuthenticated && to.name === 'login') {
    return { name: 'fleet' }
  }
  return true
})

export default router
