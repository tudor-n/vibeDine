/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed } from 'vue';
import { T, toneFromKey } from '@/design/tokens';
import { useCartStore } from '@/stores/useCartStore';
import { useSessionStore } from '@/stores/useSessionStore';
import { apiClient } from '@/services/api-client';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import BigButton from '@/components/ui/BigButton.vue';
import FoodTile from '@/components/ui/FoodTile.vue';
const emit = defineEmits();
const cartStore = useCartStore();
const sessionStore = useSessionStore();
const isSubmitting = ref(false);
const submitError = ref(null);
const tableLabel = computed(() => {
    const id = sessionStore.tableId;
    if (!id)
        return 'Masa';
    const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2);
    return `Masa ${tail}`;
});
const itemCount = computed(() => cartStore.items.reduce((sum, i) => sum + i.quantity, 0));
const prepEstimate = computed(() => {
    // Rough heuristic: 8 minutes base + 2 min per item.
    return Math.max(12, 8 + itemCount.value * 2);
});
async function submit() {
    if (!sessionStore.orderId) {
        submitError.value = 'Sesiunea nu este validă.';
        return;
    }
    if (cartStore.items.length === 0) {
        submitError.value = 'Coșul este gol.';
        return;
    }
    try {
        isSubmitting.value = true;
        submitError.value = null;
        await apiClient.submitOrder(sessionStore.orderId);
        emit('submitted');
    }
    catch (e) {
        submitError.value = e instanceof Error ? e.message : 'Eroare la trimitere.';
    }
    finally {
        isSubmitting.value = false;
    }
}
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['row']} */ ;
/** @type {__VLS_StyleScopedClasses['row']} */ ;
const __VLS_0 = PhoneScreen || PhoneScreen;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({}));
const __VLS_2 = __VLS_1({}, ...__VLS_functionalComponentArgsRest(__VLS_1));
var __VLS_5 = {};
const { default: __VLS_6 } = __VLS_3.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "header" },
});
/** @type {__VLS_StyleScopedClasses['header']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "status-table" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['status-table']} */ ;
(__VLS_ctx.tableLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "title" },
});
/** @type {__VLS_StyleScopedClasses['title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.emit('close');
            // @ts-ignore
            [T, tableLabel, emit,];
        } },
    ...{ class: "close-btn" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}`, color: __VLS_ctx.T.ink }) },
    'aria-label': "Închide",
});
/** @type {__VLS_StyleScopedClasses['close-btn']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "lede" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['lede']} */ ;
if (__VLS_ctx.cartStore.items.length > 0) {
    (__VLS_ctx.itemCount);
    (__VLS_ctx.itemCount === 1 ? 'preparat selectat' : 'preparate selectate');
    (__VLS_ctx.prepEstimate);
}
else {
}
if (__VLS_ctx.cartStore.items.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "items" },
    });
    /** @type {__VLS_StyleScopedClasses['items']} */ ;
    for (const [it] of __VLS_vFor((__VLS_ctx.cartStore.items))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (it.id),
            ...{ class: "item" },
            ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
        });
        /** @type {__VLS_StyleScopedClasses['item']} */ ;
        const __VLS_7 = FoodTile;
        // @ts-ignore
        const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
            tone: (__VLS_ctx.toneFromKey(it.id)),
            image: (it.imageUrl),
            radius: (14),
            ...{ class: "item-tile" },
        }));
        const __VLS_9 = __VLS_8({
            tone: (__VLS_ctx.toneFromKey(it.id)),
            image: (it.imageUrl),
            radius: (14),
            ...{ class: "item-tile" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_8));
        /** @type {__VLS_StyleScopedClasses['item-tile']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "item-body" },
        });
        /** @type {__VLS_StyleScopedClasses['item-body']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "item-name" },
        });
        /** @type {__VLS_StyleScopedClasses['item-name']} */ ;
        (it.name);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "item-row" },
        });
        /** @type {__VLS_StyleScopedClasses['item-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "item-price" },
        });
        /** @type {__VLS_StyleScopedClasses['item-price']} */ ;
        (it.price * it.quantity);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "item-price-unit" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
        });
        /** @type {__VLS_StyleScopedClasses['item-price-unit']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "stepper" },
            ...{ style: ({ background: __VLS_ctx.T.paper, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
        });
        /** @type {__VLS_StyleScopedClasses['stepper']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!(__VLS_ctx.cartStore.items.length > 0))
                        return;
                    __VLS_ctx.cartStore.removeItem(it.id);
                    // @ts-ignore
                    [T, T, T, T, T, T, T, T, T, cartStore, cartStore, cartStore, cartStore, itemCount, itemCount, prepEstimate, toneFromKey,];
                } },
            ...{ class: "stepper-btn" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
            'aria-label': "Scade cantitatea",
        });
        /** @type {__VLS_StyleScopedClasses['stepper-btn']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "qty" },
        });
        /** @type {__VLS_StyleScopedClasses['qty']} */ ;
        (it.quantity);
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!(__VLS_ctx.cartStore.items.length > 0))
                        return;
                    __VLS_ctx.cartStore.addItem(it);
                    // @ts-ignore
                    [T, cartStore,];
                } },
            ...{ class: "stepper-btn" },
            ...{ style: ({ color: __VLS_ctx.T.accent }) },
            'aria-label': "Crește cantitatea",
        });
        /** @type {__VLS_StyleScopedClasses['stepper-btn']} */ ;
        // @ts-ignore
        [T,];
    }
}
if (__VLS_ctx.cartStore.items.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "summary-wrap" },
    });
    /** @type {__VLS_StyleScopedClasses['summary-wrap']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "summary" },
        ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['summary']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "row" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.cartStore.total);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "row" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['row']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "total" },
        ...{ style: ({ borderTop: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['total']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "total-label" },
    });
    /** @type {__VLS_StyleScopedClasses['total-label']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "total-value" },
    });
    /** @type {__VLS_StyleScopedClasses['total-value']} */ ;
    (__VLS_ctx.cartStore.total);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "total-unit" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['total-unit']} */ ;
}
if (__VLS_ctx.submitError) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "error" },
    });
    /** @type {__VLS_StyleScopedClasses['error']} */ ;
    (__VLS_ctx.submitError);
}
if (__VLS_ctx.cartStore.items.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cta" },
    });
    /** @type {__VLS_StyleScopedClasses['cta']} */ ;
    const __VLS_12 = BigButton || BigButton;
    // @ts-ignore
    const __VLS_13 = __VLS_asFunctionalComponent1(__VLS_12, new __VLS_12({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isSubmitting),
    }));
    const __VLS_14 = __VLS_13({
        ...{ 'onClick': {} },
        disabled: (__VLS_ctx.isSubmitting),
    }, ...__VLS_functionalComponentArgsRest(__VLS_13));
    let __VLS_17;
    const __VLS_18 = ({ click: {} },
        { onClick: (__VLS_ctx.submit) });
    const { default: __VLS_19 } = __VLS_15.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
    (__VLS_ctx.isSubmitting ? 'Se trimite...' : 'Trimite la bucătărie');
    // @ts-ignore
    [T, T, T, T, T, T, cartStore, cartStore, cartStore, cartStore, submitError, submitError, isSubmitting, isSubmitting, submit,];
    var __VLS_15;
    var __VLS_16;
}
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
});
export default {};
