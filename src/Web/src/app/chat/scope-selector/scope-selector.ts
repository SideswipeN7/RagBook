import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { ChatScopeSelection } from '../../core/chat.store';
import { TreeStore } from '../../core/tree.store';

/**
 * Scope selector for the chat (US-15). Options come from the shared {@link TreeStore}: "All documents", each
 * folder (its subtree — US-13), and each **ready** document (processing/failed are not selectable). Emits the
 * chosen {@link ChatScopeSelection}. Standalone, OnPush.
 */
@Component({
  selector: 'app-scope-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './scope-selector.html',
  styleUrl: './scope-selector.scss',
})
export class ScopeSelector {
  private readonly tree = inject(TreeStore);

  /** Emitted when the user picks a scope. */
  readonly scopeChange = output<ChatScopeSelection>();

  readonly folders = this.tree.folders;
  readonly readyDocuments = computed(() => this.tree.documents().filter((document) => document.status === 'Ready'));
  /** Whether the demo scope is offered (there are seeded demo documents) — US-03. */
  readonly hasDemo = computed(() => this.tree.demoDocuments().length > 0);

  /** Maps the encoded `<select>` value (`all` / `demo` / `folder:{id}` / `document:{id}`) to a scope and emits it. */
  select(value: string): void {
    if (value === 'all') {
      this.scopeChange.emit({ type: 'all', label: 'Wszystkie dokumenty' });

      return;
    }

    if (value === 'demo') {
      this.scopeChange.emit({ type: 'demo', label: 'Dokumenty demo' });

      return;
    }

    const [type, id] = value.split(':');
    if (type === 'folder') {
      const folder = this.folders().find((candidate) => candidate.id === id);
      this.scopeChange.emit({ type: 'folder', targetId: id, label: folder?.name ?? 'Folder' });
    } else if (type === 'document') {
      const document = this.readyDocuments().find((candidate) => candidate.id === id);
      this.scopeChange.emit({ type: 'document', targetId: id, label: document?.fileName ?? 'Dokument' });
    }
  }
}
