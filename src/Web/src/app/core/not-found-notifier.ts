import { Injectable, signal } from '@angular/core';

/**
 * Holds the most recent "resource does not exist" message surfaced by the 404 interceptor.
 * The frontend carries no isolation logic — it simply reflects the backend's 404 (US-01 §IX).
 */
@Injectable({ providedIn: 'root' })
export class NotFoundNotifier {
  readonly message = signal<string | null>(null);

  notify(message: string): void {
    this.message.set(message);
  }

  clear(): void {
    this.message.set(null);
  }
}
