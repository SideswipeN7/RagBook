import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentStatusStore } from './document-status.store';

describe('DocumentStatusStore', () => {
  let store: DocumentStatusStore;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(DocumentStatusStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('refreshes the tree when a status is pushed (SSE event → GET /api/tree)', () => {
    store.onStatus();

    controller.expectOne('/api/tree').flush({ folders: [], documents: [] });
    // The status push re-reads the tree so a row flips processing → ready/failed without a reload.
    expect().nothing();
  });
});
