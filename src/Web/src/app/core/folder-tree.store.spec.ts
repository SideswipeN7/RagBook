import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FolderNode, FolderTreeStore, buildTree } from './folder-tree.store';

describe('FolderTreeStore', () => {
  let store: FolderTreeStore;
  let controller: HttpTestingController;

  const nodes: FolderNode[] = [
    { id: 'a', parentId: null, name: 'Umowy', depth: 1 },
    { id: 'b', parentId: 'a', name: '2026', depth: 2 },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(FolderTreeStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should load folders from GET /api/folders and compose the tree', () => {
    store.refresh();

    controller.expectOne('/api/folders').flush(nodes);

    expect(store.nodes().length).toBe(2);
    const tree = store.tree();
    expect(tree.length).toBe(1);
    expect(tree[0].name).toBe('Umowy');
    expect(tree[0].children[0].name).toBe('2026');
  });

  it('should POST a create then refresh the list', () => {
    store.create('Umowy', null).subscribe();

    controller.expectOne((req) => req.method === 'POST' && req.url === '/api/folders').flush({ id: 'a' });
    controller.expectOne('/api/folders').flush(nodes);

    expect(store.nodes().length).toBe(2);
  });

  it('should PUT a rename then refresh', () => {
    store.rename('a', 'Umowy 2026').subscribe();

    controller.expectOne((req) => req.method === 'PUT' && req.url === '/api/folders/a/name').flush(null);
    controller.expectOne('/api/folders').flush(nodes);

    expect(store.nodes().length).toBe(2);
  });

  it('should DELETE then refresh', () => {
    store.remove('b').subscribe();

    controller.expectOne((req) => req.method === 'DELETE' && req.url === '/api/folders/b').flush(null);
    controller.expectOne('/api/folders').flush([nodes[0]]);

    expect(store.nodes().length).toBe(1);
  });

  it('buildTree nests children under their parent and keeps roots ordered', () => {
    const tree = buildTree([
      { id: 'a', parentId: null, name: 'Ananas', depth: 1 },
      { id: 'b', parentId: null, name: 'banan', depth: 1 },
      { id: 'c', parentId: 'a', name: 'child', depth: 2 },
    ]);

    expect(tree.map((n) => n.name)).toEqual(['Ananas', 'banan']);
    expect(tree[0].children.map((n) => n.name)).toEqual(['child']);
  });
});
