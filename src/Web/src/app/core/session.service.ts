import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

/** Empty-session application state returned by <c>GET /api/session</c>. */
export interface SessionState {
  readonly isNew: boolean;
  readonly resourceCount: number;
}

/**
 * Loads the current session state. The session cookie is entirely backend-managed; this service holds
 * no isolation logic (US-01 §IX) — it just reflects what the API reports.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);

  readonly state = signal<SessionState | null>(null);

  load(): void {
    this.http.get<SessionState>('/api/session').subscribe((state) => this.state.set(state));
  }
}
