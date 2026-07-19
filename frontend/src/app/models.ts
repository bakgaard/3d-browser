// DTOs matching API_CONTRACT.md exactly.

export interface Artist {
  id: number;
  name: string;
  releaseCount: number;
  objectCount: number;
}

export interface Release {
  id: number;
  artistId: number;
  artistName: string;
  name: string;
  code: string;
  year: number;
  month: number;
  objectCount: number;
}

export interface Category {
  id: number;
  name: string;
  objectCount: number;
}

/** Lightweight category reference embedded in objects / search results. */
export interface CategoryRef {
  id: number;
  name: string;
}

export interface SearchResultItem {
  id: number;
  name: string;
  artistId: number;
  artistName: string;
  releaseId: number;
  releaseName: string;
  /** null when the object has no renders (models only). */
  thumbnailRenderId: number | null;
  renderCount: number;
  categories: CategoryRef[];
}

export interface SearchResponse {
  total: number;
  page: number;
  pageSize: number;
  items: SearchResultItem[];
}

export interface Render {
  id: number;
  fileName: string;
  relPath: string;
}

export interface ObjectDetail {
  id: number;
  name: string;
  customName: string | null;
  autoName: string;
  primaryRenderId: number | null;
  artistId: number;
  artistName: string;
  releaseId: number;
  releaseName: string;
  folderRelPath: string;
  hostPath: string;
  modelFileCount: number;
  categories: CategoryRef[];
  renders: Render[];
}

export interface ScanStartResponse {
  running: boolean;
}

export interface ScanStatus {
  running: boolean;
  startedAt: string | null;
  finishedAt: string | null;
  phase: string | null;
  artistsScanned: number;
  releasesFound: number;
  objectsFound: number;
  rendersFound: number;
  objectsAdded: number;
  rendersAdded: number;
  warnings: string[];
  message: string | null;
}

/** Query params accepted by GET /api/search (all optional). */
export interface SearchParams {
  q?: string;
  artistId?: number;
  releaseId?: number;
  categoryId?: number;
  page?: number;
  pageSize?: number;
}

/** Body for POST /api/objects/{id}/categories. */
export type AddCategoryBody = { categoryId: number } | { name: string };

export interface CategoryTagCount {
  name: string;
  added: number;
}

export interface AutoTagResult {
  linksAdded: number;
  categoriesConsidered: number;
  objectsConsidered: number;
  perCategory: CategoryTagCount[];
}
