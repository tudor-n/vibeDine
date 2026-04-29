/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, onMounted } from 'vue';
import { T } from '@/design/tokens';
import { useMenu } from '@/composables/useMenu';
import { useSessionStore } from '@/stores/useSessionStore';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import BigButton from '@/components/ui/BigButton.vue';
import Pebble from '@/components/ui/Pebble.vue';
const emit = defineEmits();
const { categories, isLoading, error, load, productsByCategory } = useMenu();
const sessionStore = useSessionStore();
const active = ref(null);
onMounted(() => {
    load().catch(() => {
        /* surfaced via the `error` ref */
    });
});
const tableLabel = computed(() => {
    const id = sessionStore.tableId;
    if (!id)
        return 'Masa';
    const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2);
    return `Masa ${tail}`;
});
// Pick an Unsplash photo for each category by keyword. Falls back to a
// generic plate photo when no keyword matches.
const KEYWORD_IMAGES = [
    [/sup|ciorb|borș|bors/i, 'https://images.unsplash.com/photo-1547592180-85f173990554?w=500&q=80'],
    [/salat|salad/i, 'https://images.unsplash.com/photo-1512621776951-a57141f2eefd?w=500&q=80'],
    [/desert|dulce|prajitur|prăjitur|tort|cake/i,
        'https://images.unsplash.com/photo-1488477181946-6428a0291777?w=500&q=80'],
    [/vin|wine/i, 'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=500&q=80'],
    [/bere|beer/i, 'https://images.unsplash.com/photo-1535958636474-b021ee887b13?w=500&q=80'],
    [/băutur|bautur|cocktail|drink|suc|limonad/i,
        'https://images.unsplash.com/photo-1551024709-8f23befc6f87?w=500&q=80'],
    [/cafe|cafea|coffee|ceai|tea/i,
        'https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?w=500&q=80'],
    [/pizza/i, 'https://images.unsplash.com/photo-1513104890138-7c749659a591?w=500&q=80'],
    [/paste|pasta/i, 'https://images.unsplash.com/photo-1551183053-bf91a1d81141?w=500&q=80'],
    [/peste|pește|fish|somon|salmon|fruct.?de.?mare/i,
        'https://images.unsplash.com/photo-1467003909585-2f8a72700288?w=500&q=80'],
    [/aperitiv|gustare|starter/i, 'https://images.unsplash.com/photo-1551024601-bec78aea704b?w=500&q=80'],
    [/vegetar|vegan|legume/i, 'https://images.unsplash.com/photo-1540420773420-3366772f4999?w=500&q=80'],
    [/burger/i, 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=500&q=80'],
    [/principal|main|carne|meat|vita|vită|porc|pui|chicken/i,
        'https://images.unsplash.com/photo-1544025162-d76694265947?w=500&q=80'],
];
const FALLBACK_IMAGE = 'https://images.unsplash.com/photo-1414235077428-338989a2e8c0?w=500&q=80';
function imageFor(name) {
    for (const [pattern, url] of KEYWORD_IMAGES) {
        if (pattern.test(name))
            return url;
    }
    return FALLBACK_IMAGE;
}
const pebbles = computed(() => categories.value.map((cat, i) => ({
    id: cat.id,
    label: cat.name,
    count: productsByCategory(cat.id).length,
    pathIndex: i % 6,
    image: imageFor(cat.name),
})));
const activePebble = computed(() => pebbles.value.find((p) => p.id === active.value));
function toggle(id) {
    active.value = active.value === id ? null : id;
}
function confirm() {
    if (active.value)
        emit('open-category', active.value);
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
const __VLS_0 = PhoneScreen || PhoneScreen;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    column: true,
}));
const __VLS_2 = __VLS_1({
    column: true,
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
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
    ...{ class: "status-table" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['status-table']} */ ;
(__VLS_ctx.tableLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "heading" },
});
/** @type {__VLS_StyleScopedClasses['heading']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "title" },
});
/** @type {__VLS_StyleScopedClasses['title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ style: ({ color: __VLS_ctx.T.accent }) },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "lede" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['lede']} */ ;
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
        ...{ class: "grid-wrap" },
    });
    /** @type {__VLS_StyleScopedClasses['grid-wrap']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "grid" },
    });
    /** @type {__VLS_StyleScopedClasses['grid']} */ ;
    for (const [pb] of __VLS_vFor((__VLS_ctx.pebbles))) {
        const __VLS_7 = Pebble;
        // @ts-ignore
        const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
            ...{ 'onClick': {} },
            key: (pb.id),
            label: (pb.label),
            sublabel: (`${pb.count} preparate`),
            pathIndex: (pb.pathIndex),
            image: (pb.image),
            active: (pb.id === __VLS_ctx.active),
        }));
        const __VLS_9 = __VLS_8({
            ...{ 'onClick': {} },
            key: (pb.id),
            label: (pb.label),
            sublabel: (`${pb.count} preparate`),
            pathIndex: (pb.pathIndex),
            image: (pb.image),
            active: (pb.id === __VLS_ctx.active),
        }, ...__VLS_functionalComponentArgsRest(__VLS_8));
        let __VLS_12;
        const __VLS_13 = ({ click: {} },
            { onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.isLoading))
                        return;
                    if (!!(__VLS_ctx.error))
                        return;
                    __VLS_ctx.toggle(pb.id);
                    // @ts-ignore
                    [T, T, T, T, T, T, T, T, tableLabel, isLoading, error, error, pebbles, active, toggle,];
                } });
        var __VLS_10;
        var __VLS_11;
        // @ts-ignore
        [];
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "cta" },
    ...{ style: ({ transform: __VLS_ctx.active ? 'translateY(0)' : 'translateY(120%)' }) },
});
/** @type {__VLS_StyleScopedClasses['cta']} */ ;
const __VLS_14 = BigButton || BigButton;
// @ts-ignore
const __VLS_15 = __VLS_asFunctionalComponent1(__VLS_14, new __VLS_14({
    ...{ 'onClick': {} },
}));
const __VLS_16 = __VLS_15({
    ...{ 'onClick': {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_15));
let __VLS_19;
const __VLS_20 = ({ click: {} },
    { onClick: (__VLS_ctx.confirm) });
const { default: __VLS_21 } = __VLS_17.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({});
(__VLS_ctx.activePebble?.label ?? '');
// @ts-ignore
[active, confirm, activePebble,];
var __VLS_17;
var __VLS_18;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
});
export default {};
