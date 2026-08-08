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
            path: '/inventory',
            name: 'inventory',
            component: () => import('@/views/InventoryView.vue')
        },
        {
            path: '/happy-hour',
            name: 'happy-hour',
            component: () => import('@/views/HappyHourView.vue')
        },
        {
            path: '/flash-deal',
            name: 'flash-deal',
            component: () => import('@/views/FlashDealView.vue')
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
            path: '/waiter-calls',
            name: 'waiter-calls',
            component: () => import('@/views/WaiterCallsView.vue')
        },
        {
            path: '/floor',
            name: 'floor',
            component: () => import('@/views/FloorPlanView.vue')
        },
        // ── Waiter Mode ───────────────────────────────────────────────────────
        // These three names (and only these) render inside WaiterShell — App.vue
        // matches on the exact set, never on a 'waiter' prefix.
        {
            path: '/waiter',
            name: 'waiter-floor',
            component: () => import('@/views/waiter/WaiterFloorView.vue')
        },
        {
            path: '/waiter/table/:tableId',
            name: 'waiter-table',
            component: () => import('@/views/waiter/WaiterTableView.vue')
        },
        {
            path: '/waiter/table/:tableId/order',
            name: 'waiter-order',
            component: () => import('@/views/waiter/WaiterOrderView.vue')
        },
        {
            path: '/waiter/orders',
            name: 'waiter-active-orders',
            component: () => import('@/views/waiter/WaiterActiveOrdersView.vue')
        },
        {
            path: '/tables',
            name: 'tables',
            component: () => import('@/views/TablesView.vue')
        },
        {
            // Legacy entry point — the entire panel has moved into Settings → Colaboratori.
            // Keep the path so any old bookmark forwards to the new location.
            path: '/organization',
            redirect: { name: 'settings-collaborators' },
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
                {
                    path: 'collaborators',
                    name: 'settings-collaborators',
                    component: () => import('@/views/Settings/CollaboratorsTab.vue'),
                },
                {
                    path: 'brand',
                    name: 'settings-brand',
                    component: () => import('@/views/Settings/BrandTab.vue'),
                },
            ],
        },
    ]
})

// Public pages an authenticated user should still be allowed to visit (e.g. clicking a
// waiter signup link in the same browser they're logged into as a manager). Pages like
// 'login' / 'confirm-email' don't belong in this list — being already authenticated
// makes them meaningless, so we keep redirecting away from them.
const PUBLIC_ALLOWED_WHEN_AUTHENTICATED = new Set(['waiter-signup', 'awaiting-approval'])

router.beforeEach(async (to) => {
    const auth = useAuthStore()
    const isPublic = to.meta.public === true

    if (!auth.isAuthenticated) {
        return isPublic ? true : { name: 'login', query: { redirect: to.fullPath } }
    }

    // Authenticated visiting a public page → go to home (guard will then steer correctly),
    // except for a small whitelist of pages that are still useful when signed in.
    if (isPublic && !PUBLIC_ALLOWED_WHEN_AUTHENTICATED.has(String(to.name))) {
        return { path: '/' }
    }

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

    // Waiters have access to Waiter Mode (their default home), active orders, the
    // orders log, waiter calls, the floor plan (claim-only — to take orders via
    // "Preia comanda"), the picker, and the settings profile tab. Anything else
    // (menu, AI config, rules, tables, organization, waiter management, dashboard)
    // is rerouted into Waiter Mode. This set is gating place ① of three (the others
    // are App.vue's visibility flags and the per-view computeds).
    const WAITER_ALLOWED = new Set([
        'waiter-floor',
        'waiter-table',
        'waiter-order',
        'waiter-active-orders',
        'orders-active',
        'orders',
        'waiter-calls',
        'floor',
        'org-picker',
        'settings-profile',
    ])
    if (auth.isWaiter && !WAITER_ALLOWED.has(String(to.name))) {
        return { name: 'waiter-floor' }
    }

    // Active org selected — the picker is still allowed (the user clicked "Switch organization").
    return true
})

export default router
