/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { T, TD, BLOB_PATHS } from '@/design/tokens';
const props = withDefaults(defineProps(), {
    sublabel: '',
    pathIndex: 0,
    image: null,
    active: false,
    theme: 'light',
    size: 158,
});
const emit = defineEmits();
const t = computed(() => (props.theme === 'dark' ? TD : T));
const path = computed(() => BLOB_PATHS[props.pathIndex % BLOB_PATHS.length]);
const uid = computed(() => Math.random().toString(36).slice(2, 9));
const dropShadow = computed(() => {
    if (props.active) {
        return props.theme === 'dark'
            ? 'drop-shadow(0 16px 22px rgba(232,37,90,0.45)) drop-shadow(0 4px 8px rgba(0,0,0,0.5))'
            : 'drop-shadow(0 16px 22px rgba(180,40,10,0.35)) drop-shadow(0 4px 8px rgba(180,40,10,0.16))';
    }
    return props.theme === 'dark'
        ? 'drop-shadow(0 8px 14px rgba(0,0,0,0.55)) drop-shadow(0 2px 4px rgba(0,0,0,0.35))'
        : 'drop-shadow(0 8px 14px rgba(21,20,15,0.18)) drop-shadow(0 2px 4px rgba(21,20,15,0.08))';
});
const fallbackBg = computed(() => (props.theme === 'dark' ? '#3a2418' : '#e8d5b9'));
const __VLS_defaults = {
    sublabel: '',
    pathIndex: 0,
    image: null,
    active: false,
    theme: 'light',
    size: 158,
};
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
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (...[$event]) => {
            __VLS_ctx.emit('click');
            // @ts-ignore
            [emit,];
        } },
    ...{ class: "pebble" },
    'aria-pressed': (__VLS_ctx.active),
});
/** @type {__VLS_StyleScopedClasses['pebble']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "blob-wrap" },
    ...{ style: ({
            transform: __VLS_ctx.active ? 'scale(1.06) translateY(-4px)' : 'scale(1) translateY(0)',
            filter: __VLS_ctx.dropShadow,
        }) },
});
/** @type {__VLS_StyleScopedClasses['blob-wrap']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    viewBox: ('0 0 100 100'),
    width: (__VLS_ctx.size),
    height: (__VLS_ctx.size),
    ...{ style: {} },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.defs, __VLS_intrinsics.defs)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.clipPath, __VLS_intrinsics.clipPath)({
    id: (`clip-${__VLS_ctx.uid}`),
});
__VLS_asFunctionalElement1(__VLS_intrinsics.path)({
    d: (__VLS_ctx.path),
});
__VLS_asFunctionalElement1(__VLS_intrinsics.radialGradient, __VLS_intrinsics.radialGradient)({
    id: (`hi-${__VLS_ctx.uid}`),
    cx: "32%",
    cy: "25%",
    r: "52%",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.stop)({
    offset: "0%",
    'stop-color': "rgba(255,255,255,0.75)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.stop)({
    offset: "60%",
    'stop-color': "rgba(255,255,255,0.12)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.stop)({
    offset: "100%",
    'stop-color': "rgba(255,255,255,0)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.linearGradient, __VLS_intrinsics.linearGradient)({
    id: (`sh-${__VLS_ctx.uid}`),
    x1: "50%",
    y1: "0%",
    x2: "50%",
    y2: "100%",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.stop)({
    offset: "0%",
    'stop-color': "rgba(255,255,255,0.08)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.stop)({
    offset: "100%",
    'stop-color': "rgba(21,20,15,0.28)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "0",
    y: "0",
    width: "100",
    height: "100",
    fill: (__VLS_ctx.fallbackBg),
    'clip-path': (`url(#clip-${__VLS_ctx.uid})`),
});
if (__VLS_ctx.image) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.image)({
        href: (__VLS_ctx.image),
        x: "0",
        y: "0",
        width: "100",
        height: "100",
        preserveAspectRatio: "xMidYMid slice",
        'clip-path': (`url(#clip-${__VLS_ctx.uid})`),
    });
}
__VLS_asFunctionalElement1(__VLS_intrinsics.path)({
    d: (__VLS_ctx.path),
    fill: (`url(#sh-${__VLS_ctx.uid})`),
    opacity: (__VLS_ctx.active ? 0.3 : 0.5),
});
__VLS_asFunctionalElement1(__VLS_intrinsics.path)({
    d: (__VLS_ctx.path),
    fill: (`url(#hi-${__VLS_ctx.uid})`),
});
__VLS_asFunctionalElement1(__VLS_intrinsics.path)({
    d: (__VLS_ctx.path),
    fill: "none",
    stroke: (__VLS_ctx.active ? __VLS_ctx.t.accent : (__VLS_ctx.theme === 'dark' ? 'rgba(245,240,232,0.18)' : 'rgba(21,20,15,0.14)')),
    'stroke-width': (__VLS_ctx.active ? 2 : 0.8),
});
if (__VLS_ctx.active) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        d: (__VLS_ctx.path),
        fill: "none",
        stroke: (__VLS_ctx.theme === 'dark' ? 'rgba(245,240,232,0.55)' : 'rgba(255,244,236,0.55)'),
        'stroke-width': "3.5",
    });
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "caption" },
});
/** @type {__VLS_StyleScopedClasses['caption']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "label" },
    ...{ style: ({ color: __VLS_ctx.active ? __VLS_ctx.t.accent : __VLS_ctx.t.ink }) },
});
/** @type {__VLS_StyleScopedClasses['label']} */ ;
(__VLS_ctx.label);
if (__VLS_ctx.sublabel) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "sublabel" },
        ...{ style: ({ color: __VLS_ctx.t.ink40 }) },
    });
    /** @type {__VLS_StyleScopedClasses['sublabel']} */ ;
    (__VLS_ctx.sublabel);
}
// @ts-ignore
[active, active, active, active, active, active, active, dropShadow, size, size, uid, uid, uid, uid, uid, uid, uid, path, path, path, path, path, fallbackBg, image, image, t, t, t, t, theme, theme, label, sublabel, sublabel,];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __defaults: __VLS_defaults,
    __typeProps: {},
});
export default {};
