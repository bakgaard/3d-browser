import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  debounceTime,
  distinctUntilChanged,
  switchMap,
  of,
  catchError,
  tap,
} from 'rxjs';
import { ApiService } from '../../api.service';
import { ToastService } from '../../toast.service';
import {
  Artist,
  Category,
  Release,
  ScanStatus,
  SearchParams,
  SearchResponse,
  SearchResultItem,
} from '../../models';

const PAGE_SIZE = 48;

@Component({
  selector: 'app-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search.page.html',
  styleUrl: './search.page.css',
})
export class SearchPage implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly pageSize = PAGE_SIZE;

  // ---- Lookups ----
  readonly artists = signal<Artist[]>([]);
  readonly releases = signal<Release[]>([]);
  readonly categories = signal<Category[]>([]);

  // ---- Filter state ----
  readonly q = signal('');
  readonly artistId = signal<number | null>(null);
  readonly releaseId = signal<number | null>(null);
  readonly categoryId = signal<number | null>(null);
  readonly page = signal(1);

  // ---- Results ----
  readonly result = signal<SearchResponse | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // ---- Derived ----
  readonly items = computed<SearchResultItem[]>(
    () => this.result()?.items ?? [],
  );
  readonly total = computed(() => this.result()?.total ?? 0);
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize)),
  );
  readonly rangeStart = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize + 1,
  );
  readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize, this.total()),
  );

  // ---- Selection / merge ----
  readonly selectedIds = signal<Set<number>>(new Set());
  readonly selectedCount = computed(() => this.selectedIds().size);
  /** Selected items in current-page order (for the merge modal). */
  readonly selectedItems = computed<SearchResultItem[]>(() => {
    const ids = this.selectedIds();
    return this.items().filter((it) => ids.has(it.id));
  });
  readonly mergeOpen = signal(false);
  readonly keptId = signal<number | null>(null);
  readonly merging = signal(false);

  // ---- Scan state ----
  readonly scanStatus = signal<ScanStatus | null>(null);
  readonly scanning = signal(false);
  readonly autoTagging = signal(false);
  private scanTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly params = computed<SearchParams>(() => ({
    q: this.q().trim() || undefined,
    artistId: this.artistId() ?? undefined,
    releaseId: this.releaseId() ?? undefined,
    categoryId: this.categoryId() ?? undefined,
    page: this.page(),
    pageSize: this.pageSize,
  }));

  constructor() {
    // Seed filter state from the URL so refresh, deep-links, and "back" from an
    // object all restore the same search.
    this.initFromUrl();
    this.loadLookups();

    // Mirror the filter state into the URL query params. `replaceUrl` keeps a single
    // search history entry (so typing doesn't spam history), while navigating to an
    // object detail pushes a new entry — going back returns here with filters intact.
    effect(() => {
      const queryParams = {
        q: this.q().trim() || null,
        artistId: this.artistId(),
        releaseId: this.releaseId(),
        categoryId: this.categoryId(),
        page: this.page() > 1 ? this.page() : null,
      };
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams,
        replaceUrl: true,
      });
    });

    // Reactive search pipeline: debounced, de-duplicated, latest-wins.
    toObservable(this.params)
      .pipe(
        debounceTime(300),
        distinctUntilChanged(
          (a, b) => JSON.stringify(a) === JSON.stringify(b),
        ),
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((p) =>
          this.api.search(p).pipe(
            catchError((err: unknown) => {
              console.error('search failed', err);
              this.error.set('Search failed. Is the backend running?');
              this.loading.set(false);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => {
        if (res) {
          this.result.set(res);
        }
        this.loading.set(false);
      });
  }

  ngOnDestroy(): void {
    this.stopScanPolling();
  }

  /** Reads the current URL query params into the filter signals (NaN-safe). */
  private initFromUrl(): void {
    const qp = this.route.snapshot.queryParamMap;
    const num = (key: string): number | null => {
      const raw = qp.get(key);
      if (raw === null) return null;
      const n = Number(raw);
      return Number.isFinite(n) ? n : null;
    };
    this.q.set(qp.get('q') ?? '');
    this.artistId.set(num('artistId'));
    this.releaseId.set(num('releaseId'));
    this.categoryId.set(num('categoryId'));
    this.page.set(Math.max(1, num('page') ?? 1));
  }

  private loadLookups(): void {
    this.api
      .getArtists()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => this.artists.set(a),
        error: () => this.toasts.error('Could not load artists.'),
      });
    // Scope releases to the (possibly URL-seeded) artist so the dropdown is correct.
    this.api
      .getReleases(this.artistId() ?? undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.releases.set(r), error: () => {} });
    this.reloadCategories();
  }

  private reloadCategories(): void {
    this.api
      .getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (c) => this.categories.set(c), error: () => {} });
  }

  // ---- Filter handlers ----

  onQueryInput(value: string): void {
    this.q.set(value);
    this.page.set(1);
  }

  onArtistChange(value: string): void {
    const id = value ? Number(value) : null;
    this.artistId.set(id);
    this.releaseId.set(null);
    this.page.set(1);
    // Reload releases scoped to the chosen artist (or all).
    this.api
      .getReleases(id ?? undefined)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.releases.set(r), error: () => {} });
  }

  onReleaseChange(value: string): void {
    this.releaseId.set(value ? Number(value) : null);
    this.page.set(1);
  }

  onCategoryChange(value: string): void {
    this.categoryId.set(value ? Number(value) : null);
    this.page.set(1);
  }

  clearFilters(): void {
    this.q.set('');
    this.artistId.set(null);
    this.releaseId.set(null);
    this.categoryId.set(null);
    this.page.set(1);
    this.api
      .getReleases()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.releases.set(r), error: () => {} });
  }

  readonly hasActiveFilters = computed(
    () =>
      !!this.q().trim() ||
      this.artistId() != null ||
      this.releaseId() != null ||
      this.categoryId() != null,
  );

  // ---- Pagination ----

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.scrollTop();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update((p) => p + 1);
      this.scrollTop();
    }
  }

  private scrollTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // ---- Card helpers ----

  thumb(renderId: number): string {
    return this.api.thumbUrl(renderId);
  }

  onThumbError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.style.display = 'none';
    const parent = img.parentElement;
    if (parent) {
      parent.classList.add('thumb--failed');
    }
  }

  openObject(id: number): void {
    this.router.navigate(['/objects', id]);
  }

  // ---- Selection / merge ----

  isSelected(id: number): boolean {
    return this.selectedIds().has(id);
  }

  toggleSelected(id: number, event?: Event): void {
    event?.stopPropagation();
    this.selectedIds.update((set) => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  openMerge(): void {
    if (this.selectedCount() < 2) {
      return;
    }
    // Default the kept object to the first selected (current-page order).
    this.keptId.set(this.selectedItems()[0]?.id ?? null);
    this.mergeOpen.set(true);
  }

  cancelMerge(): void {
    this.mergeOpen.set(false);
  }

  selectKept(id: number): void {
    this.keptId.set(id);
  }

  confirmMerge(): void {
    const keptId = this.keptId();
    const ids = Array.from(this.selectedIds());
    if (keptId === null || ids.length < 2 || this.merging()) {
      return;
    }
    const sourceIds = ids.filter((id) => id !== keptId);
    if (sourceIds.length === 0) {
      return;
    }
    this.merging.set(true);
    this.api
      .mergeObjects(keptId, sourceIds)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          const n = sourceIds.length + 1;
          this.merging.set(false);
          this.mergeOpen.set(false);
          this.clearSelection();
          this.refetch();
          this.toasts.success(`Combined ${n} objects.`);
        },
        error: () => {
          this.merging.set(false);
          this.toasts.error('Could not combine objects. Please try again.');
        },
      });
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.mergeOpen()) {
      this.cancelMerge();
    }
  }

  // ---- Scan ----

  startScan(): void {
    this.scanning.set(true);
    this.api
      .scan()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toasts.show('Library scan started.', 'info');
          this.pollScanStatus();
        },
        error: () => {
          this.scanning.set(false);
          this.toasts.error('Could not start scan. Is the backend running?');
        },
      });
  }

  private pollScanStatus(): void {
    this.api
      .scanStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.scanStatus.set(status);
          if (status.running) {
            this.scanTimer = setTimeout(() => this.pollScanStatus(), 1500);
          } else {
            this.onScanFinished();
          }
        },
        error: () => {
          this.stopScanPolling();
          this.scanning.set(false);
          this.toasts.error('Lost connection to scan status.');
        },
      });
  }

  private onScanFinished(): void {
    this.stopScanPolling();
    this.scanning.set(false);
    this.toasts.success('Scan complete.');
    // Refresh lookups and results — new content may have appeared.
    this.loadLookups();
    this.refetch();
  }

  // ---- Auto-tag ----

  autoTag(): void {
    if (this.autoTagging()) {
      return;
    }
    this.autoTagging.set(true);
    this.api
      .autoTag()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.autoTagging.set(false);
          if (res.categoriesConsidered === 0) {
            this.toasts.show('No categories yet — add some first.', 'info');
            return;
          }
          this.toasts.success(
            `Auto-tagged ${res.linksAdded} match(es) across ${res.categoriesConsidered} categories.`,
          );
          this.loadLookups();
          this.refetch();
        },
        error: () => {
          this.autoTagging.set(false);
          this.toasts.error('Could not auto-tag.');
        },
      });
  }

  /** One-off manual fetch using the current filter state. */
  private refetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .search(this.params())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Search failed. Is the backend running?');
          this.loading.set(false);
        },
      });
  }

  private stopScanPolling(): void {
    if (this.scanTimer !== null) {
      clearTimeout(this.scanTimer);
      this.scanTimer = null;
    }
  }
}
