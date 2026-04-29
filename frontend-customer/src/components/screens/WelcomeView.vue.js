/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { useSessionStore } from '@/stores/useSessionStore';
import { T } from '@/design/tokens';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import BigButton from '@/components/ui/BigButton.vue';
import FoodTile from '@/components/ui/FoodTile.vue';
const emit = defineEmits();
const sessionStore = useSessionStore();
const tableLabel = computed(() => {
    const id = sessionStore.tableId;
    if (!id)
        return 'Masa';
    const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2);
    return `Masa ${tail}`;
});
const clockLabel = computed(() => {
    const d = new Date();
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
});
const orderShortId = computed(() => {
    const id = sessionStore.orderId;
    if (!id)
        return '#----';
    return `#${id.slice(-4).toUpperCase()}`;
});
const infoCells = computed(() => [
    ['Masă', tableLabel.value.replace('Masa ', '') + ' · Terasă'],
    ['Oaspeți', '2 persoane'],
    ['Chef', 'Andrei C.'],
    ['Comanda', orderShortId.value],
]);
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
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({}));
const __VLS_2 = __VLS_1({}, ...__VLS_functionalComponentArgsRest(__VLS_1));
var __VLS_5 = {};
const { default: __VLS_6 } = __VLS_3.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "halo halo-accent" },
    ...{ style: ({ background: `radial-gradient(circle, ${__VLS_ctx.T.accent} 0%, transparent 70%)` }) },
});
/** @type {__VLS_StyleScopedClasses['halo']} */ ;
/** @type {__VLS_StyleScopedClasses['halo-accent']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "halo halo-lime" },
    ...{ style: ({ background: `radial-gradient(circle, ${__VLS_ctx.T.lime} 0%, transparent 70%)` }) },
});
/** @type {__VLS_StyleScopedClasses['halo']} */ ;
/** @type {__VLS_StyleScopedClasses['halo-lime']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "status-row" },
});
/** @type {__VLS_StyleScopedClasses['status-row']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "status-table" },
});
/** @type {__VLS_StyleScopedClasses['status-table']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span)({
    ...{ class: "dot" },
    ...{ style: ({ background: __VLS_ctx.T.accent, boxShadow: `0 0 8px ${__VLS_ctx.T.accent}` }) },
});
/** @type {__VLS_StyleScopedClasses['dot']} */ ;
(__VLS_ctx.tableLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "clock" },
});
/** @type {__VLS_StyleScopedClasses['clock']} */ ;
(__VLS_ctx.clockLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "hero" },
});
/** @type {__VLS_StyleScopedClasses['hero']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "eyebrow" },
    ...{ style: ({ color: __VLS_ctx.T.accent }) },
});
/** @type {__VLS_StyleScopedClasses['eyebrow']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "title" },
});
/** @type {__VLS_StyleScopedClasses['title']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.br)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ style: ({ color: __VLS_ctx.T.accent }) },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "lede" },
    ...{ style: ({ color: __VLS_ctx.T.ink70 }) },
});
/** @type {__VLS_StyleScopedClasses['lede']} */ ;
(__VLS_ctx.tableLabel.toLowerCase());
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "info-grid" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
});
/** @type {__VLS_StyleScopedClasses['info-grid']} */ ;
for (const [[k, v]] of __VLS_vFor((__VLS_ctx.infoCells))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (k),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-label" },
        ...{ style: ({ color: __VLS_ctx.T.ink40 }) },
    });
    /** @type {__VLS_StyleScopedClasses['info-label']} */ ;
    (k);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "info-value" },
        ...{ style: ({ color: __VLS_ctx.T.ink }) },
    });
    /** @type {__VLS_StyleScopedClasses['info-value']} */ ;
    (v);
    // @ts-ignore
    [T, T, T, T, T, T, T, T, T, T, T, tableLabel, tableLabel, clockLabel, infoCells,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "chefs-pick" },
    ...{ style: ({ background: __VLS_ctx.T.ink, color: __VLS_ctx.T.paperHi }) },
});
/** @type {__VLS_StyleScopedClasses['chefs-pick']} */ ;
const __VLS_7 = FoodTile;
// @ts-ignore
const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
    tone: "terra",
    label: "AZI",
    radius: (14),
    ...{ class: "chefs-tile" },
}));
const __VLS_9 = __VLS_8({
    tone: "terra",
    label: "AZI",
    radius: (14),
    ...{ class: "chefs-tile" },
}, ...__VLS_functionalComponentArgsRest(__VLS_8));
/** @type {__VLS_StyleScopedClasses['chefs-tile']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ style: {} },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pick-eyebrow" },
    ...{ style: ({ color: __VLS_ctx.T.lime }) },
});
/** @type {__VLS_StyleScopedClasses['pick-eyebrow']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pick-name" },
});
/** @type {__VLS_StyleScopedClasses['pick-name']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "pick-meta" },
});
/** @type {__VLS_StyleScopedClasses['pick-meta']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "cta" },
});
/** @type {__VLS_StyleScopedClasses['cta']} */ ;
const __VLS_12 = BigButton || BigButton;
// @ts-ignore
const __VLS_13 = __VLS_asFunctionalComponent1(__VLS_12, new __VLS_12({
    ...{ 'onClick': {} },
}));
const __VLS_14 = __VLS_13({
    ...{ 'onClick': {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_13));
let __VLS_17;
const __VLS_18 = ({ click: {} },
    { onClick: (...[$event]) => {
            __VLS_ctx.emit('open-menu');
            // @ts-ignore
            [T, T, T, emit,];
        } });
const { default: __VLS_19 } = __VLS_15.slots;
// @ts-ignore
[];
var __VLS_15;
var __VLS_16;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
});
export default {};
