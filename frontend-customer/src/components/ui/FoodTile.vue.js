/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { FOOD_PALETTES } from '@/design/tokens';
const props = withDefaults(defineProps(), { tone: 'terra', label: '', image: null, radius: 14 });
const palette = computed(() => FOOD_PALETTES[props.tone] ?? FOOD_PALETTES.terra);
const __VLS_defaults = { tone: 'terra', label: '', image: null, radius: 14 };
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
    ...{ class: "food-tile" },
    ...{ style: ({
            background: __VLS_ctx.palette.c,
            borderRadius: `${__VLS_ctx.radius}px`,
        }) },
});
/** @type {__VLS_StyleScopedClasses['food-tile']} */ ;
if (__VLS_ctx.image) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.img)({
        src: (__VLS_ctx.image),
        alt: (__VLS_ctx.label || ''),
        ...{ class: "photo" },
    });
    /** @type {__VLS_StyleScopedClasses['photo']} */ ;
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "gradient" },
        ...{ style: ({
                background: `radial-gradient(60% 50% at 30% 30%, ${__VLS_ctx.palette.a} 0%, transparent 60%),` +
                    ` radial-gradient(50% 40% at 75% 75%, ${__VLS_ctx.palette.b} 0%, transparent 60%)`,
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['gradient']} */ ;
}
if (__VLS_ctx.label && !__VLS_ctx.image) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "label" },
    });
    /** @type {__VLS_StyleScopedClasses['label']} */ ;
    (__VLS_ctx.label);
}
// @ts-ignore
[palette, palette, palette, radius, image, image, image, label, label, label,];
const __VLS_export = (await import('vue')).defineComponent({
    __defaults: __VLS_defaults,
    __typeProps: {},
});
export default {};
