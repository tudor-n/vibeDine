declare function load(): Promise<void>;
declare function productsByCategory(categoryId: string): any;
export declare function useMenu(): {
    categories: any;
    products: any;
    isLoading: any;
    error: any;
    load: typeof load;
    productsByCategory: typeof productsByCategory;
    countByCategory: (categoryId: string) => any;
};
export {};
