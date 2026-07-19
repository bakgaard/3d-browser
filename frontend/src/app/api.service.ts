import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AddCategoryBody,
  Artist,
  AutoTagResult,
  Category,
  CategoryRef,
  ObjectDetail,
  Release,
  ScanStartResponse,
  ScanStatus,
  SearchParams,
  SearchResponse,
} from './models';

/**
 * Typed client for the Patreon 3D Browser backend.
 * Base URL is relative ('') so requests hit '/api/...' — proxied to the
 * backend in dev (proxy.conf.json) and by nginx in prod.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // ---- Reference / lookups ----

  getArtists(): Observable<Artist[]> {
    return this.http.get<Artist[]>(`${this.base}/artists`);
  }

  getReleases(artistId?: number): Observable<Release[]> {
    let params = new HttpParams();
    if (artistId != null) {
      params = params.set('artistId', String(artistId));
    }
    return this.http.get<Release[]>(`${this.base}/releases`, { params });
  }

  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.base}/categories`);
  }

  createCategory(name: string): Observable<Category> {
    return this.http.post<Category>(`${this.base}/categories`, { name });
  }

  /** Tag every object whose title contains a category name (whole-word by default). */
  autoTag(wholeWord = true): Observable<AutoTagResult> {
    const params = new HttpParams().set('wholeWord', String(wholeWord));
    return this.http.post<AutoTagResult>(`${this.base}/categories/auto-tag`, {}, { params });
  }

  // ---- Search ----

  search(query: SearchParams): Observable<SearchResponse> {
    let params = new HttpParams();
    if (query.q) {
      params = params.set('q', query.q);
    }
    if (query.artistId != null) {
      params = params.set('artistId', String(query.artistId));
    }
    if (query.releaseId != null) {
      params = params.set('releaseId', String(query.releaseId));
    }
    if (query.categoryId != null) {
      params = params.set('categoryId', String(query.categoryId));
    }
    if (query.page != null) {
      params = params.set('page', String(query.page));
    }
    if (query.pageSize != null) {
      params = params.set('pageSize', String(query.pageSize));
    }
    return this.http.get<SearchResponse>(`${this.base}/search`, { params });
  }

  // ---- Object detail ----

  getObject(id: number): Observable<ObjectDetail> {
    return this.http.get<ObjectDetail>(`${this.base}/objects/${id}`);
  }

  addObjectCategory(
    objectId: number,
    body: AddCategoryBody,
  ): Observable<CategoryRef[]> {
    return this.http.post<CategoryRef[]>(
      `${this.base}/objects/${objectId}/categories`,
      body,
    );
  }

  removeObjectCategory(
    objectId: number,
    categoryId: number,
  ): Observable<void> {
    return this.http.delete<void>(
      `${this.base}/objects/${objectId}/categories/${categoryId}`,
    );
  }

  setCustomName(
    id: number,
    customName: string | null,
  ): Observable<ObjectDetail> {
    return this.http.put<ObjectDetail>(`${this.base}/objects/${id}/name`, {
      customName,
    });
  }

  setPrimaryRender(id: number, renderId: number): Observable<ObjectDetail> {
    return this.http.put<ObjectDetail>(
      `${this.base}/objects/${id}/primary-render`,
      { renderId },
    );
  }

  mergeObjects(
    targetId: number,
    sourceIds: number[],
  ): Observable<ObjectDetail> {
    return this.http.post<ObjectDetail>(
      `${this.base}/objects/${targetId}/merge`,
      { sourceIds },
    );
  }

  // ---- Scan ----

  scan(): Observable<ScanStartResponse> {
    return this.http.post<ScanStartResponse>(`${this.base}/scan`, {});
  }

  scanStatus(): Observable<ScanStatus> {
    return this.http.get<ScanStatus>(`${this.base}/scan/status`);
  }

  // ---- Image URL helpers ----

  thumbUrl(renderId: number): string {
    return `${this.base}/renders/${renderId}/thumbnail`;
  }

  fileUrl(renderId: number): string {
    return `${this.base}/renders/${renderId}/file`;
  }
}
