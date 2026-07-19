import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink],
  template: `
    <header class="topbar">
      <a routerLink="/" class="brand" aria-label="Home">
        <span class="brand-icon" aria-hidden="true">🎲</span>
        <span class="brand-text">Patreon <strong>3D</strong> Browser</span>
      </a>
    </header>

    <main class="content">
      <router-outlet />
    </main>

    <!-- Global toast host -->
    <div class="toast-host" role="status" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast toast--{{ t.kind }}">
          <span class="toast-msg">{{ t.message }}</span>
          @if (t.actionLabel && t.action) {
            <button type="button" class="toast-action" (click)="runAction(t.id, t.action!)">
              {{ t.actionLabel }}
            </button>
          }
          <button
            type="button"
            class="toast-close"
            aria-label="Dismiss notification"
            (click)="toasts.dismiss(t.id)"
          >
            ×
          </button>
        </div>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        min-height: 100vh;
      }
      .topbar {
        position: sticky;
        top: 0;
        z-index: 50;
        display: flex;
        align-items: center;
        gap: 1rem;
        height: 56px;
        padding: 0 1.25rem;
        background: rgba(14, 17, 22, 0.85);
        backdrop-filter: blur(10px);
        border-bottom: 1px solid var(--border);
      }
      .brand {
        display: inline-flex;
        align-items: center;
        gap: 0.55rem;
        color: var(--text);
        font-size: 1.05rem;
        letter-spacing: 0.2px;
      }
      .brand:hover {
        color: var(--text);
      }
      .brand-icon {
        font-size: 1.4rem;
      }
      .brand-text strong {
        color: var(--accent);
      }
      .content {
        display: block;
      }
      .toast-host {
        position: fixed;
        right: 1rem;
        bottom: 1rem;
        z-index: 200;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        max-width: min(420px, calc(100vw - 2rem));
      }
      .toast {
        display: flex;
        align-items: center;
        gap: 0.6rem;
        padding: 0.7rem 0.8rem;
        border-radius: var(--radius-sm);
        background: var(--bg-elevated-2);
        border: 1px solid var(--border-strong);
        box-shadow: var(--shadow);
        animation: slideIn 0.18s ease;
      }
      .toast--error {
        border-color: var(--danger);
      }
      .toast--success {
        border-color: var(--ok);
      }
      .toast-msg {
        flex: 1;
        font-size: 0.9rem;
      }
      .toast-action {
        padding: 0.3rem 0.55rem;
        font-size: 0.82rem;
        white-space: nowrap;
      }
      .toast-close {
        border: none;
        background: transparent;
        color: var(--text-muted);
        font-size: 1.1rem;
        line-height: 1;
        padding: 0 0.2rem;
      }
      .toast-close:hover {
        background: transparent;
        color: var(--text);
        border: none;
      }
      @keyframes slideIn {
        from {
          opacity: 0;
          transform: translateY(8px);
        }
        to {
          opacity: 1;
          transform: translateY(0);
        }
      }
    `,
  ],
})
export class App {
  readonly toasts = inject(ToastService);

  runAction(id: number, action: () => void): void {
    action();
    this.toasts.dismiss(id);
  }
}
