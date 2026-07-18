import { HttpErrorResponse, HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { messageForCode } from './error-messages';
import { QuotaStore } from './quota.store';
import { TreeStore } from './tree.store';

/** Client-side pre-validation limits (a convenience — the server is the authority, US-04 FR-015). */
export const MAX_UPLOAD_MB = 10;
export const ALLOWED_UPLOAD_EXTENSIONS = ['.pdf', '.txt', '.md', '.markdown'];

/** Pre-validates a file by extension and size; returns an error message or `null` when acceptable. */
export function preValidate(file: File): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!ALLOWED_UPLOAD_EXTENSIONS.includes(extension)) {
    return messageForCode('document.unsupported_file_type');
  }
  if (file.size === 0) {
    return messageForCode('document.empty_file');
  }
  if (file.size > MAX_UPLOAD_MB * 1_000_000) {
    return messageForCode('quota.file_too_large');
  }

  return null;
}

/**
 * Uploads a file to `POST /api/documents` with progress, then refreshes the folder tree and the quota
 * so the new document and updated counter appear without a page reload (US-04). Pre-validation is a
 * convenience; the backend re-validates and remains the source of truth.
 */
@Injectable({ providedIn: 'root' })
export class DocumentUploadStore {
  private readonly http = inject(HttpClient);
  private readonly tree = inject(TreeStore);
  private readonly quota = inject(QuotaStore);

  /** Upload progress as a percentage, or `null` when no upload is in flight. */
  readonly progress = signal<number | null>(null);
  readonly error = signal<string | null>(null);

  upload(file: File, folderId: string | null): void {
    const preError = preValidate(file);
    if (preError) {
      this.error.set(preError);

      return;
    }

    const form = new FormData();
    form.append('file', file);
    if (folderId) {
      form.append('folderId', folderId);
    }

    this.error.set(null);
    this.progress.set(0);

    this.http.post('/api/documents', form, { reportProgress: true, observe: 'events' }).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.progress.set(Math.round((100 * event.loaded) / event.total));
        } else if (event.type === HttpEventType.Response) {
          this.progress.set(null);
          this.tree.refresh();
          this.quota.refresh();
        }
      },
      error: (response: HttpErrorResponse) => {
        this.progress.set(null);
        this.error.set(messageForCode(response.error?.code, 'Nie udało się wgrać pliku.'));
      },
    });
  }
}
