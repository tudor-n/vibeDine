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
            path: '/select-organization',
            name: 'org-picker',
            component: () => import('@/views/OrganizationPickerView.vue'),
        },
        {
            path: '/accept-invite',
            name: 'accept-invite',
            component: () => import('@/views/AcceptInviteView.vue'),
        },
        {
            path: '/waiter-signup/:token',
            name: 'waiter-signup',
            component: () => import('@/views/WaiterSignupView.vue'),
            meta: { public: true },
        },
        {
            path: '/awaiting-approval',
            name: 'awaiting-approval',
            component: () => import('@/views/AwaitingApprovalView.vue'),
            meta: { public: true },
        },
        {
            path: '/waiters',
            name: 'waiters',
            component: () => import('@/views/WaiterManagementView.vue'),
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
            component: () => import('@/views/ActiveOrdersBoardView.vue')
        },
        {
            path: '/orders/active/board',
            redirect: { name: 'orders-active' },
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
        },
        {
            path: '/settings',
            component: () => import('@/views/SettingsLayout.vue'),
            redirect: { name: 'settings-profile' },
            children: [
                {
                    path: 'profile',
                    name: 'settings-profile',
                    component: () => import('@/views/Settings/ProfileTab.vue'),
                },
                {
                    path: 'organizations',
                    name: 'settings-organizations',
                    component: () => import('@/views/Settings/OrganizationsTab.vue'),
                },
                {
                    path: 'employees',
                    name: 'settings-employees',
                    component: () => import('@/views/Settings/EmployeesTab.vue'),
                },
            ],
        },
    ]
})

router.beforeEach(async (to) => {
    const auth = useAuthStore()
    const isPublic = to.meta.public === true

    if (!auth.isAuthenticated) {
        return isPublic ? true : { name: 'login', query: { redirect: to.fullPath } }
    }

    // Authenticated visiting a public page → go to home (guard will then steer correctly).
    if (isPublic) return { path: '/' }

    // After a full-page reload the token survives (localStorage) but the in-memory
    // profile/memberships and org list do not. Restore both before any view evaluates
    // permissions like `isOrgOwnerOfActive` / `isWaiter`.
    if (!auth.user || auth.accessibleOrganizations.length === 0) {
        try {
            await Promise.all([
                auth.user ? Promise.resolve() : auth.fetchMe(),
                auth.accessibleOrganizations.length
                    ? Promise.resolve()
                    : auth.loadAccessibleOrganizations(),
            ])
        } catch {
            auth.logout()
            return { name: 'login', query: { redirect: to.fullPath } }
        }
    }

    // Accepting an invite is allowed even with no orgs yet.
    if (to.name === 'accept-invite') return true

    // No org → onboarding (create the first one).
    if (auth.accessibleOrganizations.length === 0) {
        return to.name === 'onboarding' ? true : { name: 'onboarding' }
    }
    if (to.name === 'onboarding') return { name: 'org-picker' }

    // Has orgs but none selected → force the picker (always shown, even with 1 org).
    if (!auth.activeOrganizationId) {
        return to.name === 'org-picker' ? true : { name: 'org-picker' }
    }

    // Waiters only have access to active orders, the orders log, the picker, and the
    // settings profile tab. Anything else (menu, AI config, rules, tables, organization,
    // waiter management, dashboard) is rerouted to the active orders board.
    const WAITER_ALLOWED = new Set([
        'orders-active',
        'orders',
        'org-picker',
        'settings-profile',
    ])
    if (auth.isWaiter && !WAITER_ALLOWED.has(String(to.name))) {
        return { name: 'orders-active' }
    }

    // Active org selected — the picker is still allowed (the user clicked "Switch organization").
    return true
})

export default router
