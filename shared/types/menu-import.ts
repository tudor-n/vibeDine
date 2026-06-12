// Mirrors C# DTOs in backend/RestaurantApi/DTO/{Requests,Responses}/MenuScanRequests.cs / MenuScanResponses.cs
// and Python Pydantic models in backend/ai-scripts/models/menu_extraction.py.

export interface ExtractedMenuItem {
  name: string
  price: number
  sourcePage: number
  sourceIndex: number
  description?: string | null
  categoryHint?: string | null
  confidence?: number | null
  imageUrl?: string | null
}

export interface FailedPage {
  sourcePage: number
  reason: 'unreadable' | 'no_items_detected' | 'vision_api_error'
}

export interface MenuScanResponse {
  items: ExtractedMenuItem[]
  failedPages: FailedPage[]
  model: string
  schemaVersion: number
  hierarchy?: MenuHierarchy
}

export interface BulkCreateCategoryItem {
  tempId: string
  name: string
  sortOrder?: number | null
}

export interface BulkCreateProductItem {
  name: string
  price: number
  categoryId: string
  description?: string | null
  isAvailable?: boolean
  imageUrl?: string | null
}

export interface BulkCreateProductsRequest {
  items: BulkCreateProductItem[]
  newCategories: BulkCreateCategoryItem[]
}

export interface ItemRef {
  sourcePage: number
  sourceIndex: number
  confidence: number
}

export interface OrganizedSubcategory {
  name: string
  sortOrder: number
  itemRefs: ItemRef[]
}

export interface OrganizedCategory {
  name: string
  sortOrder: number
  subcategories: OrganizedSubcategory[]
  directItemRefs: ItemRef[]
}

export interface MenuHierarchy {
  source: 'ai' | 'hint-fallback'
  categories: OrganizedCategory[]
  uncategorizedItemRefs: ItemRef[]
  modelNotes?: string | null
}

// Request body for POST /admin/menu/bulk-create-hierarchy (typed C# endpoint — camelCase)
export interface BulkHierarchySubcategoryRequest {
  tempId: string
  name: string
  sortOrder?: number | null
}

export interface BulkHierarchyCategoryRequest {
  tempId: string
  name: string
  sortOrder?: number | null
  subcategories: BulkHierarchySubcategoryRequest[]
}

export interface BulkHierarchyProductRequest {
  name: string
  price: number
  description?: string | null
  categoryTempId: string
  subcategoryTempId?: string | null
  sortOrder: number
  isAvailable: boolean
  imageUrl?: string | null
}

export interface BulkCreateHierarchyRequest {
  categories: BulkHierarchyCategoryRequest[]
  products: BulkHierarchyProductRequest[]
}

// Response from POST /admin/menu/bulk-create-hierarchy
export interface TempIdMap {
  tempId: string
  id: string
}

export interface CreatedHierarchyProduct {
  id: string
  name: string
  categoryId: string
  subcategoryId: string | null
}

export interface BulkCreateHierarchyResponse {
  createdCategories: TempIdMap[]
  createdSubcategories: TempIdMap[]
  createdProducts: CreatedHierarchyProduct[]
}
