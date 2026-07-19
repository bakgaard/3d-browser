import { Injectable, signal } from '@angular/core';

export type ToastKind = 'info' | 'success' | 'error';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
  /** Optional action button (e.g. "Copy path"). */
  actionLabel?: string;
  action?: () => void;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  show(
    message: string,
    kind: ToastKind = 'info',
    options?: { actionLabel?: string; action?: () => void; durationMs?: number },
  ): number {
    const id = this.nextId++;
    const toast: Toast = {
      id,
      kind,
      message,
      actionLabel: options?.actionLabel,
      action: options?.action,
    };
    this.toasts.update((list) => [...list, toast]);
    const duration = options?.durationMs ?? (kind === 'error' ? 8000 : 4000);
    if (duration > 0) {
      setTimeout(() => this.dismiss(id), duration);
    }
    return id;
  }

  success(message: string, durationMs?: number): number {
    return this.show(message, 'success', { durationMs });
  }

  error(
    message: string,
    options?: { actionLabel?: string; action?: () => void; durationMs?: number },
  ): number {
    return this.show(message, 'error', options);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
