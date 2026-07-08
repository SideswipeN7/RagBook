import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { QuotaStore } from '../../core/quota.store';

/**
 * Renders the session's file quota as two meters — "X / N plików" and "X / N MB" — reading the shared
 * {@link QuotaStore}. When the quota is full it surfaces the "delete files" hint (AC-2). Standalone,
 * OnPush, signals; styled with design tokens (no inline hex).
 */
@Component({
  selector: 'app-quota-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './quota-bar.html',
  styleUrl: './quota-bar.scss',
})
export class QuotaBar {
  private readonly store = inject(QuotaStore);

  readonly state = this.store.state;
  readonly isFull = this.store.isFull;

  readonly documentsPercent = computed(() => {
    const quota = this.state();

    return quota ? Math.min(100, (quota.usedDocuments / quota.maxDocuments) * 100) : 0;
  });

  readonly storagePercent = computed(() => {
    const quota = this.state();

    return quota ? Math.min(100, (quota.usedMb / quota.maxTotalMb) * 100) : 0;
  });
}
