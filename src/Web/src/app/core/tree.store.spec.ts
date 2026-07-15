import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FolderNode, TreeDocumentDto, TreeFolderDto, TreeStore, buildForest } from './tree.store';

describe('TreeStore', () => {
  let store: TreeStore;
  let controller: HttpTestingController;

  const folders: TreeFolderDto[] = [
    { id: 'a', parentId: null, name: 'Umowy', depth: 1 },
    { id: 'b', parentId: 'a', name: '2026', depth: 2 },
  ];
  const documents: TreeDocumentDto[] = [
    { id: 'd1', folderId: 'a', fileName: 'in-folder.pdf', contentType: 'application/pdf', sizeBytes: 12_300_000, status: 'Ready', chunkCount: 4, uploadedAt: '2026-07-11T10:00:00Z', failureReason: null },
    { id: 'd2', folderId: null, fileName: 'root.pdf', contentType: 'application/pdf', sizeBytes: 500, status: 'Processing', chunkCount: 0, uploadedAt: '2026-07-11T09:00:00Z', failureReason: null },
  ];

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(TreeStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('fetches GET /api/tree and composes the nested forest with root documents at the top level', () => {
    store.refresh();
    controller.expectOne('/api/tree').flush({ folders, documents });

    const roots = store.roots();
    // Root folder "Umowy" then root document "root.pdf".
    expect(roots.map((n) => (n.kind === 'folder' ? n.name : n.fileName))).toEqual(['Umowy', 'root.pdf']);

    const umowy = roots[0] as FolderNode;
    // Child folder "2026" before the folder's document "in-folder.pdf".
    expect(umowy.children.map((n) => (n.kind === 'folder' ? n.name : n.fileName))).toEqual(['2026', 'in-folder.pdf']);
    expect(store.isEmpty()).toBeFalse();
  });

  function folderOf(id: string): string | null | undefined {
    return store.documents().find((document) => document.id === id)?.folderId;
  }

  it('optimistically moves a document to a folder and PATCHes it (US-10)', () => {
    store.refresh();
    controller.expectOne('/api/tree').flush({ folders, documents });

    store.moveDocument('d2', 'a');

    // Optimistic: the document is under "a" before the response.
    expect(folderOf('d2')).toBe('a');
    const request = controller.expectOne('/api/documents/d2/folder');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ folderId: 'a' });
    request.flush(null);
    expect(folderOf('d2')).toBe('a'); // stays on success
  });

  it('issues no request when dropped onto the folder it is already in (SC-005)', () => {
    store.refresh();
    controller.expectOne('/api/tree').flush({ folders, documents });

    store.moveDocument('d1', 'a'); // d1 is already in "a"

    expect(folderOf('d1')).toBe('a');
    // afterEach controller.verify() asserts no PATCH was issued.
  });

  it('rolls back and sets a move error when the PATCH fails (US-10 AC-2)', () => {
    store.refresh();
    controller.expectOne('/api/tree').flush({ folders, documents });

    store.moveDocument('d2', 'a');
    expect(folderOf('d2')).toBe('a'); // optimistic

    controller.expectOne('/api/documents/d2/folder').flush({ code: 'folder.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(folderOf('d2')).toBeNull(); // reverted
    expect(store.moveError()).toContain('Folder docelowy już nie istnieje');
  });

  it('reports empty for a fresh session', () => {
    store.refresh();
    controller.expectOne('/api/tree').flush({ folders: [], documents: [] });

    expect(store.isEmpty()).toBeTrue();
    expect(store.roots()).toEqual([]);
  });

  it('derives the display size and a generic failure reason', () => {
    const nodes = buildForest([], [
      { ...documents[0], sizeBytes: 12_300_000 },
      { ...documents[1], status: 'Failed', failureReason: null },
    ]);
    const [ready, failed] = nodes;

    expect(ready.kind).toBe('document');
    if (ready.kind === 'document') {
      expect(ready.displaySize).toBe('12.3 MB');
    }
    if (failed.kind === 'document') {
      expect(failed.displayFailureReason).toContain('nie powiodło');
    }
  });

  it('persists expanded folder ids to sessionStorage for the browser session', () => {
    store.saveExpandedIds(['a', 'b']);

    expect(store.loadExpandedIds()).toEqual(['a', 'b']);
    expect(sessionStorage.getItem('ragbook.tree.expanded')).toBe('["a","b"]');
  });
});
