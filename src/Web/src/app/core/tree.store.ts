import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { formatFileSize } from './file-size';

/** A folder as returned by GET /api/tree. */
export interface TreeFolderDto {
  readonly id: string;
  readonly parentId: string | null;
  readonly name: string;
  readonly depth: number;
}

/** A document as returned by GET /api/tree. */
export interface TreeDocumentDto {
  readonly id: string;
  readonly folderId: string | null;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly status: 'Processing' | 'Ready' | 'Failed';
  readonly chunkCount: number;
  readonly uploadedAt: string;
  readonly failureReason: string | null;
}

interface TreeResponseDto {
  readonly folders: TreeFolderDto[];
  readonly documents: TreeDocumentDto[];
  readonly demo?: TreeDocumentDto[];
}

/** A composed folder node (children = subfolders then documents). */
export interface FolderNode {
  readonly kind: 'folder';
  readonly id: string;
  readonly parentId: string | null;
  readonly name: string;
  readonly depth: number;
  readonly children: TreeNode[];
}

/** A composed document leaf, with display-derived fields. */
export interface DocumentNode extends TreeDocumentDto {
  readonly kind: 'document';
  readonly displaySize: string;
  readonly displayFailureReason: string;
}

export type TreeNode = FolderNode | DocumentNode;

const EXPANDED_KEY = 'ragbook.tree.expanded';
const GENERIC_FAILURE = 'Przetwarzanie nie powiodło się.';

/** Stable move-error codes → Polish messages surfaced when a move rolls back (US-10). */
const MOVE_ERROR_MESSAGES: Record<string, string> = {
  'document.not_found': 'Plik już nie istnieje.',
  'folder.not_found': 'Folder docelowy już nie istnieje.',
  'document.read_only': 'Ten plik jest tylko do odczytu i nie można go przenieść.',
  'folder.circular_move': 'Nie można przenieść folderu do niego samego ani do jego podfolderu.',
  'folder.max_depth_exceeded': 'Osiągnięto maksymalną głębokość zagnieżdżenia.',
  'folder.duplicate_name': 'Folder o tej nazwie już istnieje w miejscu docelowym.',
};
const GENERIC_MOVE_ERROR = 'Nie udało się przenieść pliku. Spróbuj ponownie.';

/**
 * Shared, signal-based store of the folder+document tree (US-07). Fetches the whole view from
 * `GET /api/tree` in one request and composes the nested tree on the client; every mutation
 * (upload/delete/folder ops) calls {@link refresh} so the tree updates without a page reload. Folder
 * expansion is UI state persisted to `sessionStorage` — never sent to the server.
 */
@Injectable({ providedIn: 'root' })
export class TreeStore {
  private readonly http = inject(HttpClient);

  readonly folders = signal<readonly TreeFolderDto[]>([]);
  readonly documents = signal<readonly TreeDocumentDto[]>([]);

  /** The globally-visible, read-only demo documents (US-03); shown in a separate Demo section. */
  readonly demoDocuments = signal<readonly TreeDocumentDto[]>([]);

  /** The composed forest: root folders (A→Z) then root documents (newest-first). */
  readonly roots = computed(() => buildForest(this.folders(), this.documents()));

  /** True when the session has no folders and no documents (drives the empty state). */
  readonly isEmpty = computed(() => this.folders().length === 0 && this.documents().length === 0);

  /** The reason a move rolled back, for a design-system notice (US-10); null when clear. */
  readonly moveError = signal<string | null>(null);

  /**
   * Moves a document to a folder (or the root when <c>null</c>) — **optimistically** (US-10): the tree recomposes
   * at once, then the change is sent. A drop onto the current folder is a no-op (no request). On failure the
   * document snaps back to its previous folder and {@link moveError} carries the reason.
   */
  moveDocument(documentId: string, targetFolderId: string | null): void {
    const document = this.documents().find((candidate) => candidate.id === documentId);
    if (document === undefined || document.folderId === targetFolderId) {
      return; // unknown document, or already there — no request (FR-006)
    }

    const previousFolderId = document.folderId;
    this.setDocumentFolder(documentId, targetFolderId); // optimistic
    this.moveError.set(null);

    this.http.patch(`/api/documents/${documentId}/folder`, { folderId: targetFolderId }).subscribe({
      error: (error: HttpErrorResponse) => {
        this.setDocumentFolder(documentId, previousFolderId); // rollback
        this.moveError.set(this.moveErrorMessage(error));
      },
    });
  }

  /**
   * Moves a folder (with its subtree) under a target folder (or the root when <c>null</c>) — **optimistically**
   * (US-11): the moved folder's `parentId` changes, so the composed tree re-nests the whole subtree at once. A move
   * to the current parent is a no-op. On success the tree refreshes (correcting paths/depths); on failure the
   * folder snaps back and {@link moveError} carries the reason.
   */
  moveFolder(folderId: string, targetParentId: string | null): void {
    const folder = this.folders().find((candidate) => candidate.id === folderId);
    if (folder === undefined || folder.parentId === targetParentId) {
      return; // unknown folder, or already there — no request
    }

    const previousParentId = folder.parentId;
    this.setFolderParent(folderId, targetParentId); // optimistic re-nest
    this.moveError.set(null);

    this.http.patch(`/api/folders/${folderId}/parent`, { parentId: targetParentId }).subscribe({
      next: () => this.refresh(), // reconcile paths/depths from the server
      error: (error: HttpErrorResponse) => {
        this.setFolderParent(folderId, previousParentId); // rollback
        this.moveError.set(this.moveErrorMessage(error));
      },
    });
  }

  /** True when <paramref name="movedId"/> is <paramref name="targetId"/> itself or one of its ancestors — i.e. the target is in the moved folder's subtree (US-11 cycle guard). */
  isDescendant(targetId: string, movedId: string): boolean {
    const byId = new Map(this.folders().map((folder) => [folder.id, folder]));
    let current: string | null = targetId;
    while (current !== null) {
      if (current === movedId) {
        return true;
      }
      current = byId.get(current)?.parentId ?? null;
    }

    return false;
  }

  /** Clears the move-error notice. */
  clearMoveError(): void {
    this.moveError.set(null);
  }

  private setFolderParent(folderId: string, parentId: string | null): void {
    this.folders.update((folders) =>
      folders.map((folder) => (folder.id === folderId ? { ...folder, parentId } : folder)),
    );
  }

  private setDocumentFolder(documentId: string, folderId: string | null): void {
    this.documents.update((documents) =>
      documents.map((document) => (document.id === documentId ? { ...document, folderId } : document)),
    );
  }

  private moveErrorMessage(error: HttpErrorResponse): string {
    const code = (error.error as { code?: string } | null)?.code;

    return (code && MOVE_ERROR_MESSAGES[code]) || GENERIC_MOVE_ERROR;
  }

  /** Re-reads the whole tree from the backend. Call after any upload, delete, or folder mutation. */
  refresh(): void {
    this.http.get<TreeResponseDto>('/api/tree').subscribe((response) => {
      this.folders.set(response.folders);
      this.documents.set(response.documents);
      this.demoDocuments.set(response.demo ?? []);
    });
  }

  /** The folder ids expanded in this browser session (UI state). */
  loadExpandedIds(): string[] {
    try {
      const raw = sessionStorage.getItem(EXPANDED_KEY);

      return raw ? (JSON.parse(raw) as string[]) : [];
    } catch {
      return [];
    }
  }

  /** Persists the expanded folder ids for this browser session. */
  saveExpandedIds(ids: string[]): void {
    sessionStorage.setItem(EXPANDED_KEY, JSON.stringify(ids));
  }
}

function toDocumentNode(document: TreeDocumentDto): DocumentNode {
  return {
    ...document,
    kind: 'document',
    displaySize: formatFileSize(document.sizeBytes),
    displayFailureReason: document.failureReason ?? GENERIC_FAILURE,
  };
}

/**
 * Composes the nested forest from the two flat, server-ordered lists. Within a folder, child folders
 * (A→Z) render before its documents (newest-first); root documents follow the root folders.
 */
export function buildForest(
  folders: readonly TreeFolderDto[],
  documents: readonly TreeDocumentDto[],
): TreeNode[] {
  const folderNodes: FolderNode[] = folders.map((folder) => ({
    kind: 'folder',
    id: folder.id,
    parentId: folder.parentId,
    name: folder.name,
    depth: folder.depth,
    children: [],
  }));
  const byId = new Map(folderNodes.map((node) => [node.id, node]));
  const roots: TreeNode[] = [];

  // Folders first (already A→Z), so sibling folder order is preserved.
  for (const node of folderNodes) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined;
    if (parent) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  // Documents after folders (already newest-first) — placed under their folder, or at the root.
  for (const document of documents) {
    const node = toDocumentNode(document);
    const parent = document.folderId ? byId.get(document.folderId) : undefined;
    if (parent) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  return roots;
}
