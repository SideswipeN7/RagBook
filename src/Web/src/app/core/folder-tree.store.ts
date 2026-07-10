import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

/** Maximum folder nesting depth. Mirrors the backend `Folders:MaxDepth` default (US-09, AC-2). */
export const MAX_FOLDER_DEPTH = 3;

/** A flat folder node returned by `GET /api/folders` (US-09). */
export interface FolderNode {
  readonly id: string;
  readonly parentId: string | null;
  readonly name: string;
  readonly depth: number;
}

/** A folder node with its children, composed on the client from {@link FolderNode.parentId}. */
export interface FolderTreeNode extends FolderNode {
  readonly children: FolderTreeNode[];
}

/**
 * Shared, signal-based store of the session's folder tree. It reads the flat, name-ordered list from
 * the backend and composes the nested tree on the client; every mutation refreshes the list so the UI
 * updates without a page reload. Isolation and validation are backend-managed — this store only issues
 * requests and reflects the result.
 */
@Injectable({ providedIn: 'root' })
export class FolderTreeStore {
  private readonly http = inject(HttpClient);

  readonly nodes = signal<readonly FolderNode[]>([]);

  /** The session's folders as a nested tree, preserving the backend's case-insensitive name order. */
  readonly tree = computed(() => buildTree(this.nodes()));

  /** Re-reads the folder list from the backend. Call after any create/rename/delete. */
  refresh(): void {
    this.http.get<FolderNode[]>('/api/folders').subscribe((nodes) => this.nodes.set(nodes));
  }

  /** Creates a folder at the root (`parentId` null) or inside a parent, then refreshes. */
  create(name: string, parentId: string | null): Observable<unknown> {
    return this.http.post('/api/folders', { name, parentId }).pipe(tap(() => this.refresh()));
  }

  /** Renames a folder, then refreshes. */
  rename(id: string, name: string): Observable<unknown> {
    return this.http.put(`/api/folders/${id}/name`, { name }).pipe(tap(() => this.refresh()));
  }

  /** Deletes a folder, then refreshes. */
  remove(id: string): Observable<unknown> {
    return this.http.delete(`/api/folders/${id}`).pipe(tap(() => this.refresh()));
  }
}

/** Composes a nested tree from the flat, ordered node list. Order within each level is preserved. */
export function buildTree(nodes: readonly FolderNode[]): FolderTreeNode[] {
  const byId = new Map<string, FolderTreeNode>();
  for (const node of nodes) {
    byId.set(node.id, { ...node, children: [] });
  }

  const roots: FolderTreeNode[] = [];
  for (const node of nodes) {
    const treeNode = byId.get(node.id)!;
    const parent = node.parentId ? byId.get(node.parentId) : undefined;

    if (parent) {
      parent.children.push(treeNode);
    } else {
      roots.push(treeNode);
    }
  }

  return roots;
}
