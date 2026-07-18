import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Demo usage as returned by GET /api/demo/status (US-03). */
export interface DemoStatusDto {
  readonly asked: number;
  readonly max: number;
  readonly remaining: number;
  readonly available: boolean;
}

/**
 * Signal-based store of the current session's demo usage (US-03). Reads `GET /api/demo/status` to drive the
 * "X / N pytań demo" counter and the BYOK nudge, and decrements optimistically after a successful demo ask so the
 * counter updates without a round-trip. `available` reflects whether demo generation is configured server-side.
 */
@Injectable({ providedIn: 'root' })
export class DemoStore {
  private readonly http = inject(HttpClient);

  readonly asked = signal(0);
  readonly max = signal(0);
  readonly remaining = signal(0);
  readonly available = signal(false);

  /** True once demo is configured but the session has used its whole allowance (drives the BYOK nudge). */
  readonly isExhausted = computed(() => this.available() && this.remaining() <= 0);

  /** Reads the current demo usage from the backend. */
  refresh(): void {
    this.http.get<DemoStatusDto>('/api/demo/status').subscribe((status) => {
      this.asked.set(status.asked);
      this.max.set(status.max);
      this.remaining.set(status.remaining);
      this.available.set(status.available);
    });
  }

  /** Optimistically records one demo question (after a successful demo ask), then reconciles with the server. */
  noteAsked(): void {
    this.asked.update((value) => value + 1);
    this.remaining.update((value) => Math.max(0, value - 1));
    this.refresh();
  }
}
