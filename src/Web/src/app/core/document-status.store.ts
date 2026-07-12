import { Injectable, inject } from '@angular/core';
import { TreeStore } from './tree.store';

/**
 * Subscribes to the server-sent document status stream (US-06). While connected, each `ready`/`failed`
 * status push refreshes the shared {@link TreeStore}, so a document's row flips from processing to
 * ready/failed without a page reload. Best-effort: if the stream is unavailable, the tree still reflects
 * the true state on the next read.
 */
@Injectable({ providedIn: 'root' })
export class DocumentStatusStore {
  private readonly tree = inject(TreeStore);

  private source: EventSource | null = null;

  /** Opens the SSE stream once; subsequent calls are no-ops. */
  connect(url = '/api/documents/status/stream'): void {
    if (this.source) {
      return;
    }

    this.source = new EventSource(url);
    this.source.addEventListener('message', () => this.onStatus());
  }

  /** Applies a status push by re-reading the tree (a row flips processing → ready/failed). */
  onStatus(): void {
    this.tree.refresh();
  }

  /** Closes the stream. */
  disconnect(): void {
    this.source?.close();
    this.source = null;
  }
}
