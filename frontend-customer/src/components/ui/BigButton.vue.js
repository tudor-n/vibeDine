/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { T, TD } from '@/design/tokens';
const props = withDefaults(defineProps(), { theme: 'light', arrow: true, disabled: false, fontSize: 19 });
const emit = defineEmits();
const t = computed(() => (props.theme === 'dark' ? TD : T));
const bg = computed(() => props.bg ?? t.value.accent);
const color = computed(() => props.color ?? t.value.onAccent);
const arrowBg = computed(() => props.theme === 'dark' ? t.value.ink : t.value.onAccent);
const arrowColor = computed(() => bg.value);
function onClick(ev) {
    if (props.disabled)
        return;
    emit('click', ev);
}
const __VLS_defaults = { theme: 'light', arrow: true, disabled: false, fontSize: 19 };
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
/** @type {__VLS_StyleScopedClasses['big-btn']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
    ...{ onClick: (__VLS_ctx.onClick) },
    ...{ class: "big-btn" },
    disabled: (__VLS_ctx.disabled),
    ...{ style: ({
            background: __VLS_ctx.bg,
            color: __VLS_ctx.color,
            fontSize: `${__VLS_ctx.fontSize}px`,
            boxShadow: __VLS_ctx.disabled ? 'none' : `0 10px 24px -8px ${__VLS_ctx.bg}66`,
            opacity: __VLS_ctx.disabled ? 0.55 : 1,
            cursor: __VLS_ctx.disabled ? 'not-allowed' : 'pointer',
            fontFamily: __VLS_ctx.t.sans,
        }) },
});
/** @type {__VLS_StyleScopedClasses['big-btn']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "label" },
});
/** @type {__VLS_StyleScopedClasses['label']} */ ;
var __VLS_0 = {};
if (__VLS_ctx.arrow) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "arrow" },
        ...{ style: ({ background: __VLS_ctx.arrowBg, color: __VLS_ctx.arrowColor }) },
        'aria-hidden': "true",
    });
    /** @type {__VLS_StyleScopedClasses['arrow']} */ ;
}
// @ts-ignore
var __VLS_1 = __VLS_0;
// @ts-ignore
[onClick, disabled, disabled, disabled, disabled, bg, bg, color, fontSize, t, arrow, arrowBg, arrowColor,];
const __VLS_base = (await import('vue')).defineComponent({
    __typeEmits: {},
    __defaults: __VLS_defaults,
    __typeProps: {},
});
const __VLS_export = {};
export default {};
