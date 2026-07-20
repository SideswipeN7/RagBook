import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

/** Client bootstrap flags as returned by GET /api/config (US-22). */
export interface AppConfigDto {
  /** True when the server can generate an answer without a session key (application key or keyless CLI mode). */
  readonly keylessGeneration: boolean;
}

/**
 * Signal-based store of server bootstrap flags (US-22). Reads `GET /api/config` once on startup so the composer can
 * unlock without a BYOK key when the server offers keyless generation (a configured application key, or the local
 * `claude` CLI fallback). Defaults to `false` until the first response, so the key guard holds until proven keyless.
 */
@Injectable({ providedIn: 'root' })
export class AppConfigStore {
  private readonly http = inject(HttpClient);

  /** True when the backend can answer without a session key (drives composer unlock alongside demo scope). */
  readonly keylessGeneration = signal(false);

  /** Reads the bootstrap config from the backend. */
  refresh(): void {
    this.http.get<AppConfigDto>('/api/config').subscribe((config) => {
      this.keylessGeneration.set(config.keylessGeneration);
    });
  }
}
