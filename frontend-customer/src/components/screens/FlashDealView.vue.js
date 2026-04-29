/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, watch, onUnmounted } from 'vue';
import { T, toneFromKey } from '@/design/tokens';
import { useFlashDealStore } from '@/stores/useFlashDealStore';
import { apiClient } from '@/services/api-client';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import BigButton from '@/components/ui/BigButton.vue';
import FoodTile from '@/components/ui/FoodTile.vue';
const store = useFlashDealStore();
const remaining = ref(0);
const totalSeconds = ref(0);
let timer = null;
const isClaiming = ref(false);
const claimError = ref(null);
const claimSuccess = ref(false);
function tick() {
    const deal = store.activeDeal;
    if (!deal)
        return;
    const expires = new Date(deal.expiresAt).getTime();
    const diff = Math.max(0, Math.floor((expires - Date.now()) / 1000));
    remaining.value = diff;
    if (diff <= 0) {
        if (timer)
            clearInterval(timer);
        timer = null;
        store.clearDeal();
    }
}
function startTimer() {
    if (timer)
        clearInterval(timer);
    const deal = store.activeDeal;
    if (!deal)
        return;
    totalSeconds.value = Math.max(deal.countdownSeconds || 0, 1);
    tick();
    timer = setInterval(tick, 1000);
}
watch(() => store.activeDeal?.id, (id) => {
    claimError.value = null;
    claimSuccess.value = false;
    if (id)
        startTimer();
    else if (timer) {
        clearInterval(timer);
        timer = null;
    }
}, { immediate: true });
onUnmounted(() => {
    if (timer)
        clearInterval(timer);
});
const minutes = computed(() => Math.floor(remaining.value / 60).toString().padStart(2, '0'));
const seconds = computed(() => Math.floor(remaining.value % 60).toString().padStart(2, '0'));
const progressPct = computed(() => {
    if (!totalSeconds.value)
        return 0;
    return Math.max(0, Math.min(100, (remaining.value / totalSeconds.value) * 100));
});
const discountPercent = computed(() => store.activeDeal?.discountPercent ?? 0);
const itemTone = computed(() => {
    const id = store.activeDeal?.menuItem.id ?? 'flash';
    return toneFromKey(id);
});
async function claim() {
    const deal = store.activeDeal;
    const orderId = store.activeDealOrderId;
    if (!deal || !orderId) {
        claimError.value = 'Sesiune invalidă. Reîncărcați pagina.';
        return;
    }
    try {
        isClaiming.value = true;
        claimError.value = null;
        await apiClient.claimFlashDeal(orderId, deal.id);
        claimSuccess.value = true;
        setTimeout(() => store.clearDeal(), 1200);
    }
    catch (e) {
        claimError.value = e instanceof Error ? e.message : 'Eroare la revendicare.';
    }
    finally {
        isClaiming.value = false;
    }
}
function dismiss() {
    store.clearDeal();
}
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
if (__VLS_ctx.store.activeDeal) {
    const __VLS_0 = PhoneScreen || PhoneScreen;
    // @ts-ignore
    const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({}));
    const __VLS_2 = __VLS_1({}, ...__VLS_functionalComponentArgsRest(__VLS_1));
    var __VLS_5 = {};
    const { default: __VLS_6 } = __VLS_3.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "halo" },
        ...{ style: ({ background: `radial-gradient(circle, ${__VLS_ctx.T.accent} 0%, transparent 70%)` }) },
    });
    /** @type {__VLS_StyleScopedClasses['halo']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "header" },
    });
    /** @type {__VLS_StyleScopedClasses['header']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "badge" },
        ...{ style: ({ background: 'rgba(255,74,28,0.1)', color: __VLS_ctx.T.accent }) },
    });
    /** @type {__VLS_StyleScopedClasses['badge']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
        ...{ class: "dot" },
        ...{ style: ({ background: __VLS_ctx.T.accent, boxShadow: `0 0 8px ${__VLS_ctx.T.accent}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['dot']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.dismiss) },
        ...{ class: "dismiss" },
        ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}`, color: __VLS_ctx.T.ink70 }) },
        'aria-label': "Închide oferta",
    });
    /** @type {__VLS_StyleScopedClasses['dismiss']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "hero" },
    });
    /** @type {__VLS_StyleScopedClasses['hero']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
        ...{ class: "title" },
    });
    /** @type {__VLS_StyleScopedClasses['title']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.br)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ style: ({ color: __VLS_ctx.T.accent }) },
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.br)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "lede" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['lede']} */ ;
    (__VLS_ctx.store.activeDeal.reason || 'O porție specială la preț redus, doar câteva minute.');
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "deal-card" },
        ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['deal-card']} */ ;
    const __VLS_7 = FoodTile;
    // @ts-ignore
    const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
        tone: (__VLS_ctx.itemTone),
        image: (__VLS_ctx.store.activeDeal.menuItem.imageUrl),
        radius: (16),
        ...{ class: "deal-tile" },
    }));
    const __VLS_9 = __VLS_8({
        tone: (__VLS_ctx.itemTone),
        image: (__VLS_ctx.store.activeDeal.menuItem.imageUrl),
        radius: (16),
        ...{ class: "deal-tile" },
    }, ...__VLS_functionalComponentArgsRest(__VLS_8));
    /** @type {__VLS_StyleScopedClasses['deal-tile']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "deal-body" },
    });
    /** @type {__VLS_StyleScopedClasses['deal-body']} */ ;
    if (__VLS_ctx.discountPercent) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "discount" },
            ...{ style: ({ background: __VLS_ctx.T.lime, color: __VLS_ctx.T.ink }) },
        });
        /** @type {__VLS_StyleScopedClasses['discount']} */ ;
        (__VLS_ctx.discountPercent);
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "deal-name" },
    });
    /** @type {__VLS_StyleScopedClasses['deal-name']} */ ;
    (__VLS_ctx.store.activeDeal.menuItem.name);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "deal-meta" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['deal-meta']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "prices" },
    });
    /** @type {__VLS_StyleScopedClasses['prices']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "price-now" },
        ...{ style: ({ color: __VLS_ctx.T.accent }) },
    });
    /** @type {__VLS_StyleScopedClasses['price-now']} */ ;
    (__VLS_ctx.store.activeDeal.discountedPrice);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "price-unit" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['price-unit']} */ ;
    if (__VLS_ctx.store.activeDeal.menuItem.price !== __VLS_ctx.store.activeDeal.discountedPrice) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "price-was" },
            ...{ style: ({ color: __VLS_ctx.T.ink40 }) },
        });
        /** @type {__VLS_StyleScopedClasses['price-was']} */ ;
        (__VLS_ctx.store.activeDeal.menuItem.price);
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "countdown" },
        ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['countdown']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "countdown-label" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['countdown-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "countdown-row" },
    });
    /** @type {__VLS_StyleScopedClasses['countdown-row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "num" },
    });
    /** @type {__VLS_StyleScopedClasses['num']} */ ;
    (__VLS_ctx.minutes);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "colon" },
        ...{ style: ({ color: __VLS_ctx.T.accent }) },
    });
    /** @type {__VLS_StyleScopedClasses['colon']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "num" },
    });
    /** @type {__VLS_StyleScopedClasses['num']} */ ;
    (__VLS_ctx.seconds);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "unit" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['unit']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "bar" },
        ...{ style: ({ background: __VLS_ctx.T.paper, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['bar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "fill" },
        ...{ style: ({
                width: `${__VLS_ctx.progressPct}%`,
                background: `linear-gradient(90deg, ${__VLS_ctx.T.accent}, ${__VLS_ctx.T.lime})`,
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['fill']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cta" },
    });
    /** @type {__VLS_StyleScopedClasses['cta']} */ ;
    const __VLS_12 = BigButton || BigButton;
    // @ts-ignore
    const __VLS_13 = __VLS_asFunctionalComponent1(__VLS_12, new __VLS_12({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isClaiming || __VLS_ctx.claimSuccess),
    }));
    const __VLS_14 = __VLS_13({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isClaiming || __VLS_ctx.claimSuccess),
    }, ...__VLS_functionalComponentArgsRest(__VLS_13));
    let __VLS_17;
    const __VLS_18 = ({ click: {} },
        { onClick: (__VLS_ctx.claim) });
    const { default: __VLS_19 } = __VLS_15.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.claimSuccess
        ? 'Adăugat ✓'
        : __VLS_ctx.isClaiming
            ? 'Se adaugă...'
            : 'Adaugă în comandă');
    // @ts-ignore
    [store, store, store, store, store, store, store, store, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, dismiss, itemTone, discountPercent, discountPercent, minutes, seconds, progressPct, isClaiming, isClaiming, claimSuccess, claimSuccess, claim,];
    var __VLS_15;
    var __VLS_16;
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (__VLS_ctx.dismiss) },
        ...{ class: "reject" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['reject']} */ ;
    if (__VLS_ctx.claimError) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
            ...{ class: "claim-error" },
        });
        /** @type {__VLS_StyleScopedClasses['claim-error']} */ ;
        (__VLS_ctx.claimError);
    }
    // @ts-ignore
    [T, dismiss, claimError, claimError,];
    var __VLS_3;
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
