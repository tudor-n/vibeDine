/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="C:/Users/Tudor/AppData/Local/npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, onMounted } from 'vue';
import { apiClient } from '@/services/api-client';
import { useCartStore } from '@/stores/useCartStore';
const cartStore = useCartStore();
const categories = ref([]);
const products = ref([]);
const isLoading = ref(true);
const error = ref(null);
const selectedCategoryId = ref(null);
const getProductsByCategory = (categoryId) => {
    return products.value.filter(p => p.categoryId === categoryId);
};
const filteredCategories = computed(() => {
    if (!selectedCategoryId.value) {
        return categories.value;
    }
    return categories.value.filter(c => c.id === selectedCategoryId.value);
});
onMounted(async () => {
    try {
        isLoading.value = true;
        const response = await apiClient.getMenu();
        categories.value = response.categories.map((c) => ({
            id: c.id,
            name: c.name,
            sortOrder: c.sortOrder || c.sort_order
        })).sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0));
        const allProducts = [];
        response.categories.forEach((cat) => {
            if (cat.products && Array.isArray(cat.products)) {
                cat.products.forEach((p) => {
                    allProducts.push({
                        id: p.id,
                        categoryId: cat.id,
                        name: p.name,
                        description: p.description,
                        price: p.price,
                        imageUrl: p.imageUrl || p.image_url || '',
                        isAvailable: p.isAvailable !== undefined ? p.isAvailable : (p.is_available !== undefined ? p.is_available : true)
                    });
                });
            }
        });
        products.value = allProducts;
    }
    catch (err) {
        if (err instanceof Error) {
            error.value = err.message;
        }
        else {
            error.value = 'Eroare necunoscută la încărcarea meniului.';
        }
    }
    finally {
        isLoading.value = false;
    }
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
/** @type {__VLS_StyleScopedClasses['hide-scrollbar']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "menu-list" },
});
/** @type {__VLS_StyleScopedClasses['menu-list']} */ ;
if (__VLS_ctx.isLoading) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "p-4 text-center text-gray-500" },
    });
    /** @type {__VLS_StyleScopedClasses['p-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-gray-500']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "animate-pulse" },
    });
    /** @type {__VLS_StyleScopedClasses['animate-pulse']} */ ;
}
else if (__VLS_ctx.error) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "p-4 text-center text-red-500" },
    });
    /** @type {__VLS_StyleScopedClasses['p-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-red-500']} */ ;
    (__VLS_ctx.error);
}
else {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
    if (__VLS_ctx.selectedCategoryId === null) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "animate-fade-in px-4" },
        });
        /** @type {__VLS_StyleScopedClasses['animate-fade-in']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
            ...{ class: "text-2xl font-black text-gray-800 mb-6 text-center" },
        });
        /** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
        /** @type {__VLS_StyleScopedClasses['font-black']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-gray-800']} */ ;
        /** @type {__VLS_StyleScopedClasses['mb-6']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "grid grid-cols-2 gap-4" },
        });
        /** @type {__VLS_StyleScopedClasses['grid']} */ ;
        /** @type {__VLS_StyleScopedClasses['grid-cols-2']} */ ;
        /** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
        for (const [category] of __VLS_vFor((__VLS_ctx.categories))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.isLoading))
                            return;
                        if (!!(__VLS_ctx.error))
                            return;
                        if (!(__VLS_ctx.selectedCategoryId === null))
                            return;
                        __VLS_ctx.selectedCategoryId = category.id;
                        // @ts-ignore
                        [isLoading, error, error, selectedCategoryId, selectedCategoryId, categories,];
                    } },
                key: (category.id),
                ...{ class: "relative overflow-hidden bg-white rounded-2xl p-6 shadow-sm border border-gray-100 flex flex-col items-center justify-center cursor-pointer hover:shadow-md active:scale-95 transition-all duration-200 group h-32" },
            });
            /** @type {__VLS_StyleScopedClasses['relative']} */ ;
            /** @type {__VLS_StyleScopedClasses['overflow-hidden']} */ ;
            /** @type {__VLS_StyleScopedClasses['bg-white']} */ ;
            /** @type {__VLS_StyleScopedClasses['rounded-2xl']} */ ;
            /** @type {__VLS_StyleScopedClasses['p-6']} */ ;
            /** @type {__VLS_StyleScopedClasses['shadow-sm']} */ ;
            /** @type {__VLS_StyleScopedClasses['border']} */ ;
            /** @type {__VLS_StyleScopedClasses['border-gray-100']} */ ;
            /** @type {__VLS_StyleScopedClasses['flex']} */ ;
            /** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
            /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
            /** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
            /** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
            /** @type {__VLS_StyleScopedClasses['hover:shadow-md']} */ ;
            /** @type {__VLS_StyleScopedClasses['active:scale-95']} */ ;
            /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
            /** @type {__VLS_StyleScopedClasses['duration-200']} */ ;
            /** @type {__VLS_StyleScopedClasses['group']} */ ;
            /** @type {__VLS_StyleScopedClasses['h-32']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "absolute -right-4 -top-4 w-16 h-16 bg-brand opacity-10 rounded-full group-hover:scale-150 transition-transform duration-500" },
            });
            /** @type {__VLS_StyleScopedClasses['absolute']} */ ;
            /** @type {__VLS_StyleScopedClasses['-right-4']} */ ;
            /** @type {__VLS_StyleScopedClasses['-top-4']} */ ;
            /** @type {__VLS_StyleScopedClasses['w-16']} */ ;
            /** @type {__VLS_StyleScopedClasses['h-16']} */ ;
            /** @type {__VLS_StyleScopedClasses['bg-brand']} */ ;
            /** @type {__VLS_StyleScopedClasses['opacity-10']} */ ;
            /** @type {__VLS_StyleScopedClasses['rounded-full']} */ ;
            /** @type {__VLS_StyleScopedClasses['group-hover:scale-150']} */ ;
            /** @type {__VLS_StyleScopedClasses['transition-transform']} */ ;
            /** @type {__VLS_StyleScopedClasses['duration-500']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "text-lg font-bold text-gray-800 text-center z-10" },
            });
            /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
            /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
            /** @type {__VLS_StyleScopedClasses['text-gray-800']} */ ;
            /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
            /** @type {__VLS_StyleScopedClasses['z-10']} */ ;
            (category.name);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "text-xs text-brand font-semibold mt-2 opacity-0 group-hover:opacity-100 transition-opacity z-10" },
            });
            /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
            /** @type {__VLS_StyleScopedClasses['text-brand']} */ ;
            /** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
            /** @type {__VLS_StyleScopedClasses['mt-2']} */ ;
            /** @type {__VLS_StyleScopedClasses['opacity-0']} */ ;
            /** @type {__VLS_StyleScopedClasses['group-hover:opacity-100']} */ ;
            /** @type {__VLS_StyleScopedClasses['transition-opacity']} */ ;
            /** @type {__VLS_StyleScopedClasses['z-10']} */ ;
            // @ts-ignore
            [];
        }
    }
    else {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "animate-fade-in" },
        });
        /** @type {__VLS_StyleScopedClasses['animate-fade-in']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "flex overflow-x-auto gap-3 px-4 mb-6 pb-2 snap-x hide-scrollbar items-center" },
        });
        /** @type {__VLS_StyleScopedClasses['flex']} */ ;
        /** @type {__VLS_StyleScopedClasses['overflow-x-auto']} */ ;
        /** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
        /** @type {__VLS_StyleScopedClasses['mb-6']} */ ;
        /** @type {__VLS_StyleScopedClasses['pb-2']} */ ;
        /** @type {__VLS_StyleScopedClasses['snap-x']} */ ;
        /** @type {__VLS_StyleScopedClasses['hide-scrollbar']} */ ;
        /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
            ...{ onClick: (...[$event]) => {
                    if (!!(__VLS_ctx.isLoading))
                        return;
                    if (!!(__VLS_ctx.error))
                        return;
                    if (!!(__VLS_ctx.selectedCategoryId === null))
                        return;
                    __VLS_ctx.selectedCategoryId = null;
                    // @ts-ignore
                    [selectedCategoryId,];
                } },
            ...{ class: "whitespace-nowrap flex items-center gap-1 px-5 py-2 rounded-full font-bold transition-all shadow-sm snap-start bg-white border border-gray-200 text-gray-600 hover:bg-gray-50" },
        });
        /** @type {__VLS_StyleScopedClasses['whitespace-nowrap']} */ ;
        /** @type {__VLS_StyleScopedClasses['flex']} */ ;
        /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
        /** @type {__VLS_StyleScopedClasses['gap-1']} */ ;
        /** @type {__VLS_StyleScopedClasses['px-5']} */ ;
        /** @type {__VLS_StyleScopedClasses['py-2']} */ ;
        /** @type {__VLS_StyleScopedClasses['rounded-full']} */ ;
        /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
        /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
        /** @type {__VLS_StyleScopedClasses['shadow-sm']} */ ;
        /** @type {__VLS_StyleScopedClasses['snap-start']} */ ;
        /** @type {__VLS_StyleScopedClasses['bg-white']} */ ;
        /** @type {__VLS_StyleScopedClasses['border']} */ ;
        /** @type {__VLS_StyleScopedClasses['border-gray-200']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-gray-600']} */ ;
        /** @type {__VLS_StyleScopedClasses['hover:bg-gray-50']} */ ;
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "text-lg leading-none" },
        });
        /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
        /** @type {__VLS_StyleScopedClasses['leading-none']} */ ;
        for (const [category] of __VLS_vFor((__VLS_ctx.categories))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
                ...{ onClick: (...[$event]) => {
                        if (!!(__VLS_ctx.isLoading))
                            return;
                        if (!!(__VLS_ctx.error))
                            return;
                        if (!!(__VLS_ctx.selectedCategoryId === null))
                            return;
                        __VLS_ctx.selectedCategoryId = category.id;
                        // @ts-ignore
                        [selectedCategoryId, categories,];
                    } },
                key: (category.id),
                ...{ class: ([
                        'whitespace-nowrap px-5 py-2 rounded-full font-bold transition-all shadow-sm snap-start',
                        __VLS_ctx.selectedCategoryId === category.id
                            ? 'bg-brand text-white scale-105'
                            : 'bg-white border border-gray-200 text-gray-600 hover:bg-gray-50'
                    ]) },
            });
            /** @type {__VLS_StyleScopedClasses['whitespace-nowrap']} */ ;
            /** @type {__VLS_StyleScopedClasses['px-5']} */ ;
            /** @type {__VLS_StyleScopedClasses['py-2']} */ ;
            /** @type {__VLS_StyleScopedClasses['rounded-full']} */ ;
            /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
            /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
            /** @type {__VLS_StyleScopedClasses['shadow-sm']} */ ;
            /** @type {__VLS_StyleScopedClasses['snap-start']} */ ;
            (category.name);
            // @ts-ignore
            [selectedCategoryId,];
        }
        for (const [category] of __VLS_vFor((__VLS_ctx.filteredCategories))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.section, __VLS_intrinsics.section)({
                key: (category.id),
                ...{ class: "mb-8 transition-all" },
            });
            /** @type {__VLS_StyleScopedClasses['mb-8']} */ ;
            /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
                ...{ class: "text-2xl font-bold mb-4 px-4" },
            });
            /** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
            /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
            /** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
            /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
            (category.name);
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                ...{ class: "grid grid-cols-1 md:grid-cols-2 gap-4 px-4" },
            });
            /** @type {__VLS_StyleScopedClasses['grid']} */ ;
            /** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
            /** @type {__VLS_StyleScopedClasses['md:grid-cols-2']} */ ;
            /** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
            /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
            for (const [product] of __VLS_vFor((__VLS_ctx.getProductsByCategory(category.id)))) {
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    key: (product.id),
                    ...{ class: "bg-white border border-gray-100 rounded-xl p-4 flex justify-between items-center shadow-sm hover:shadow-md transition-shadow" },
                });
                /** @type {__VLS_StyleScopedClasses['bg-white']} */ ;
                /** @type {__VLS_StyleScopedClasses['border']} */ ;
                /** @type {__VLS_StyleScopedClasses['border-gray-100']} */ ;
                /** @type {__VLS_StyleScopedClasses['rounded-xl']} */ ;
                /** @type {__VLS_StyleScopedClasses['p-4']} */ ;
                /** @type {__VLS_StyleScopedClasses['flex']} */ ;
                /** @type {__VLS_StyleScopedClasses['justify-between']} */ ;
                /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
                /** @type {__VLS_StyleScopedClasses['shadow-sm']} */ ;
                /** @type {__VLS_StyleScopedClasses['hover:shadow-md']} */ ;
                /** @type {__VLS_StyleScopedClasses['transition-shadow']} */ ;
                __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                    ...{ class: "pr-4" },
                });
                /** @type {__VLS_StyleScopedClasses['pr-4']} */ ;
                __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
                    ...{ class: "font-bold text-lg text-gray-800" },
                });
                /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
                /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
                /** @type {__VLS_StyleScopedClasses['text-gray-800']} */ ;
                (product.name);
                __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
                    ...{ class: "text-gray-500 text-sm mb-2 line-clamp-2" },
                });
                /** @type {__VLS_StyleScopedClasses['text-gray-500']} */ ;
                /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
                /** @type {__VLS_StyleScopedClasses['mb-2']} */ ;
                /** @type {__VLS_StyleScopedClasses['line-clamp-2']} */ ;
                (product.description);
                __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                    ...{ class: "font-black text-brand" },
                });
                /** @type {__VLS_StyleScopedClasses['font-black']} */ ;
                /** @type {__VLS_StyleScopedClasses['text-brand']} */ ;
                (product.price);
                __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
                    ...{ onClick: (...[$event]) => {
                            if (!!(__VLS_ctx.isLoading))
                                return;
                            if (!!(__VLS_ctx.error))
                                return;
                            if (!!(__VLS_ctx.selectedCategoryId === null))
                                return;
                            __VLS_ctx.cartStore.addItem(product);
                            // @ts-ignore
                            [filteredCategories, getProductsByCategory, cartStore,];
                        } },
                    disabled: (!product.isAvailable),
                    ...{ class: "shrink-0 bg-brand hover:bg-brand-light text-white font-bold px-5 py-2 rounded-lg transition-transform active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed disabled:active:scale-100 shadow-sm" },
                });
                /** @type {__VLS_StyleScopedClasses['shrink-0']} */ ;
                /** @type {__VLS_StyleScopedClasses['bg-brand']} */ ;
                /** @type {__VLS_StyleScopedClasses['hover:bg-brand-light']} */ ;
                /** @type {__VLS_StyleScopedClasses['text-white']} */ ;
                /** @type {__VLS_StyleScopedClasses['font-bold']} */ ;
                /** @type {__VLS_StyleScopedClasses['px-5']} */ ;
                /** @type {__VLS_StyleScopedClasses['py-2']} */ ;
                /** @type {__VLS_StyleScopedClasses['rounded-lg']} */ ;
                /** @type {__VLS_StyleScopedClasses['transition-transform']} */ ;
                /** @type {__VLS_StyleScopedClasses['active:scale-95']} */ ;
                /** @type {__VLS_StyleScopedClasses['disabled:opacity-50']} */ ;
                /** @type {__VLS_StyleScopedClasses['disabled:cursor-not-allowed']} */ ;
                /** @type {__VLS_StyleScopedClasses['disabled:active:scale-100']} */ ;
                /** @type {__VLS_StyleScopedClasses['shadow-sm']} */ ;
                (product.isAvailable ? 'Adaugă' : 'Epuizat');
                // @ts-ignore
                [];
            }
            // @ts-ignore
            [];
        }
    }
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
