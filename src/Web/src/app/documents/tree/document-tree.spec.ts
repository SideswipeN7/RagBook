import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentNode, FolderNode, TreeDocumentDto, TreeFolderDto, TreeStore } from '../../core/tree.store';
import { SelectionStore } from '../../core/selection.store';
import { DocumentTree } from './document-tree';

const folders: TreeFolderDto[] = [
  { id: 'a', parentId: null, name: 'Umowy', depth: 1 },
];
const documents: TreeDocumentDto[] = [
  { id: 'd1', folderId: 'a', fileName: 'in-folder.pdf', contentType: 'application/pdf', sizeBytes: 100, status: 'Ready', chunkCount: 2, uploadedAt: '2026-07-11T10:00:00Z', failureReason: null },
  { id: 'd2', folderId: null, fileName: 'root.pdf', contentType: 'application/pdf', sizeBytes: 200, status: 'Processing', chunkCount: 0, uploadedAt: '2026-07-11T09:00:00Z', failureReason: null },
];

describe('DocumentTree', () => {
  let fixture: ComponentFixture<DocumentTree>;
  let controller: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(DocumentTree);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function loadTree(f = folders, d = documents): HTMLElement {
    fixture.detectChanges(); // constructor → refresh() → GET /api/tree
    controller.expectOne('/api/tree').flush({ folders: f, documents: d });
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('renders folders and root documents from GET /api/tree', () => {
    const el = loadTree();
    const text = el.textContent ?? '';

    expect(text).toContain('Umowy'); // folder
    expect(text).toContain('root.pdf'); // root document (top-level leaf)
  });

  it('moves a dropped document onto a folder via the store (US-10 AC-1)', () => {
    loadTree();
    const spy = spyOn(TestBed.inject(TreeStore), 'moveDocument');

    fixture.componentInstance.onDrop({ id: 'd2', kind: 'document' } as DocumentNode, 'a');

    expect(spy).toHaveBeenCalledWith('d2', 'a');
  });

  it('moves a dropped document onto the root zone (US-10 AC-4)', () => {
    loadTree();
    const spy = spyOn(TestBed.inject(TreeStore), 'moveDocument');

    fixture.componentInstance.onDrop({ id: 'd1', kind: 'document' } as DocumentNode, null);

    expect(spy).toHaveBeenCalledWith('d1', null);
  });

  it('routes a dropped folder to moveFolder (US-11)', () => {
    loadTree();
    const store = TestBed.inject(TreeStore);
    const folderSpy = spyOn(store, 'moveFolder');
    const documentSpy = spyOn(store, 'moveDocument');

    fixture.componentInstance.onDrop({ id: 'child', kind: 'folder' } as unknown as FolderNode, null);

    expect(folderSpy).toHaveBeenCalledWith('child', null);
    expect(documentSpy).not.toHaveBeenCalled();
  });

  it('drop predicate rejects a folder dropped into its own subtree (US-11 AC-2)', () => {
    loadTree([{ id: 'a', parentId: null, name: 'A', depth: 1 }, { id: 'b', parentId: 'a', name: 'B', depth: 2 }] as TreeFolderDto[], [] as TreeDocumentDto[]);

    // Dragging folder A over target B (a descendant of A) must be rejected.
    const predicate = fixture.componentInstance.dropPredicate('b');
    expect(predicate({ data: { id: 'a', kind: 'folder' } as unknown as FolderNode } as never)).toBeFalse();
    // Dragging A over an unrelated target is allowed.
    expect(fixture.componentInstance.dropPredicate('x')({ data: { id: 'a', kind: 'folder' } as unknown as FolderNode } as never)).toBeTrue();
  });

  it('"Przenieś do…" folder menu moves via moveFolder and never offers self or a descendant (US-11 FR-011)', () => {
    loadTree([{ id: 'a', parentId: null, name: 'A', depth: 1 }, { id: 'b', parentId: 'a', name: 'B', depth: 2 }, { id: 'c', parentId: null, name: 'C', depth: 1 }] as TreeFolderDto[], [] as TreeDocumentDto[]);
    const spy = spyOn(TestBed.inject(TreeStore), 'moveFolder');

    // Targets for moving A exclude A itself and its descendant B, but include C.
    const targets = fixture.componentInstance.folderMoveTargets('a').map((t) => t.id);
    expect(targets).toContain('c');
    expect(targets).not.toContain('a');
    expect(targets).not.toContain('b');

    fixture.componentInstance.chooseMoveFolder('a', 'c');
    expect(spy).toHaveBeenCalledWith('a', 'c');
  });

  it('"Przenieś do…" menu moves via the same store action as a drop (US-10 AC-5)', () => {
    loadTree();
    const spy = spyOn(TestBed.inject(TreeStore), 'moveDocument');

    fixture.componentInstance.chooseMove('d2', 'a');

    expect(spy).toHaveBeenCalledWith('d2', 'a');
    expect(fixture.componentInstance.movingId()).toBeNull();
  });

  it('highlights the hovered drop target (US-10 AC-3)', () => {
    const el = loadTree();

    fixture.componentInstance.dropTarget.set('a');
    fixture.detectChanges();

    expect(el.querySelector('.tree__row--drop')).toBeTruthy();
  });

  it('reveals a folder document when the folder is expanded', () => {
    loadTree();

    const umowy = fixture.componentInstance.roots()[0] as FolderNode;
    fixture.componentInstance.treeControl.expand(umowy);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('in-folder.pdf');
  });

  it('shows the empty state with the upload CTA and a demo pointer for a fresh session', () => {
    const el = loadTree([], []);
    const text = el.textContent ?? '';

    expect(text).toContain('Wgraj pierwszy dokument');
    expect(text).toContain('demo');
    expect(el.querySelector('cdk-tree')).toBeNull();
  });

  it('refreshes the tree after creating a folder (analyze I1)', () => {
    loadTree();

    fixture.componentInstance.startCreate(null);
    fixture.componentInstance.submitCreate('Faktury');

    // FolderTreeStore.create posts then refreshes its own list; the component refreshes the TREE.
    controller.expectOne((r) => r.method === 'POST' && r.url === '/api/folders').flush({ id: 'x' });
    controller.expectOne('/api/folders').flush([]); // FolderTreeStore.refresh()
    controller.expectOne('/api/tree').flush({ folders, documents }); // component → TreeStore.refresh() (I1)

    expect(fixture.componentInstance.creatingUnder()).toBeUndefined();
  });

  it('shows an "empty folder" note for a folder with no children when expanded', () => {
    loadTree([{ id: 'e', parentId: null, name: 'Pusty', depth: 1 }], []);

    const empty = fixture.componentInstance.roots()[0] as FolderNode;
    fixture.componentInstance.treeControl.expand(empty);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Pusty folder');
  });

  it('renders a read-only demo section with no mutating controls (US-03 AC-4)', () => {
    const el = loadTree();
    const store = TestBed.inject(TreeStore);

    store.demoDocuments.set([
      { id: 'demo1', folderId: null, fileName: 'demo-umowa.pdf', contentType: 'application/pdf', sizeBytes: 10, status: 'Ready', chunkCount: 2, uploadedAt: '2026-07-09T10:00:00Z', failureReason: null },
    ]);
    fixture.detectChanges();

    const demoSection = el.querySelector('.tree__demo');
    expect(demoSection).toBeTruthy();
    expect(demoSection?.textContent).toContain('demo-umowa.pdf');
    expect(demoSection?.textContent).toContain('tylko do odczytu');
    // No move/delete/checkbox controls inside the demo section.
    expect(demoSection?.querySelector('button')).toBeNull();
    expect(demoSection?.querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('shows the bulk action bar with the count while documents are selected (US-12 AC-1)', () => {
    const el = loadTree();
    const selection = TestBed.inject(SelectionStore);

    selection.toggle('d1');
    selection.toggle('d2');
    fixture.detectChanges();

    expect(el.querySelector('.tree__bulkbar')).toBeTruthy();
    expect(el.querySelector('.tree__bulkbar')?.textContent).toContain('2 zaznaczonych');
  });

  it('select() toggles a single document and Shift-click extends the range within the folder (US-12 AC-1)', () => {
    loadTree(
      [{ id: 'a', parentId: null, name: 'A', depth: 1 }] as TreeFolderDto[],
      [
        { id: 'x1', folderId: 'a', fileName: 'x1.pdf', contentType: 'application/pdf', sizeBytes: 1, status: 'Ready', chunkCount: 1, uploadedAt: '2026-07-11T10:00:00Z', failureReason: null },
        { id: 'x2', folderId: 'a', fileName: 'x2.pdf', contentType: 'application/pdf', sizeBytes: 1, status: 'Ready', chunkCount: 1, uploadedAt: '2026-07-11T09:00:00Z', failureReason: null },
        { id: 'x3', folderId: 'a', fileName: 'x3.pdf', contentType: 'application/pdf', sizeBytes: 1, status: 'Ready', chunkCount: 1, uploadedAt: '2026-07-11T08:00:00Z', failureReason: null },
      ] as TreeDocumentDto[],
    );
    const selection = TestBed.inject(SelectionStore);
    const component = fixture.componentInstance;
    const node = (id: string): DocumentNode => ({ id, folderId: 'a', kind: 'document' } as DocumentNode);

    component.select(node('x1'), { shiftKey: false } as MouseEvent);
    component.select(node('x3'), { shiftKey: true } as MouseEvent);

    expect(selection.selectedIds().sort()).toEqual(['x1', 'x2', 'x3']);
  });

  it('bulk move picker calls SelectionStore.bulkMove with the chosen folder (US-12 AC-2)', () => {
    loadTree();
    const spy = spyOn(TestBed.inject(SelectionStore), 'bulkMove');

    fixture.componentInstance.startBulkMove();
    fixture.componentInstance.chooseBulkMove('a');

    expect(spy).toHaveBeenCalledWith('a');
    expect(fixture.componentInstance.bulkMoving()).toBeFalse();
  });

  it('bulk delete confirm calls SelectionStore.bulkDelete only after confirming (US-12 AC-3)', () => {
    loadTree();
    const selection = TestBed.inject(SelectionStore);
    const spy = spyOn(selection, 'bulkDelete');
    selection.toggle('d2');

    fixture.componentInstance.askBulkDelete();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Usunąć 1 dokumentów');

    fixture.componentInstance.cancelBulkDelete();
    expect(spy).not.toHaveBeenCalled();

    fixture.componentInstance.askBulkDelete();
    fixture.componentInstance.confirmBulkDelete();
    expect(spy).toHaveBeenCalled();
  });

  it('marks a row flagged by an all-or-nothing failure (US-12 AC-4 / FR-009)', () => {
    const el = loadTree();
    const selection = TestBed.inject(SelectionStore);

    selection.toggle('d2');
    selection.bulkDelete();
    controller.expectOne('/api/documents/bulk-delete').flush(
      { code: 'document.bulk_validation_failed', failures: [{ id: 'd2', code: 'document.read_only' }] },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    fixture.detectChanges();

    expect(el.querySelector('.tree__row--failed')).toBeTruthy();
  });

  it('deletes a document leaf via DELETE /api/documents after confirming (US-08)', () => {
    loadTree();

    // root.pdf (id d2) is a top-level document leaf.
    fixture.componentInstance.askDeleteDocument('d2');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Usunąć dokument');

    fixture.componentInstance.confirmDeleteDocument('d2');

    // DocumentActionsStore.delete → DELETE then refresh tree + quota.
    controller.expectOne((r) => r.method === 'DELETE' && r.url === '/api/documents/d2').flush(null, { status: 204, statusText: 'No Content' });
    controller.expectOne('/api/tree').flush({ folders, documents });
    controller.expectOne('/api/quota').flush({});
    expect(fixture.componentInstance.confirmingDeleteDocumentId()).toBeNull();
  });
});
