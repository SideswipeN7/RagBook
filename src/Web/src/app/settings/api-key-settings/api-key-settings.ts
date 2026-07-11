import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ApiKeyStore } from '../../core/api-key.store';

/**
 * BYOK settings panel (US-02). Shows one of three states from {@link ApiKeyStore}: loading, no key (a
 * password input + Save), or active (the mask + a Delete with inline confirm — never a native dialog).
 * The full key is never rendered. Standalone, OnPush, signals.
 */
@Component({
  selector: 'app-api-key-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './api-key-settings.html',
  styleUrl: './api-key-settings.scss',
})
export class ApiKeySettings implements OnInit {
  private readonly store = inject(ApiKeyStore);

  readonly status = this.store.status;
  readonly maskedKey = this.store.maskedKey;
  readonly error = this.store.error;
  readonly saving = this.store.saving;

  readonly confirmingDelete = signal(false);

  ngOnInit(): void {
    this.store.refresh();
  }

  save(apiKey: string): void {
    const trimmed = apiKey.trim();
    if (trimmed.length > 0) {
      this.store.save(trimmed);
    }
  }

  askDelete(): void {
    this.confirmingDelete.set(true);
  }

  confirmDelete(): void {
    this.store.delete();
    this.confirmingDelete.set(false);
  }

  cancelDelete(): void {
    this.confirmingDelete.set(false);
  }
}
