import type { Product } from '@shared/types/models';
export interface CartItem extends Product {
    quantity: number;
}
export declare const useCartStore: any;
