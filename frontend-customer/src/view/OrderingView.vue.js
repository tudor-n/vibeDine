/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { useCartStore } from '@/stores/useCartStore';
import { useSessionStore } from '@/stores/useSessionStore';
import { useFlashDealStore } from '@/stores/useFlashDealStore';
import { useWebSocket } from '@/composables/useWebSocket';
import { T } from '@/design/tokens';
import WelcomeView from '@/components/screens/WelcomeView.vue';
import PebblesView from '@/components/screens/PebblesView.vue';
import MenuView from '@/components/screens/MenuView.vue';
import CartView from '@/components/screens/CartView.vue';
import FlashDealView from '@/components/screens/FlashDealView.vue';
import OrderSentView from '@/components/screens/OrderSentView.vue';
const route = useRoute();
const cartStore = useCartStore();
const sessionStore = useSessionStore();
const flashDealStore = useFlashDealStore();
useWebSocket();
const screen = ref('welcome');
const activeCategoryId = ref(null);
const lastTotal = ref(0);
onMounted(async () => {
    const tokenFromUrl = route.params.qrToken || 'aaaaaaaa-0000-0000-0000-000000000001';
    try {
        await sessionStore.initializeSession(tokenFromUrl);
    }
    catch (e) {
        console.error('Nu am putut inițializa sesiunea', e);
    }
});
function openMenu() { screen.value = 'pebbles'; }
function openCategory(id) {
    activeCategoryId.value = id;
    screen.value = 'menu';
}
function backToPebbles() { screen.value = 'pebbles'; }
function openCart() { screen.value = 'cart'; }
function backFromCart() { screen.value = 'menu'; }
async function handleSubmitted() {
    lastTotal.value = cartStore.total;
    cartStore.clearCart();
    screen.value = 'sent';
    // Refresh the draft order so the next round of ordering has a fresh id.
    if (sessionStore.qrToken) {
        sessionStore.initializeSession(sessionStore.qrToken).catch(() => undefined);
    }
}
function orderAgain() {
    activeCategoryId.value = null;
    screen.value = 'pebbles';
}
const flashOverlayActive = computed(() => Boolean(flashDealStore.activeDeal));
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['stage']} */ ;
/** @type {__VLS_StyleScopedClasses['phone-frame']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "stage" },
    ...{ style: ({ background: __VLS_ctx.T.paper }) },
});
/** @type {__VLS_StyleScopedClasses['stage']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "phone-frame" },
});
/** @type {__VLS_StyleScopedClasses['phone-frame']} */ ;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
Transition;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    name: "fade",
    mode: "out-in",
}));
const __VLS_2 = __VLS_1({
    name: "fade",
    mode: "out-in",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
if (__VLS_ctx.screen === 'welcome') {
    const __VLS_6 = WelcomeView;
    // @ts-ignore
    const __VLS_7 = __VLS_asFunctionalComponent1(__VLS_6, new __VLS_6({
        ...{ 'onOpenMenu': {} },
        key: "welcome",
    }));
    const __VLS_8 = __VLS_7({
        ...{ 'onOpenMenu': {} },
        key: "welcome",
    }, ...__VLS_functionalComponentArgsRest(__VLS_7));
    let __VLS_11;
    const __VLS_12 = ({ openMenu: {} },
        { onOpenMenu: (__VLS_ctx.openMenu) });
    var __VLS_9;
    var __VLS_10;
}
else if (__VLS_ctx.screen === 'pebbles') {
    const __VLS_13 = PebblesView;
    // @ts-ignore
    const __VLS_14 = __VLS_asFunctionalComponent1(__VLS_13, new __VLS_13({
        ...{ 'onOpenCategory': {} },
        ...{ 'onBack': {} },
        key: "pebbles",
    }));
    const __VLS_15 = __VLS_14({
        ...{ 'onOpenCategory': {} },
        ...{ 'onBack': {} },
        key: "pebbles",
    }, ...__VLS_functionalComponentArgsRest(__VLS_14));
    let __VLS_18;
    const __VLS_19 = ({ openCategory: {} },
        { onOpenCategory: (__VLS_ctx.openCategory) });
    const __VLS_20 = ({ back: {} },
        { onBack: (...[$event]) => {
                if (!!(__VLS_ctx.screen === 'welcome'))
                    return;
                if (!(__VLS_ctx.screen === 'pebbles'))
                    return;
                __VLS_ctx.screen = 'welcome';
                // @ts-ignore
                [T, screen, screen, screen, openMenu, openCategory,];
            } });
    var __VLS_16;
    var __VLS_17;
}
else if (__VLS_ctx.screen === 'menu' && __VLS_ctx.activeCategoryId) {
    const __VLS_21 = MenuView;
    // @ts-ignore
    const __VLS_22 = __VLS_asFunctionalComponent1(__VLS_21, new __VLS_21({
        ...{ 'onBack': {} },
        ...{ 'onOpenCart': {} },
        key: (`menu-${__VLS_ctx.activeCategoryId}`),
        categoryId: (__VLS_ctx.activeCategoryId),
    }));
    const __VLS_23 = __VLS_22({
        ...{ 'onBack': {} },
        ...{ 'onOpenCart': {} },
        key: (`menu-${__VLS_ctx.activeCategoryId}`),
        categoryId: (__VLS_ctx.activeCategoryId),
    }, ...__VLS_functionalComponentArgsRest(__VLS_22));
    let __VLS_26;
    const __VLS_27 = ({ back: {} },
        { onBack: (__VLS_ctx.backToPebbles) });
    const __VLS_28 = ({ openCart: {} },
        { onOpenCart: (__VLS_ctx.openCart) });
    var __VLS_24;
    var __VLS_25;
}
else if (__VLS_ctx.screen === 'cart') {
    const __VLS_29 = CartView;
    // @ts-ignore
    const __VLS_30 = __VLS_asFunctionalComponent1(__VLS_29, new __VLS_29({
        ...{ 'onClose': {} },
        ...{ 'onSubmitted': {} },
        key: "cart",
    }));
    const __VLS_31 = __VLS_30({
        ...{ 'onClose': {} },
        ...{ 'onSubmitted': {} },
        key: "cart",
    }, ...__VLS_functionalComponentArgsRest(__VLS_30));
    let __VLS_34;
    const __VLS_35 = ({ close: {} },
        { onClose: (__VLS_ctx.backFromCart) });
    const __VLS_36 = ({ submitted: {} },
        { onSubmitted: (__VLS_ctx.handleSubmitted) });
    var __VLS_32;
    var __VLS_33;
}
else if (__VLS_ctx.screen === 'sent') {
    const __VLS_37 = OrderSentView;
    // @ts-ignore
    const __VLS_38 = __VLS_asFunctionalComponent1(__VLS_37, new __VLS_37({
        ...{ 'onOrderAgain': {} },
        ...{ 'onCallWaiter': {} },
        key: "sent",
        total: (__VLS_ctx.lastTotal),
    }));
    const __VLS_39 = __VLS_38({
        ...{ 'onOrderAgain': {} },
        ...{ 'onCallWaiter': {} },
        key: "sent",
        total: (__VLS_ctx.lastTotal),
    }, ...__VLS_functionalComponentArgsRest(__VLS_38));
    let __VLS_42;
    const __VLS_43 = ({ orderAgain: {} },
        { onOrderAgain: (__VLS_ctx.orderAgain) });
    const __VLS_44 = ({ callWaiter: {} },
        { onCallWaiter: (() => undefined) });
    var __VLS_40;
    var __VLS_41;
}
// @ts-ignore
[screen, screen, screen, activeCategoryId, activeCategoryId, activeCategoryId, backToPebbles, openCart, backFromCart, handleSubmitted, lastTotal, orderAgain,];
var __VLS_3;
let __VLS_45;
/** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
Transition;
// @ts-ignore
const __VLS_46 = __VLS_asFunctionalComponent1(__VLS_45, new __VLS_45({
    name: "overlay",
}));
const __VLS_47 = __VLS_46({
    name: "overlay",
}, ...__VLS_functionalComponentArgsRest(__VLS_46));
const { default: __VLS_50 } = __VLS_48.slots;
if (__VLS_ctx.flashOverlayActive) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "overlay" },
    });
    /** @type {__VLS_StyleScopedClasses['overlay']} */ ;
    const __VLS_51 = FlashDealView;
    // @ts-ignore
    const __VLS_52 = __VLS_asFunctionalComponent1(__VLS_51, new __VLS_51({}));
    const __VLS_53 = __VLS_52({}, ...__VLS_functionalComponentArgsRest(__VLS_52));
}
// @ts-ignore
[flashOverlayActive,];
var __VLS_48;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
