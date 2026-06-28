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
    { path: '/', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
    { path: '/organizations', name: 'organizations', component: () => import('@/views/OrganizationsView.vue') },
    {
      path: '/organizations/:id',
      name: 'organization-detail',
      component: () => import('@/views/OrganizationDetailView.vue'),
    },
    { path: '/locations', name: 'locations', component: () => import('@/views/LocationsView.vue') },
    {
      path: '/locations/:id',
      name: 'location-detail',
      component: () => import('@/views/LocationDetailView.vue'),
    },
    {
      path: '/registration-links',
      name: 'registration-links',
      component: () => import('@/views/RegistrationLinksView.vue'),
    },
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
    return { name: 'dashboard' }
  }
  return true
})

export default router
