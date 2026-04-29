/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, watch, onUnmounted, computed } from 'vue';
import { useFlashDealStore } from '@/stores/useFlashDealStore';
import { apiClient } from '@/services/api-client';
const flashDealStore = useFlashDealStore();
const timeLeft = ref(0);
let timerInterval = null;
const isClaiming = ref(false);
const claimError = ref(null);
const claimSuccess = ref(false);
const formattedTime = computed(() => {
    const m = Math.floor(timeLeft.value / 60).toString().padStart(2, '0');
    const s = (timeLeft.value % 60).toString().padStart(2, '0');
    return `${m}:${s}`;
});
const startTimer = () => {
    if (timerInterval)
        clearInterval(timerInterval);
    const deal = flashDealStore.activeDeal;
    if (!deal)
        return;
    const updateTime = () => {
        const now = new Date().getTime();
        const expires = new Date(deal.expiresAt).getTime();
        const diff = Math.floor((expires - now) / 1000);
        if (diff <= 0) {
            timeLeft.value = 0;
            clearInterval(timerInterval);
            flashDealStore.clearDeal();
        }
        else {
            timeLeft.value = diff;
        }
    };
    updateTime();
    timerInterval = setInterval(updateTime, 1000);
};
watch(() => flashDealStore.activeDeal, (newDeal) => {
    if (newDeal) {
        claimError.value = null;
        claimSuccess.value = false;
        startTimer();
    }
    else {
        if (timerInterval)
            clearInterval(timerInterval);
    }
}, { immediate: true });
onUnmounted(() => {
    if (timerInterval)
        clearInterval(timerInterval);
});
const claimDeal = async () => {
    const deal = flashDealStore.activeDeal;
    const orderId = flashDealStore.activeDealOrderId;
    if (!deal || !orderId) {
        claimError.value = 'Sesiune invalidă. Te rugăm să reîncărci pagina.';
        return;
    }
    try {
        isClaiming.value = true;
        claimError.value = null;
        // Backend creates the OrderItem — we don't need addLocalItemOnly
        await apiClient.claimFlashDeal(orderId, deal.id);
        claimSuccess.value = true;
        // Short delay so user sees the success state before banner disappears
        setTimeout(() => {
            flashDealStore.clearDeal();
        }, 1500);
    }
    catch (error) {
        if (error instanceof Error) {
            claimError.value = error.message;
        }
        else {
            claimError.value = 'Eroare la revendicarea ofertei. Încearcă din nou.';
        }
    }
    finally {
        isClaiming.value = false;
    }
};
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
let __VLS_0;
/** @ts-ignore @type {typeof __VLS_components.Transition | typeof __VLS_components.Transition} */
Transition;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    name: "slide-down",
}));
const __VLS_2 = __VLS_1({
    name: "slide-down",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
const { default: __VLS_5 } = __VLS_3.slots;
if (__VLS_ctx.flashDealStore.activeDeal) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "fixed top-0 left-0 right-0 z-50 bg-brand text-white shadow-xl px-4 py-3 border-b-4 border-yellow-400" },
    });
    /** @type {__VLS_StyleScopedClasses['fixed']} */ ;
    /** @type {__VLS_StyleScopedClasses['top-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['left-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['right-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['z-50']} */ ;
    /** @type {__VLS_StyleScopedClasses['bg-brand']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-white']} */ ;
    /** @type {__VLS_StyleScopedClasses['shadow-xl']} */ ;
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-3']} */ ;
    /** @type {__VLS_StyleScopedClasses['border-b-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['border-yellow-400']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "max-w-3xl mx-auto flex flex-col md:flex-row items-center justify-between gap-4" },
    });
    /** @type {__VLS_StyleScopedClasses['max-w-3xl']} */ ;
    /** @type {__VLS_StyleScopedClasses['mx-auto']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
    /** @type {__VLS_StyleScopedClasses['md:flex-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-between']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex-1 text-center md:text-left" },
    });
    /** @type {__VLS_StyleScopedClasses['flex-1']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['md:text-left']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex items-center justify-center md:justify-start gap-2 mb-1" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['md:justify-start']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-xl animate-bounce" },
    });
    /** @type {__VLS_StyleScopedClasses['text-xl']} */ ;
    /** @type {__VLS_StyleScopedClasses['animate-bounce']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
        ...{ class: "font-black text-lg uppercase tracking-wider text-yellow-300" },
    });
    /** @type {__VLS_StyleScopedClasses['font-black']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
    /** @type {__VLS_StyleScopedClasses['tracking-wider']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-yellow-300']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "font-medium" },
    });
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    (__VLS_ctx.flashDealStore.activeDeal.menuItem.name);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "mt-1 flex items-center justify-center md:justify-start gap-3" },
    });
    /** @type {__VLS_StyleScopedClasses['mt-1']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['md:justify-start']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-2xl font-bold" },
    });
    /** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
    (__VLS_ctx.flashDealStore.activeDeal.discountedPrice);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "line-through text-brand-light opacity-80 text-sm" },
    });
    /** @type {__VLS_StyleScopedClasses['line-through']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-brand-light']} */ ;
    /** @type {__VLS_StyleScopedClasses['opacity-80']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    (__VLS_ctx.flashDealStore.activeDeal.menuItem.price);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex flex-col items-center gap-2 w-full md:w-auto" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['w-full']} */ ;
    /** @type {__VLS_StyleScopedClasses['md:w-auto']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex items-center gap-4" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "bg-black/20 px-4 py-2 rounded-lg font-mono text-2xl font-bold min-w-[90px] text-center shadow-inner" },
    });
    /** @type {__VLS_StyleScopedClasses['bg-black/20']} */ ;
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['rounded-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
    /** @type {__VLS_StyleScopedClasses['min-w-[90px]']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['shadow-inner']} */ ;
    (__VLS_ctx.formattedTime);
    if (__VLS_ctx.claimSuccess) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "flex-1 md:flex-none bg-green-400 text-green-900 font-black py-3 px-6 rounded-xl shadow-lg flex items-center gap-2" },
        });
        /** @type {__VLS_StyleScopedClasses['flex-1']} */ ;
        /** @type {__VLS_StyleScopedClasses['md:flex-none']} */ ;
        /** @type {__VLS_StyleScopedClasses['bg-green-400']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-green-900']} */ ;
        /** @type {__VLS_StyleScopedClasses['font-black']} */ ;
        /** @type {__VLS_StyleScopedClasses['py-3']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-6']} */ ;
        /** @type {__VLS_StyleScopedClasses['rounded-xl']} */ ;
        /** @type {__VLS_StyleScopedClasses['shadow-lg']} */ ;
        /** @type {__VLS_StyleScopedClasses['flex']} */ ;
        /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
        /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (__VLS_ctx.claimDeal) },
            disabled: (__VLS_ctx.isClaiming),
            ...{ class: "flex-1 md:flex-none bg-yellow-400 text-red-900 font-black py-3 px-6 rounded-xl shadow-lg hover:bg-yellow-300 active:scale-95 transition-all disabled:opacity-70 disabled:cursor-not-allowed" },
        });
        /** @type {__VLS_StyleScopedClasses['flex-1']} */ ;
        /** @type {__VLS_StyleScopedClasses['md:flex-none']} */ ;
        /** @type {__VLS_StyleScopedClasses['bg-yellow-400']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-red-900']} */ ;
        /** @type {__VLS_StyleScopedClasses['font-black']} */ ;
        /** @type {__VLS_StyleScopedClasses['py-3']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-6']} */ ;
        /** @type {__VLS_StyleScopedClasses['rounded-xl']} */ ;
        /** @type {__VLS_StyleScopedClasses['shadow-lg']} */ ;
        /** @type {__VLS_StyleScopedClasses['hover:bg-yellow-300']} */ ;
        /** @type {__VLS_StyleScopedClasses['active:scale-95']} */ ;
        /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
        /** @type {__VLS_StyleScopedClasses['disabled:opacity-70']} */ ;
        /** @type {__VLS_StyleScopedClasses['disabled:cursor-not-allowed']} */ ;
        (__VLS_ctx.isClaiming ? 'Se adaugă...' : 'Revendică');
    }
    if (__VLS_ctx.claimError) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
            ...{ class: "text-xs text-yellow-200 bg-black/20 px-3 py-1 rounded-lg" },
        });
        /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-yellow-200']} */ ;
        /** @type {__VLS_StyleScopedClasses['bg-black/20']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-3']} */ ;
        /** @type {__VLS_StyleScopedClasses['py-1']} */ ;
        /** @type {__VLS_StyleScopedClasses['rounded-lg']} */ ;
        (__VLS_ctx.claimError);
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.flashDealStore.activeDeal))
                    return;
                __VLS_ctx.flashDealStore.clearDeal();
                // @ts-ignore
                [flashDealStore, flashDealStore, flashDealStore, flashDealStore, flashDealStore, formattedTime, claimSuccess, claimDeal, isClaiming, isClaiming, claimError, claimError,];
            } },
        ...{ class: "absolute top-2 right-2 text-white/70 hover:text-white" },
    });
    /** @type {__VLS_StyleScopedClasses['absolute']} */ ;
    /** @type {__VLS_StyleScopedClasses['top-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['right-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-white/70']} */ ;
    /** @type {__VLS_StyleScopedClasses['hover:text-white']} */ ;
}
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
