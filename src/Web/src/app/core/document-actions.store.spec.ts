import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentActionsStore } from './document-actions.store';

describe('DocumentActionsStore', () => {
  let store: DocumentActionsStore;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(DocumentActionsStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('deletes the document then refreshes the tree and quota', () => {
    store.delete('doc-1');

    const del = controller.expectOne((r) => r.method === 'DELETE' && r.url === '/api/documents/doc-1');
    del.flush(null, { status: 204, statusText: 'No Content' });

    controller.expectOne('/api/tree').flush({ folders: [], documents: [] });
    controller.expectOne('/api/quota').flush({});
  });

  it('still refreshes on a 404 (already deleted)', () => {
    store.delete('gone');

    controller
      .expectOne('/api/documents/gone')
      .flush({ code: 'document.not_found' }, { status: 404, statusText: 'Not Found' });

    // The row is already gone — refresh so the tree/quota reflect reality.
    controller.expectOne('/api/tree').flush({ folders: [], documents: [] });
    controller.expectOne('/api/quota').flush({});
  });
});
