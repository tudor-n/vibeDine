import { createRouter, createWebHistory} from 'vue-router';
import DashboardView from '@/views/DashboardView.vue';

const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [
        {
            path: '/',
            name: 'dashboard',
            component: DashboardView
        },

        {
            path: '/ai-config',
            name: 'ai-config',
            component: () => import('@/views/AIConfigView.vue')
        },

        {
            path: '/rules',
            name: 'rules',
            component: () => import('@/views/RulesManagementView.vue')
        },

        {
            path: '/orders',
            name: 'orders',
            component: () => import('@/views/OrdersLogView.vue')
        }
    ]
})

export default router