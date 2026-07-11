import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FolderNode } from '../../core/tree.store';
import { DocumentTree } from './document-tree';

const folders = [
  { id: 'a', parentId: null, name: 'Umowy', depth: 1 },
];
const documents = [
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
});
