import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Status projection returned by the settings endpoints (US-02). Never carries the full key. */
export interface ApiKeyStatusResponse {
  readonly status: 'none' | 'active';
  readonly maskedKey: string | null;
}

/** Human-readable messages for the stable settings error codes. */
const ERROR_MESSAGES: Record<string, string> = {
  'settings.invalid_api_key': 'Nieprawidłowy klucz API. Sprawdź go i spróbuj ponownie.',
  'settings.validation_unavailable': 'Nie można teraz zweryfikować klucza. Spróbuj ponownie za chwilę.',
  'settings.too_many_attempts': 'Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.',
};

/**
 * Shared, signal-based store of the session's BYOK key (US-02). Saving validates upstream and returns
 * the status + mask directly (no full key ever reaches the client); deleting returns the session to the
 * blocked state. {@link chatLocked} drives the question field lock until an active key is configured.
 */
@Injectable({ providedIn: 'root' })
export class ApiKeyStore {
  private readonly http = inject(HttpClient);

  /** `'unknown'` until the first status read completes, then `'none'` or `'active'`. */
  readonly status = signal<'unknown' | 'none' | 'active'>('unknown');
  readonly maskedKey = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly saving = signal(false);

  /** Chat/generation is locked until the session has an active key (US-02 AC-3, FR-015). */
  readonly chatLocked = computed(() => this.status() !== 'active');

  /** Reads the current key status for the session. */
  refresh(): void {
    this.http.get<ApiKeyStatusResponse>('/api/settings/api-key').subscribe({
      next: (response) => this.apply(response),
      error: () => this.setNone(),
    });
  }

  /** Validates and stores a key; on success applies the returned status + mask. */
  save(apiKey: string): void {
    this.error.set(null);
    this.saving.set(true);

    this.http.post<ApiKeyStatusResponse>('/api/settings/api-key', { apiKey }).subscribe({
      next: (response) => {
        this.saving.set(false);
        this.apply(response);
      },
      error: (response: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(ERROR_MESSAGES[response.error?.code] ?? 'Nie udało się zapisać klucza.');
      },
    });
  }

  /** Removes the stored key; the session returns to the blocked state (idempotent). */
  delete(): void {
    this.http.delete('/api/settings/api-key').subscribe({
      next: () => this.setNone(),
      error: () => this.setNone(),
    });
  }

  private apply(response: ApiKeyStatusResponse): void {
    this.status.set(response.status);
    this.maskedKey.set(response.maskedKey);
  }

  private setNone(): void {
    this.status.set('none');
    this.maskedKey.set(null);
    this.error.set(null);
  }
}
