/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, onMounted } from 'vue';
import { T, toneFromKey } from '@/design/tokens';
import { useMenu } from '@/composables/useMenu';
import { useCartStore } from '@/stores/useCartStore';
import { useRecommendations } from '@/composables/useRecommendations';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import FoodTile from '@/components/ui/FoodTile.vue';
const props = defineProps();
const emit = defineEmits();
const { categories, isLoading, error, load, productsByCategory } = useMenu();
const cartStore = useCartStore();
const { pairings, upsells } = useRecommendations();
const filter = ref('all');
onMounted(() => {
    load().catch(() => undefined);
});
const category = computed(() => categories.value.find((c) => c.id === props.categoryId));
const items = computed(() => productsByCategory(props.categoryId));
const cartCount = computed(() => cartStore.items.reduce((sum, i) => sum + i.quantity, 0));
const recommendations = computed(() => [
    ...pairings.value,
    ...upsells.value,
]);
function addRecommendation(rec) {
    const product = {
        id: rec.menuItem.id,
        categoryId: '',
        name: rec.menuItem.name,
        description: rec.menuItem.description ?? '',
        price: rec.menuItem.price,
        imageUrl: rec.menuItem.imageUrl ?? null,
        isAvailable: true,
    };
    cartStore.addRecommendedItem(product);
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
/** @type {__VLS_StyleScopedClasses['chips']} */ ;
/** @type {__VLS_StyleScopedClasses['add-btn']} */ ;
/** @type {__VLS_StyleScopedClasses['recs-strip']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.emit('back');
            // @ts-ignore
            [emit,];
        } },
    ...{ class: "circle-btn" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}`, color: __VLS_ctx.T.ink }) },
    'aria-label': "Înapoi",
});
/** @type {__VLS_StyleScopedClasses['circle-btn']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "title" },
});
/** @type {__VLS_StyleScopedClasses['title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "title-name" },
});
/** @type {__VLS_StyleScopedClasses['title-name']} */ ;
(__VLS_ctx.category?.name ?? 'Meniu');
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "title-meta" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['title-meta']} */ ;
(__VLS_ctx.items.length);
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ class: "circle-btn" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}`, color: __VLS_ctx.T.ink70 }) },
    'aria-label': "Caută",
});
/** @type {__VLS_StyleScopedClasses['circle-btn']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "chips" },
});
/** @type {__VLS_StyleScopedClasses['chips']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "chip chip-active" },
    ...{ style: ({ background: __VLS_ctx.T.ink, color: __VLS_ctx.T.paperHi }) },
});
/** @type {__VLS_StyleScopedClasses['chip']} */ ;
/** @type {__VLS_StyleScopedClasses['chip-active']} */ ;
if (__VLS_ctx.isLoading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "state" },
        ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
    });
    /** @type {__VLS_StyleScopedClasses['state']} */ ;
}
else if (__VLS_ctx.error) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "state" },
        ...{ style: ({ color: __VLS_ctx.T.accent }) },
    });
    /** @type {__VLS_StyleScopedClasses['state']} */ ;
    (__VLS_ctx.error);
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "dishes" },
    });
    /** @type {__VLS_StyleScopedClasses['dishes']} */ ;
    for (const [m] of __VLS_vFor((__VLS_ctx.items))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.article, __VLS_intrinsics.article)({
            key: (m.id),
            ...{ class: "dish" },
            ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
        });
        /** @type {__VLS_StyleScopedClasses['dish']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dish-row" },
        });
        /** @type {__VLS_StyleScopedClasses['dish-row']} */ ;
        const __VLS_7 = FoodTile;
        // @ts-ignore
        const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
            tone: (__VLS_ctx.toneFromKey(m.id)),
            image: (m.imageUrl),
            radius: (16),
            ...{ class: "dish-tile" },
        }));
        const __VLS_9 = __VLS_8({
            tone: (__VLS_ctx.toneFromKey(m.id)),
            image: (m.imageUrl),
            radius: (16),
            ...{ class: "dish-tile" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_8));
        /** @type {__VLS_StyleScopedClasses['dish-tile']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dish-body" },
        });
        /** @type {__VLS_StyleScopedClasses['dish-body']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dish-name" },
        });
        /** @type {__VLS_StyleScopedClasses['dish-name']} */ ;
        (m.name);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dish-desc" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
        });
        /** @type {__VLS_StyleScopedClasses['dish-desc']} */ ;
        (m.description);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "dish-foot" },
            ...{ style: ({ borderTop: `1px solid ${__VLS_ctx.T.ink06}` }) },
        });
        /** @type {__VLS_StyleScopedClasses['dish-foot']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "price" },
        });
        /** @type {__VLS_StyleScopedClasses['price']} */ ;
        (m.price);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "price-unit" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
        });
        /** @type {__VLS_StyleScopedClasses['price-unit']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.isLoading))
                        return;
                    if (!!(__VLS_ctx.error))
                        return;
                    __VLS_ctx.cartStore.addItem(m);
                    // @ts-ignore
                    [T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, category, items, items, isLoading, error, error, toneFromKey, cartStore,];
                } },
            ...{ class: "add-btn" },
            ...{ style: ({ background: __VLS_ctx.T.accent, color: '#FFF4EC' }) },
            disabled: (!m.isAvailable),
        });
        /** @type {__VLS_StyleScopedClasses['add-btn']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "plus" },
        });
        /** @type {__VLS_StyleScopedClasses['plus']} */ ;
        (m.isAvailable ? 'Adaugă' : 'Epuizat');
        // @ts-ignore
        [T,];
    }
}
if (__VLS_ctx.recommendations.length > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "recs" },
        ...{ style: ({ background: __VLS_ctx.T.paperHi, borderTop: `1px solid ${__VLS_ctx.T.ink12}` }) },
    });
    /** @type {__VLS_StyleScopedClasses['recs']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "recs-pill" },
        ...{ style: ({ background: __VLS_ctx.T.lime, color: __VLS_ctx.T.ink }) },
    });
    /** @type {__VLS_StyleScopedClasses['recs-pill']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "recs-strip" },
    });
    /** @type {__VLS_StyleScopedClasses['recs-strip']} */ ;
    for (const [r] of __VLS_vFor((__VLS_ctx.recommendations))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            key: (r.id),
            ...{ class: "rec-card" },
            ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
        });
        /** @type {__VLS_StyleScopedClasses['rec-card']} */ ;
        const __VLS_12 = FoodTile;
        // @ts-ignore
        const __VLS_13 = __VLS_asFunctionalComponent1(__VLS_12, new __VLS_12({
            tone: (__VLS_ctx.toneFromKey(r.menuItem.id)),
            image: (r.menuItem.imageUrl),
            radius: (14),
            ...{ class: "rec-tile" },
        }));
        const __VLS_14 = __VLS_13({
            tone: (__VLS_ctx.toneFromKey(r.menuItem.id)),
            image: (r.menuItem.imageUrl),
            radius: (14),
            ...{ class: "rec-tile" },
        }, ...__VLS_functionalComponentArgsRest(__VLS_13));
        /** @type {__VLS_StyleScopedClasses['rec-tile']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rec-name" },
        });
        /** @type {__VLS_StyleScopedClasses['rec-name']} */ ;
        (r.menuItem.name);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rec-sub" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
        });
        /** @type {__VLS_StyleScopedClasses['rec-sub']} */ ;
        (r.reason);
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "rec-row" },
        });
        /** @type {__VLS_StyleScopedClasses['rec-row']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "rec-price" },
        });
        /** @type {__VLS_StyleScopedClasses['rec-price']} */ ;
        (r.menuItem.price);
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "rec-price-unit" },
            ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
        });
        /** @type {__VLS_StyleScopedClasses['rec-price-unit']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!(__VLS_ctx.recommendations.length > 0))
                        return;
                    __VLS_ctx.addRecommendation(r);
                    // @ts-ignore
                    [T, T, T, T, T, T, T, T, toneFromKey, recommendations, recommendations, addRecommendation,];
                } },
            ...{ class: "rec-add" },
            ...{ style: ({ background: __VLS_ctx.T.accent, color: '#FFF4EC' }) },
            'aria-label': "Adaugă recomandarea",
        });
        /** @type {__VLS_StyleScopedClasses['rec-add']} */ ;
        // @ts-ignore
        [T,];
    }
}
if (__VLS_ctx.cartCount > 0) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cart-bar" },
        ...{ style: ({ background: __VLS_ctx.T.ink, color: __VLS_ctx.T.paperHi }) },
    });
    /** @type {__VLS_StyleScopedClasses['cart-bar']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cart-eyebrow" },
    });
    /** @type {__VLS_StyleScopedClasses['cart-eyebrow']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "cart-summary" },
    });
    /** @type {__VLS_StyleScopedClasses['cart-summary']} */ ;
    (__VLS_ctx.cartStore.total);
    (__VLS_ctx.cartCount);
    (__VLS_ctx.cartCount === 1 ? 'preparat' : 'preparate');
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                if (!(__VLS_ctx.cartCount > 0))
                    return;
                __VLS_ctx.emit('open-cart');
                // @ts-ignore
                [emit, T, T, cartStore, cartCount, cartCount, cartCount,];
            } },
        ...{ class: "cart-cta" },
        ...{ style: ({ background: __VLS_ctx.T.accent, color: '#FFF4EC' }) },
    });
    /** @type {__VLS_StyleScopedClasses['cart-cta']} */ ;
}
// @ts-ignore
[T,];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __typeProps: {},
});
export default {};
