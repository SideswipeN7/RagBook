import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { QuotaStore } from './quota.store';
import { TreeStore } from './tree.store';

/**
 * Document actions initiated from the tree (US-08). {@link delete} issues `DELETE /api/documents/{id}`
 * and refreshes the shared tree + quota so the row disappears and the counter drops without a reload; a
 * 404 (already deleted) is treated as already-done and still refreshes.
 */
@Injectable({ providedIn: 'root' })
export class DocumentActionsStore {
  private readonly http = inject(HttpClient);
  private readonly tree = inject(TreeStore);
  private readonly quota = inject(QuotaStore);

  /** Deletes a document, then refreshes the tree and quota (idempotent — a 404 also refreshes). */
  delete(id: string): void {
    this.http.delete(`/api/documents/${id}`).subscribe({
      next: () => this.refresh(),
      error: () => this.refresh(),
    });
  }

  private refresh(): void {
    this.tree.refresh();
    this.quota.refresh();
  }
}
