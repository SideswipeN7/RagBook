import { CdkDrag, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { CdkTreeModule, NestedTreeControl } from '@angular/cdk/tree';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { DocumentActionsStore } from '../../core/document-actions.store';
import { FolderTreeStore, MAX_FOLDER_DEPTH } from '../../core/folder-tree.store';
import { SelectionStore } from '../../core/selection.store';
import { DocumentNode, FolderNode, TreeNode, TreeStore } from '../../core/tree.store';
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
  imports: [CdkTreeModule, CdkDropListGroup, CdkDropList, CdkDrag, DocumentRow],
  templateUrl: './document-tree.html',
  styleUrl: './document-tree.scss',
})
export class DocumentTree {
  private readonly store = inject(TreeStore);
  private readonly folders = inject(FolderTreeStore);
  private readonly documents = inject(DocumentActionsStore);
  /** Cross-cutting multi-select + bulk actions (US-12); public so the template can read its state. */
  readonly selection = inject(SelectionStore);

  readonly roots = this.store.roots;
  readonly isEmpty = this.store.isEmpty;
  readonly maxDepth = MAX_FOLDER_DEPTH;
  /** The global read-only demo documents (US-03) — a separate section, no mutating controls. */
  readonly demoDocuments = this.store.demoDocuments;

  // US-12 bulk selection UI state.
  readonly bulkMoving = signal(false);
  readonly confirmingBulkDelete = signal(false);
  private lastSelectedId: string | null = null;

  readonly treeControl = new NestedTreeControl<TreeNode>((node) => (node.kind === 'folder' ? node.children : []));

  readonly creatingUnder = signal<string | null | undefined>(undefined);
  readonly renamingId = signal<string | null>(null);
  readonly confirmingDeleteId = signal<string | null>(null);
  readonly confirmingDeleteDocumentId = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  // US-10 drag & drop / "Przenieś do…"
  readonly folderList = this.store.folders;
  readonly moveError = this.store.moveError;
  /** The current drop target: a folder id, `null` for the root zone, or `undefined` for none (highlight). */
  readonly dropTarget = signal<string | null | undefined>(undefined);
  /** The document whose "Przenieś do…" picker is open, or null. */
  readonly movingId = signal<string | null>(null);

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

  /** A tree node was dropped onto a folder (or the root when null) — routed by kind to the right optimistic move. */
  onDrop(node: TreeNode, targetFolderId: string | null): void {
    this.dropTarget.set(undefined);
    if (node.kind === 'document') {
      this.store.moveDocument(node.id, targetFolderId);
    } else {
      this.store.moveFolder(node.id, targetFolderId);
    }
  }

  /** Enter-predicate for a folder drop target: reject dropping a folder into itself or its own subtree (US-11 AC-2). */
  dropPredicate(targetFolderId: string): (drag: { data: TreeNode }) => boolean {
    return (drag) => !(drag.data.kind === 'folder' && this.store.isDescendant(targetFolderId, drag.data.id));
  }

  // US-11 folder "Przenieś do…" menu (a11y parity with US-10).
  readonly movingFolderId = signal<string | null>(null);

  startMoveFolder(folderId: string): void {
    this.resetInlineState();
    this.movingFolderId.set(folderId);
  }

  chooseMoveFolder(folderId: string, targetParentId: string | null): void {
    this.movingFolderId.set(null);
    this.store.moveFolder(folderId, targetParentId);
  }

  cancelMoveFolder(): void {
    this.movingFolderId.set(null);
  }

  /** Valid move targets for a folder — every folder except the folder itself and its subtree. */
  folderMoveTargets(movedId: string): readonly { id: string; name: string }[] {
    return this.store.folders().filter((folder) => !this.store.isDescendant(folder.id, movedId));
  }

  /** Opens the "Przenieś do…" picker for a document (keyboard/menu fallback — US-10 AC-5). */
  startMove(documentId: string): void {
    this.resetInlineState();
    this.movingId.set(documentId);
  }

  /** Moves the document to the chosen folder (or the root) via the same store action as a drop. */
  chooseMove(documentId: string, targetFolderId: string | null): void {
    this.movingId.set(null);
    this.store.moveDocument(documentId, targetFolderId);
  }

  cancelMove(): void {
    this.movingId.set(null);
  }

  clearMoveErrorNotice(): void {
    this.store.clearMoveError();
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

  // Document-leaf delete (US-08) — a separate confirm so a folder and a document never cross wires.
  askDeleteDocument(id: string): void {
    this.resetInlineState();
    this.confirmingDeleteDocumentId.set(id);
  }

  confirmDeleteDocument(id: string): void {
    this.documents.delete(id); // the store issues DELETE /api/documents/{id} and refreshes tree + quota
    this.confirmingDeleteDocumentId.set(null);
  }

  cancelInline(): void {
    this.resetInlineState();
  }

  // US-12 — multi-select + bulk move / delete.

  /**
   * Ticks a document from its checkbox. A Shift-click extends the selection across the contiguous range within the
   * same folder (AC-1); a plain click toggles the single row. The last-clicked row anchors the next range.
   */
  select(node: DocumentNode, event: MouseEvent): void {
    const folderDocIds = this.store.documents().filter((doc) => doc.folderId === node.folderId).map((doc) => doc.id);
    if (event.shiftKey && this.lastSelectedId !== null && folderDocIds.includes(this.lastSelectedId)) {
      this.selection.selectRange(folderDocIds, this.lastSelectedId, node.id);
    } else {
      this.selection.toggle(node.id);
    }
    this.lastSelectedId = node.id;
  }

  /** The file names of the currently selected documents (shown in the bulk-delete confirm). */
  selectedNames(): readonly string[] {
    const ids = new Set(this.selection.selectedIds());

    return this.store.documents().filter((doc) => ids.has(doc.id)).map((doc) => doc.fileName);
  }

  startBulkMove(): void {
    this.resetInlineState();
    this.bulkMoving.set(true);
  }

  chooseBulkMove(targetFolderId: string | null): void {
    this.bulkMoving.set(false);
    this.selection.bulkMove(targetFolderId);
  }

  cancelBulkMove(): void {
    this.bulkMoving.set(false);
  }

  askBulkDelete(): void {
    this.resetInlineState();
    this.confirmingBulkDelete.set(true);
  }

  confirmBulkDelete(): void {
    this.confirmingBulkDelete.set(false);
    this.selection.bulkDelete();
  }

  cancelBulkDelete(): void {
    this.confirmingBulkDelete.set(false);
  }

  cancelSelection(): void {
    this.selection.clear();
  }

  clearBulkErrorNotice(): void {
    this.selection.clearBulkError();
  }

  private resetInlineState(): void {
    this.creatingUnder.set(undefined);
    this.renamingId.set(null);
    this.confirmingDeleteId.set(null);
    this.confirmingDeleteDocumentId.set(null);
    this.errorMessage.set(null);
    this.movingId.set(null);
    this.movingFolderId.set(null);
    this.bulkMoving.set(false);
    this.confirmingBulkDelete.set(false);
  }

  private showError(error: unknown): void {
    const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
    this.errorMessage.set(FOLDER_ERROR_MESSAGES[code] ?? 'Wystąpił nieoczekiwany błąd.');
  }
}
