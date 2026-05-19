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
}

export interface BulkCreateProductsRequest {
  items: BulkCreateProductItem[]
  newCategories: BulkCreateCategoryItem[]
}
