import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DocumentUploadStore } from '../../core/document-upload.store';

/**
 * Upload affordance (US-04): a file-picker button and a drag-and-drop zone that hand the chosen file to
 * the shared {@link DocumentUploadStore}, showing progress and any error. Standalone, OnPush, signals;
 * styled with design tokens. Uploads to the root here; per-folder drop is a later refinement.
 */
@Component({
  selector: 'app-document-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-upload.html',
  styleUrl: './document-upload.scss',
})
export class DocumentUpload {
  private readonly store = inject(DocumentUploadStore);

  readonly progress = this.store.progress;
  readonly error = this.store.error;
  readonly dragging = signal(false);

  onFileSelected(input: HTMLInputElement): void {
    const file = input.files?.[0];
    if (file) {
      this.store.upload(file, null);
      input.value = '';
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.store.upload(file, null);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  onDragLeave(): void {
    this.dragging.set(false);
  }
}
