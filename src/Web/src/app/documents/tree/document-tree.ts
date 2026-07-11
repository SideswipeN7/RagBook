import { CdkTreeModule, NestedTreeControl } from '@angular/cdk/tree';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { FolderTreeStore, MAX_FOLDER_DEPTH } from '../../core/folder-tree.store';
import { FolderNode, TreeNode, TreeStore } from '../../core/tree.store';
import { DocumentRow } from './document-row';

const FOLDER_ERROR_MESSAGES: Record<string, string> = {
  'folder.invalid_name': 'Nieprawidłowa nazwa folderu.',
  'folder.max_depth_exceeded': 'Osiągnięto maksymalną głębokość zagnieżdżenia.',
  'folder.duplicate_name': 'Folder o tej nazwie już istnieje w tym miejscu.',
  'folder.not_empty': 'Usuń lub przenieś zawartość przed usunięciem folderu.',
  'folder.not_found': 'Folder nie istnieje.',
};

/**
 * The unified folders + documents tree (US-07), built with `@angular/cdk` `cdk-tree`. Folders nest with
 * their documents inside; root documents are top-level. Folders expand/collapse with the state persisted
 * in `sessionStorage` (survives in-session navigation). Folder create/rename/delete actions delegate to
 * {@link FolderTreeStore} and then refresh **the tree** ({@link TreeStore.refresh}) so it never goes stale
 * (analyze I1). An empty session shows the upload call-to-action + demo pointer. Standalone, OnPush.
 */
@Component({
  selector: 'app-document-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkTreeModule, DocumentRow],
  templateUrl: './document-tree.html',
  styleUrl: './document-tree.scss',
})
export class DocumentTree {
  private readonly store = inject(TreeStore);
  private readonly folders = inject(FolderTreeStore);

  readonly roots = this.store.roots;
  readonly isEmpty = this.store.isEmpty;
  readonly maxDepth = MAX_FOLDER_DEPTH;

  readonly treeControl = new NestedTreeControl<TreeNode>((node) => (node.kind === 'folder' ? node.children : []));

  readonly creatingUnder = signal<string | null | undefined>(undefined);
  readonly renamingId = signal<string | null>(null);
  readonly confirmingDeleteId = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  private restoring = false;

  constructor() {
    this.store.refresh();

    // Persist the expanded folder ids for the browser session (skip while we are restoring them).
    this.treeControl.expansionModel.changed.subscribe(() => {
      if (this.restoring) {
        return;
      }
      const ids = this.treeControl.expansionModel.selected
        .filter((node): node is FolderNode => node.kind === 'folder')
        .map((node) => node.id);
      this.store.saveExpandedIds(ids);
    });

    // Node identity changes on every refresh, so re-apply the persisted expansion to the new nodes.
    effect(() => {
      const roots = this.roots();
      const persisted = new Set(this.store.loadExpandedIds());
      this.restoring = true;
      this.treeControl.expansionModel.clear();
      const walk = (nodes: TreeNode[]): void => {
        for (const node of nodes) {
          if (node.kind === 'folder') {
            if (persisted.has(node.id)) {
              this.treeControl.expand(node);
            }
            walk(node.children);
          }
        }
      };
      walk(roots);
      this.restoring = false;
    });
  }

  isFolder = (_: number, node: TreeNode): node is FolderNode => node.kind === 'folder';

  hasContent(node: FolderNode): boolean {
    return node.children.length > 0;
  }

  startCreate(parentId: string | null): void {
    this.resetInlineState();
    this.creatingUnder.set(parentId);
  }

  submitCreate(name: string): void {
    const parentId = this.creatingUnder() ?? null;
    this.folders.create(name, parentId).subscribe({
      next: () => {
        this.creatingUnder.set(undefined);
        this.store.refresh();
      },
      error: (error) => this.showError(error),
    });
  }

  startRename(id: string): void {
    this.resetInlineState();
    this.renamingId.set(id);
  }

  submitRename(id: string, name: string): void {
    this.folders.rename(id, name).subscribe({
      next: () => {
        this.renamingId.set(null);
        this.store.refresh();
      },
      error: (error) => this.showError(error),
    });
  }

  askDelete(id: string): void {
    this.resetInlineState();
    this.confirmingDeleteId.set(id);
  }

  confirmDelete(id: string): void {
    this.folders.remove(id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.store.refresh();
      },
      error: (error) => this.showError(error),
    });
  }

  cancelInline(): void {
    this.resetInlineState();
  }

  private resetInlineState(): void {
    this.creatingUnder.set(undefined);
    this.renamingId.set(null);
    this.confirmingDeleteId.set(null);
    this.errorMessage.set(null);
  }

  private showError(error: unknown): void {
    const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
    this.errorMessage.set(FOLDER_ERROR_MESSAGES[code] ?? 'Wystąpił nieoczekiwany błąd.');
  }
}
