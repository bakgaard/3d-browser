import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { switchMap, of, catchError, map } from 'rxjs';
import { ApiService } from '../../api.service';
import { ClipboardService } from '../../clipboard.service';
import { ToastService } from '../../toast.service';
import { Category, CategoryRef, ObjectDetail, Render } from '../../models';

@Component({
  selector: 'app-object-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './object-detail.page.html',
  styleUrl: './object-detail.page.css',
})
export class ObjectDetailPage {
  private readonly api = inject(ApiService);
  private readonly clipboard = inject(ClipboardService);
  private readonly toasts = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly object = signal<ObjectDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly categories = signal<Category[]>([]);
  readonly newCategory = signal('');
  readonly addingCategory = signal(false);

  // ---- Title editing ----
  readonly editingTitle = signal(false);
  readonly titleDraft = signal('');
  readonly savingTitle = signal(false);

  // ---- Primary render ----
  readonly settingPrimary = signal<number | null>(null);

  // ---- Lightbox ----
  readonly lightboxIndex = signal<number | null>(null);
  readonly renders = computed<Render[]>(() => this.object()?.renders ?? []);
  readonly lightboxRender = computed<Render | null>(() => {
    const i = this.lightboxIndex();
    if (i === null) {
      return null;
    }
    return this.renders()[i] ?? null;
  });

  constructor() {
    this.route.paramMap
      .pipe(
        map((p) => Number(p.get('id'))),
        switchMap((id) => {
          this.loading.set(true);
          this.error.set(null);
          this.object.set(null);
          if (!id || Number.isNaN(id)) {
            this.error.set('Invalid object id.');
            this.loading.set(false);
            return of(null);
          }
          return this.api.getObject(id).pipe(
            catchError((err: unknown) => {
              console.error('getObject failed', err);
              this.error.set('Could not load this object.');
              this.loading.set(false);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((obj) => {
        if (obj) {
          this.object.set(obj);
        }
        this.loading.set(false);
      });

    this.loadCategories();
  }

  private loadCategories(): void {
    this.api
      .getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (c) => this.categories.set(c), error: () => {} });
  }

  // ---- Image URLs ----
  thumb(renderId: number): string {
    return this.api.thumbUrl(renderId);
  }
  file(renderId: number): string {
    return this.api.fileUrl(renderId);
  }

  onImgError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.classList.add('img--failed');
  }

  // ---- Copy folder path ----
  async copyPath(): Promise<void> {
    const obj = this.object();
    if (!obj) {
      return;
    }
    const ok = await this.clipboard.copyToClipboard(obj.hostPath);
    if (ok) {
      this.toasts.success('Folder path copied to clipboard.');
    } else {
      this.toasts.error('Could not copy path.');
    }
  }

  // ---- Category editor ----
  onNewCategoryInput(value: string): void {
    this.newCategory.set(value);
  }

  addCategory(): void {
    const obj = this.object();
    const name = this.newCategory().trim();
    if (!obj || !name || this.addingCategory()) {
      return;
    }
    // Skip if the object already has this category (case-insensitive).
    const already = obj.categories.some(
      (c) => c.name.toLowerCase() === name.toLowerCase(),
    );
    if (already) {
      this.toasts.show(`Already tagged "${name}".`, 'info');
      this.newCategory.set('');
      return;
    }
    this.addingCategory.set(true);
    this.api
      .addObjectCategory(obj.id, { name })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (cats) => {
          this.applyCategories(cats);
          this.newCategory.set('');
          this.addingCategory.set(false);
          this.loadCategories(); // objectCounts / new category may have changed
        },
        error: () => {
          this.addingCategory.set(false);
          this.toasts.error('Could not add category.');
        },
      });
  }

  removeCategory(cat: CategoryRef): void {
    const obj = this.object();
    if (!obj) {
      return;
    }
    this.api
      .removeObjectCategory(obj.id, cat.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.applyCategories(
            obj.categories.filter((c) => c.id !== cat.id),
          );
        },
        error: () => this.toasts.error('Could not remove category.'),
      });
  }

  private applyCategories(cats: CategoryRef[]): void {
    const obj = this.object();
    if (obj) {
      this.object.set({ ...obj, categories: cats });
    }
  }

  // ---- Title editing ----
  startEditTitle(): void {
    const obj = this.object();
    if (!obj) {
      return;
    }
    this.titleDraft.set(obj.customName ?? '');
    this.editingTitle.set(true);
  }

  cancelEditTitle(): void {
    this.editingTitle.set(false);
  }

  onTitleInput(value: string): void {
    this.titleDraft.set(value);
  }

  saveTitle(): void {
    const obj = this.object();
    if (!obj || this.savingTitle()) {
      return;
    }
    const value = this.titleDraft().trim() || null;
    this.savingTitle.set(true);
    this.api
      .setCustomName(obj.id, value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.object.set(updated);
          this.savingTitle.set(false);
          this.editingTitle.set(false);
        },
        error: () => {
          this.savingTitle.set(false);
          this.toasts.error('Could not update the name.');
        },
      });
  }

  resetName(): void {
    const obj = this.object();
    if (!obj) {
      return;
    }
    this.api
      .setCustomName(obj.id, null)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.object.set(updated);
          this.editingTitle.set(false);
        },
        error: () => this.toasts.error('Could not reset the name.'),
      });
  }

  // ---- Primary render ----
  setPrimary(renderId: number, event?: Event): void {
    event?.stopPropagation();
    const obj = this.object();
    if (!obj || obj.primaryRenderId === renderId || this.settingPrimary() !== null) {
      return;
    }
    this.settingPrimary.set(renderId);
    this.api
      .setPrimaryRender(obj.id, renderId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.object.set(updated);
          this.settingPrimary.set(null);
          this.toasts.success('Primary image updated.');
        },
        error: () => {
          this.settingPrimary.set(null);
          this.toasts.error('Could not set the primary image.');
        },
      });
  }

  // ---- Lightbox ----
  openLightbox(index: number): void {
    this.lightboxIndex.set(index);
  }
  closeLightbox(): void {
    this.lightboxIndex.set(null);
  }
  nextRender(): void {
    const i = this.lightboxIndex();
    if (i === null) {
      return;
    }
    const len = this.renders().length;
    if (len > 0) {
      this.lightboxIndex.set((i + 1) % len);
    }
  }
  prevRender(): void {
    const i = this.lightboxIndex();
    if (i === null) {
      return;
    }
    const len = this.renders().length;
    if (len > 0) {
      this.lightboxIndex.set((i - 1 + len) % len);
    }
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (this.lightboxIndex() === null) {
      return;
    }
    switch (event.key) {
      case 'Escape':
        this.closeLightbox();
        event.preventDefault();
        break;
      case 'ArrowRight':
        this.nextRender();
        event.preventDefault();
        break;
      case 'ArrowLeft':
        this.prevRender();
        event.preventDefault();
        break;
      default:
        break;
    }
  }

  goBack(): void {
    // Prefer browser back to preserve prior search state; fall back to home.
    if (window.history.length > 1) {
      window.history.back();
    } else {
      this.router.navigate(['/']);
    }
  }
}
