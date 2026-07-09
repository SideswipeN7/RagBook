import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Current quota state returned by <c>GET /api/quota</c> (US-05). */
export interface QuotaState {
  readonly usedDocuments: number;
  readonly maxDocuments: number;
  readonly usedMb: number;
  readonly maxTotalMb: number;
  readonly maxFileSizeMb: number;
  readonly canUpload: boolean;
}

/**
 * Shared, signal-based store of the session's quota. It is the single source the quota-bar renders and
 * the hook upload (US-04) and delete (US-08) call via {@link refresh} so the counter updates without a
 * page reload (AC-4). Enforcement is entirely backend-managed; this store only reflects reported state.
 */
@Injectable({ providedIn: 'root' })
export class QuotaStore {
  private readonly http = inject(HttpClient);

  readonly state = signal<QuotaState | null>(null);

  /** Whether another upload could currently be admitted (true until state is known). */
  readonly canUpload = computed(() => this.state()?.canUpload ?? true);

  /** Whether the quota is full — drives the "delete files" hint and the disabled upload control. */
  readonly isFull = computed(() => {
    const quota = this.state();

    return quota ? !quota.canUpload : false;
  });

  /** Re-reads the quota from the backend. Call after any upload or deletion. */
  refresh(): void {
    this.http.get<QuotaState>('/api/quota').subscribe((state) => this.state.set(state));
  }
}
