/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { T, TD } from '@/design/tokens';
const props = withDefaults(defineProps(), { theme: 'light', flush: false, column: false });
const t = computed(() => (props.theme === 'dark' ? TD : T));
const noiseSvg = computed(() => {
    if (props.theme === 'dark') {
        return `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.9  0 0 0 0 0.88  0 0 0 0 0.85  0 0 0 0.06 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`;
    }
    return `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.08  0 0 0 0 0.07  0 0 0 0 0.06  0 0 0 0.05 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`;
});
const __VLS_defaults = { theme: 'light', flush: false, column: false };
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "phone-screen" },
    ...{ style: ({
            background: props.bg ?? __VLS_ctx.t.paper,
            color: __VLS_ctx.t.ink,
            fontFamily: __VLS_ctx.t.sans,
            paddingTop: __VLS_ctx.flush ? 0 : `${__VLS_ctx.t.statusClear}px`,
            display: __VLS_ctx.column ? 'flex' : 'block',
            flexDirection: __VLS_ctx.column ? 'column' : undefined,
        }) },
});
/** @type {__VLS_StyleScopedClasses['phone-screen']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "noise" },
    ...{ style: ({
            backgroundImage: __VLS_ctx.noiseSvg,
            opacity: __VLS_ctx.theme === 'dark' ? 0.2 : 0.28,
            mixBlendMode: __VLS_ctx.theme === 'dark' ? 'screen' : 'multiply',
        }) },
});
/** @type {__VLS_StyleScopedClasses['noise']} */ ;
var __VLS_0 = {};
// @ts-ignore
var __VLS_1 = __VLS_0;
// @ts-ignore
[t, t, t, t, flush, column, column, noiseSvg, theme, theme,];
const __VLS_base = (await import('vue')).defineComponent({
    __defaults: __VLS_defaults,
    __typeProps: {},
});
const __VLS_export = {};
export default {};
