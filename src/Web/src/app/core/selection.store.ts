import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { QuotaStore } from './quota.store';
import { TreeStore } from './tree.store';

/** Stable bulk-error codes → Polish messages surfaced when a bulk action is refused (US-12). */
const BULK_ERROR_MESSAGES: Record<string, string> = {
  'document.bulk_empty': 'Nie zaznaczono żadnych dokumentów.',
  'document.bulk_too_large': 'Zaznaczono zbyt wiele dokumentów naraz.',
  'document.bulk_validation_failed':
    'Niektórych zaznaczonych pozycji nie można przetworzyć — oznaczono je na liście. Popraw zaznaczenie i spróbuj ponownie.',
};
const GENERIC_BULK_ERROR = 'Nie udało się wykonać operacji zbiorczej. Spróbuj ponownie.';

/** One `{ id, code }` entry from a 422 `failures[]` extension. */
interface BulkFailure {
  readonly id: string;
  readonly code: string;
}

/**
 * Cross-cutting selection state + the two bulk actions (US-12). Holds the set of ticked document ids and, after
 * an all-or-nothing failure, the ids the server flagged ({@link failedIds}) so the tree can mark exactly those
 * rows. {@link bulkMove} / {@link bulkDelete} post the de-duplicated id list; on success they clear the selection
 * and refresh the shared tree + quota (no reload); on a `422` they populate {@link failedIds} (selection kept, so
 * the user can fix it); on any other error they surface a code-mapped notice.
 */
@Injectable({ providedIn: 'root' })
export class SelectionStore {
  private readonly http = inject(HttpClient);
  private readonly tree = inject(TreeStore);
  private readonly quota = inject(QuotaStore);

  private readonly selected = signal<ReadonlySet<string>>(new Set());

  /** The ids flagged by the last all-or-nothing failure (drives the row marking, FR-009). */
  readonly failedIds = signal<ReadonlySet<string>>(new Set());

  /** The reason a bulk action was refused, for a design-system notice; null when clear. */
  readonly bulkError = signal<string | null>(null);

  /** How many documents are currently selected. */
  readonly count = computed(() => this.selected().size);

  /** True while any document is selected (drives the action bar). */
  readonly hasSelection = computed(() => this.selected().size > 0);

  /** The selected ids as an array (request payload / iteration). */
  readonly selectedIds = computed(() => [...this.selected()]);

  /** True when <paramref name="id"/> is currently ticked. */
  has(id: string): boolean {
    return this.selected().has(id);
  }

  /** True when <paramref name="id"/> was flagged by the last failure. */
  hasFailed(id: string): boolean {
    return this.failedIds().has(id);
  }

  /** Ticks / unticks one document; clearing its failed mark. */
  toggle(id: string): void {
    this.selected.update((current) => {
      const next = new Set(current);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }

      return next;
    });
    this.clearFailedMark(id);
  }

  /**
   * Selects the contiguous range of document rows between <paramref name="fromId"/> and <paramref name="toId"/>
   * within one folder's ordered ids (Shift-click, AC-1). Ids outside the pair are left untouched; the range is
   * added to the current selection.
   */
  selectRange(folderDocIds: readonly string[], fromId: string, toId: string): void {
    const from = folderDocIds.indexOf(fromId);
    const to = folderDocIds.indexOf(toId);
    if (from === -1 || to === -1) {
      this.toggle(toId);

      return;
    }

    const [lo, hi] = from <= to ? [from, to] : [to, from];
    this.selected.update((current) => {
      const next = new Set(current);
      for (let index = lo; index <= hi; index += 1) {
        next.add(folderDocIds[index]);
      }

      return next;
    });
  }

  /** Clears the whole selection, the failed marks, and any notice (Anuluj / after success). */
  clear(): void {
    this.selected.set(new Set());
    this.failedIds.set(new Set());
    this.bulkError.set(null);
  }

  /** Clears just the bulk-error notice. */
  clearBulkError(): void {
    this.bulkError.set(null);
  }

  /** Moves every selected document to <paramref name="targetFolderId"/> (null = root), all-or-nothing. */
  bulkMove(targetFolderId: string | null): void {
    this.post('/api/documents/bulk-move', { ids: this.selectedIds(), targetFolderId });
  }

  /** Deletes every selected document, all-or-nothing (records + chunks cascade; quota −N). */
  bulkDelete(): void {
    this.post('/api/documents/bulk-delete', { ids: this.selectedIds() });
  }

  private post(url: string, body: Record<string, unknown>): void {
    this.bulkError.set(null);
    this.failedIds.set(new Set());

    this.http.post(url, body).subscribe({
      next: () => {
        this.clear();
        this.tree.refresh();
        this.quota.refresh();
      },
      error: (error: HttpErrorResponse) => this.handleError(error),
    });
  }

  private handleError(error: HttpErrorResponse): void {
    const payload = error.error as { code?: string; failures?: BulkFailure[] } | null;
    if (payload?.failures) {
      this.failedIds.set(new Set(payload.failures.map((failure) => failure.id)));
    }
    this.bulkError.set((payload?.code && BULK_ERROR_MESSAGES[payload.code]) || GENERIC_BULK_ERROR);
  }

  private clearFailedMark(id: string): void {
    if (!this.failedIds().has(id)) {
      return;
    }
    this.failedIds.update((current) => {
      const next = new Set(current);
      next.delete(id);

      return next;
    });
  }
}
