import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FolderNode } from '../core/folder-tree.store';
import { FolderTree } from './folder-tree';

describe('FolderTree', () => {
  let fixture: ComponentFixture<FolderTree>;
  let controller: HttpTestingController;

  const nodes: FolderNode[] = [
    { id: 'a', parentId: null, name: 'Umowy', depth: 1 },
    { id: 'b', parentId: 'a', name: '2026', depth: 2 },
    { id: 'c', parentId: 'b', name: 'Q1', depth: 3 },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    fixture = TestBed.createComponent(FolderTree);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function loadTree(): void {
    fixture.detectChanges(); // ngOnInit → refresh
    controller.expectOne('/api/folders').flush(nodes);
    fixture.detectChanges();
  }

  it('should render the nested folder names', () => {
    loadTree();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Umowy');
    expect(text).toContain('2026');
    expect(text).toContain('Q1');
  });

  it('should not offer "Nowy folder" on a node already at the maximum depth', () => {
    loadTree();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    const newFolderButtons = buttons.filter((b) => b.textContent?.trim() === 'Nowy folder');

    // One toolbar button (root) + one per node below max depth (Umowy@1, 2026@2) = 3; the depth-3 Q1 offers none.
    expect(newFolderButtons.length).toBe(3);
  });

  it('should POST a create when adding a root folder', () => {
    loadTree();

    fixture.componentInstance.startCreate(null);
    fixture.componentInstance.submitCreate('Nowy');

    const post = controller.expectOne((req) => req.method === 'POST' && req.url === '/api/folders');
    expect(post.request.body).toEqual({ name: 'Nowy', parentId: null });
    post.flush({ id: 'x' });
    controller.expectOne('/api/folders').flush(nodes);
  });

  it('should surface the duplicate-name message on a 409', () => {
    loadTree();

    fixture.componentInstance.startCreate(null);
    fixture.componentInstance.submitCreate('Umowy');

    controller
      .expectOne((req) => req.method === 'POST' && req.url === '/api/folders')
      .flush({ code: 'folder.duplicate_name' }, { status: 409, statusText: 'Conflict' });

    expect(fixture.componentInstance.errorMessage()).toContain('już istnieje');
  });
});
