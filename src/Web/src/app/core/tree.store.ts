import { HttpClient } from '@angular/common/http';
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

  /** The composed forest: root folders (A→Z) then root documents (newest-first). */
  readonly roots = computed(() => buildForest(this.folders(), this.documents()));

  /** True when the session has no folders and no documents (drives the empty state). */
  readonly isEmpty = computed(() => this.folders().length === 0 && this.documents().length === 0);

  /** Re-reads the whole tree from the backend. Call after any upload, delete, or folder mutation. */
  refresh(): void {
    this.http.get<TreeResponseDto>('/api/tree').subscribe((response) => {
      this.folders.set(response.folders);
      this.documents.set(response.documents);
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
