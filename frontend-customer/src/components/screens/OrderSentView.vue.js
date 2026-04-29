/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed, ref, onMounted } from 'vue';
import { T } from '@/design/tokens';
import { useSessionStore } from '@/stores/useSessionStore';
import PhoneScreen from '@/components/ui/PhoneScreen.vue';
import BigButton from '@/components/ui/BigButton.vue';
const props = defineProps();
const emit = defineEmits();
const sessionStore = useSessionStore();
const submittedAt = ref(new Date());
const now = ref(new Date());
onMounted(() => {
    submittedAt.value = new Date();
    now.value = new Date();
    setInterval(() => {
        now.value = new Date();
    }, 30_000);
});
function fmt(d) {
    return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
}
const tableLabel = computed(() => {
    const id = sessionStore.tableId;
    if (!id)
        return 'Masa';
    const tail = id.slice(-2).replace(/^0+/, '') || id.slice(-2);
    return `Masa ${tail}`;
});
const steps = computed(() => {
    const submitted = submittedAt.value;
    const inPrep = new Date(submitted.getTime() + 2 * 60_000);
    const serve = new Date(submitted.getTime() + 22 * 60_000);
    const t = now.value.getTime();
    return [
        { t: fmt(submitted), label: 'Trimisă la bucătărie', done: true, active: false },
        {
            t: fmt(inPrep),
            label: 'În pregătire acum',
            done: t >= inPrep.getTime(),
            active: t >= inPrep.getTime() && t < serve.getTime(),
        },
        {
            t: '~' + fmt(serve),
            label: 'Servire la masă',
            done: t >= serve.getTime(),
            active: false,
        },
    ];
});
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
    ...{ class: "halo" },
    ...{ style: ({ background: `radial-gradient(circle, ${__VLS_ctx.T.lime} 0%, transparent 70%)` }) },
});
/** @type {__VLS_StyleScopedClasses['halo']} */ ;
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
    ...{ class: "confirm-pill" },
    ...{ style: ({ background: 'rgba(60,122,60,0.10)', color: '#3C7A3C' }) },
});
/** @type {__VLS_StyleScopedClasses['confirm-pill']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "timeline" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}` }) },
});
/** @type {__VLS_StyleScopedClasses['timeline']} */ ;
for (const [s, i] of __VLS_vFor((__VLS_ctx.steps))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "step" },
        ...{ style: ({ borderBottom: i < __VLS_ctx.steps.length - 1 ? `1px solid ${__VLS_ctx.T.ink06}` : 'none' }) },
    });
    /** @type {__VLS_StyleScopedClasses['step']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span)({
        ...{ class: "dot" },
        ...{ style: ({
                background: s.done ? (s.active ? __VLS_ctx.T.accent : __VLS_ctx.T.ink) : 'transparent',
                border: s.done ? 'none' : `2px solid ${__VLS_ctx.T.ink40}`,
                boxShadow: s.active ? `0 0 12px ${__VLS_ctx.T.accent}` : 'none',
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['dot']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "step-body" },
    });
    /** @type {__VLS_StyleScopedClasses['step-body']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "step-label" },
        ...{ style: ({
                color: s.done ? __VLS_ctx.T.ink : __VLS_ctx.T.ink40,
                fontWeight: s.active ? 700 : 500,
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['step-label']} */ ;
    (s.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "step-time" },
        ...{ style: ({ color: __VLS_ctx.T.ink40 }) },
    });
    /** @type {__VLS_StyleScopedClasses['step-time']} */ ;
    (s.t);
    if (s.active) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "now-pill" },
            ...{ style: ({
                    color: __VLS_ctx.T.accent,
                    background: 'rgba(255,74,28,0.1)',
                }) },
        });
        /** @type {__VLS_StyleScopedClasses['now-pill']} */ ;
    }
    // @ts-ignore
    [T, T, T, T, T, T, T, T, T, T, T, T, T, T, T, tableLabel, steps, steps,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "total" },
    ...{ style: ({ background: __VLS_ctx.T.ink, color: __VLS_ctx.T.paperHi }) },
});
/** @type {__VLS_StyleScopedClasses['total']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "total-label" },
});
/** @type {__VLS_StyleScopedClasses['total-label']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "total-value" },
});
/** @type {__VLS_StyleScopedClasses['total-value']} */ ;
(props.total);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "total-unit" },
});
/** @type {__VLS_StyleScopedClasses['total-unit']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "total-hint" },
});
/** @type {__VLS_StyleScopedClasses['total-hint']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "cta" },
});
/** @type {__VLS_StyleScopedClasses['cta']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.emit('call-waiter');
            // @ts-ignore
            [T, T, emit,];
        } },
    ...{ class: "secondary" },
    ...{ style: ({ background: __VLS_ctx.T.card, border: `1px solid ${__VLS_ctx.T.ink12}`, color: __VLS_ctx.T.ink }) },
});
/** @type {__VLS_StyleScopedClasses['secondary']} */ ;
const __VLS_7 = BigButton || BigButton;
// @ts-ignore
const __VLS_8 = __VLS_asFunctionalComponent1(__VLS_7, new __VLS_7({
    ...{ 'onClick': {} },
    fontSize: (16),
}));
const __VLS_9 = __VLS_8({
    ...{ 'onClick': {} },
    fontSize: (16),
}, ...__VLS_functionalComponentArgsRest(__VLS_8));
let __VLS_12;
const __VLS_13 = ({ click: {} },
    { onClick: (...[$event]) => {
            __VLS_ctx.emit('order-again');
            // @ts-ignore
            [T, T, T, emit,];
        } });
const { default: __VLS_14 } = __VLS_10.slots;
// @ts-ignore
[];
var __VLS_10;
var __VLS_11;
// @ts-ignore
[];
var __VLS_3;
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __typeProps: {},
});
export default {};
