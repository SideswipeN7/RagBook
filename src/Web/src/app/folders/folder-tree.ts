import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MAX_FOLDER_DEPTH, FolderTreeStore } from '../core/folder-tree.store';

/** Human-readable messages for the stable `folder.*` error codes (US-09). */
const ERROR_MESSAGES: Record<string, string> = {
  'folder.invalid_name': 'Nieprawidłowa nazwa folderu.',
  'folder.max_depth_exceeded': 'Osiągnięto maksymalną głębokość zagnieżdżenia.',
  'folder.duplicate_name': 'Folder o tej nazwie już istnieje w tym miejscu.',
  'folder.not_empty': 'Usuń lub przenieś zawartość przed usunięciem folderu.',
  'folder.not_found': 'Folder nie istnieje.',
};

/**
 * Renders the session's folder tree with per-node context actions — new folder, rename, delete —
 * reading the shared {@link FolderTreeStore}. "New folder" is hidden once a folder is at the maximum
 * depth (FR-012); deletion asks for inline confirmation (never a native dialog). Standalone, OnPush,
 * signals, new control flow; styled with design tokens.
 */
@Component({
  selector: 'app-folder-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  templateUrl: './folder-tree.html',
  styleUrl: './folder-tree.scss',
})
export class FolderTree implements OnInit {
  private readonly store = inject(FolderTreeStore);

  readonly maxDepth = MAX_FOLDER_DEPTH;
  readonly tree = this.store.tree;

  /** Parent id the "new folder" input is open for: `null` = root, a string = that folder, `undefined` = closed. */
  readonly creatingUnder = signal<string | null | undefined>(undefined);
  readonly renamingId = signal<string | null>(null);
  readonly confirmingDeleteId = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.store.refresh();
  }

  startCreate(parentId: string | null): void {
    this.errorMessage.set(null);
    this.confirmingDeleteId.set(null);
    this.renamingId.set(null);
    this.creatingUnder.set(parentId);
  }

  cancelCreate(): void {
    this.creatingUnder.set(undefined);
  }

  submitCreate(name: string): void {
    const parentId = this.creatingUnder() ?? null;
    this.store.create(name, parentId).subscribe({
      next: () => this.creatingUnder.set(undefined),
      error: (error) => this.showError(error),
    });
  }

  startRename(id: string): void {
    this.errorMessage.set(null);
    this.creatingUnder.set(undefined);
    this.confirmingDeleteId.set(null);
    this.renamingId.set(id);
  }

  cancelRename(): void {
    this.renamingId.set(null);
  }

  submitRename(id: string, name: string): void {
    this.store.rename(id, name).subscribe({
      next: () => this.renamingId.set(null),
      error: (error) => this.showError(error),
    });
  }

  askDelete(id: string): void {
    this.errorMessage.set(null);
    this.confirmingDeleteId.set(id);
  }

  cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  confirmDelete(id: string): void {
    this.store.remove(id).subscribe({
      next: () => this.confirmingDeleteId.set(null),
      error: (error) => this.showError(error),
    });
  }

  private showError(error: unknown): void {
    const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
    this.errorMessage.set(ERROR_MESSAGES[code] ?? 'Wystąpił nieoczekiwany błąd.');
  }
}
