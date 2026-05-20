import { createRouter, createWebHistory } from 'vue-router';
import DashboardView from '@/views/DashboardView.vue';
import { useAuthStore } from '@/stores/useAuthStore';

const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [
        {
            path: '/login',
            name: 'login',
            component: () => import('@/views/LoginView.vue'),
            meta: { public: true },
        },
        {
            path: '/confirm-email',
            name: 'confirm-email',
            component: () => import('@/views/ConfirmEmailView.vue'),
            meta: { public: true },
        },
        {
            path: '/onboarding',
            name: 'onboarding',
            component: () => import('@/views/OnboardingView.vue'),
        },
        {
            path: '/accept-invite',
            name: 'accept-invite',
            component: () => import('@/views/AcceptInviteView.vue'),
        },
        {
            path: '/',
            name: 'dashboard',
            component: DashboardView
        },
        {
            path: '/menu',
            name: 'menu',
            component: () => import('@/views/MenuManagementView.vue')
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
        },
        {
            path: '/orders/active',
            name: 'orders-active',
            component: () => import('@/views/ActiveOrdersView.vue')
        },
        {
            path: '/orders/active/board',
            name: 'orders-active-board',
            component: () => import('@/views/ActiveOrdersBoardView.vue')
        },
        {
            path: '/tables',
            name: 'tables',
            component: () => import('@/views/TablesView.vue')
        },
        {
            path: '/organization',
            name: 'organization',
            component: () => import('@/views/OrganizationView.vue')
        },
        {
            path: '/menu/import',
            name: 'menu-import',
            component: () => import('@/views/MenuImportView.vue'),
        }
    ]
})

router.beforeEach(async (to) => {
    const auth = useAuthStore()
    const isPublic = to.meta.public === true

    if (!auth.isAuthenticated) {
        return isPublic ? true : { name: 'login', query: { redirect: to.fullPath } }
    }

    // Authenticated visiting a public page → go to the dashboard.
    if (isPublic) return { path: '/' }

    // After a full-page reload the token survives (localStorage) but the
    // in-memory profile/memberships and restaurant list do not. Restore both
    // before any view evaluates permissions like `isOrgOwnerOfActive`.
    if (!auth.user || auth.accessibleRestaurants.length === 0) {
        try {
            await Promise.all([
                auth.user ? Promise.resolve() : auth.fetchMe(),
                auth.accessibleRestaurants.length ? Promise.resolve() : auth.loadAccessibleRestaurants(),
            ])
        } catch {
            auth.logout()
            return { name: 'login', query: { redirect: to.fullPath } }
        }
    }

    // Accepting an invite is allowed even with no restaurants yet.
    if (to.name === 'accept-invite') return true

    // No org/restaurant yet → onboarding.
    if (auth.accessibleRestaurants.length === 0) {
        return to.name === 'onboarding' ? true : { name: 'onboarding' }
    }
    if (to.name === 'onboarding') return { path: '/' }

    return true
})

export default router
