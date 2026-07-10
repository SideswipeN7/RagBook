import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentUploadStore, preValidate } from './document-upload.store';

describe('DocumentUploadStore', () => {
  let store: DocumentUploadStore;
  let controller: HttpTestingController;

  const pdf = () => new File([new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d])], 'umowa.pdf', { type: 'application/pdf' });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(DocumentUploadStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should POST the file then refresh folders and quota', () => {
    store.upload(pdf(), null);

    const upload = controller.expectOne((req) => req.method === 'POST' && req.url === '/api/documents');
    expect(upload.request.body instanceof FormData).toBeTrue();
    upload.flush({ id: 'x', fileName: 'umowa.pdf', status: 'Processing' });

    controller.expectOne('/api/folders').flush([]);
    controller.expectOne('/api/quota').flush({});
    expect(store.progress()).toBeNull();
  });

  it('should include folderId when uploading into a folder', () => {
    store.upload(pdf(), 'folder-1');

    const upload = controller.expectOne('/api/documents');
    expect((upload.request.body as FormData).get('folderId')).toBe('folder-1');
    upload.flush({ id: 'x' });
    controller.expectOne('/api/folders').flush([]);
    controller.expectOne('/api/quota').flush({});
  });

  it('should surface the server error code on failure', () => {
    store.upload(pdf(), null);

    controller
      .expectOne('/api/documents')
      .flush({ code: 'quota.exceeded' }, { status: 409, statusText: 'Conflict' });

    expect(store.error()).toContain('Limit plików');
  });

  it('should reject an unsupported extension before uploading', () => {
    store.upload(new File([new Uint8Array([1, 2, 3])], 'malware.exe', { type: 'application/octet-stream' }), null);

    controller.expectNone('/api/documents');
    expect(store.error()).toContain('Nieobsługiwany typ');
  });

  it('preValidate flags empty and oversized files', () => {
    expect(preValidate(new File([], 'a.txt'))).toContain('pusty');
    const big = new File([new Uint8Array(10_000_001)], 'a.txt', { type: 'text/plain' });
    expect(preValidate(big)).toContain('rozmiar');
  });
});
