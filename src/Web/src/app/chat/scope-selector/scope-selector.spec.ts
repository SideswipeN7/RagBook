import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ChatScopeSelection } from '../../core/chat.store';
import { TreeDocumentDto, TreeStore } from '../../core/tree.store';
import { ScopeSelector } from './scope-selector';

function doc(id: string, fileName: string, status: 'Ready' | 'Processing'): TreeDocumentDto {
  return { id, folderId: null, fileName, contentType: 'application/pdf', sizeBytes: 1, status, chunkCount: 0, uploadedAt: '', failureReason: null };
}

describe('ScopeSelector', () => {
  let fixture: ComponentFixture<ScopeSelector>;
  let tree: TreeStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient()] });
    tree = TestBed.inject(TreeStore);
    tree.folders.set([{ id: 'f1', parentId: null, name: 'Umowy', depth: 1 }]);
    tree.documents.set([doc('d1', 'ready.pdf', 'Ready'), doc('d2', 'busy.pdf', 'Processing')]);
    fixture = TestBed.createComponent(ScopeSelector);
    fixture.detectChanges();
  });

  it('offers only ready documents (processing excluded)', () => {
    expect(fixture.componentInstance.readyDocuments().map((d) => d.id)).toEqual(['d1']);

    const options = (fixture.nativeElement as HTMLElement).querySelectorAll('option');
    const values = Array.from(options).map((o) => (o as HTMLOptionElement).value);
    expect(values).toContain('all');
    expect(values).toContain('folder:f1');
    expect(values).toContain('document:d1');
    expect(values).not.toContain('document:d2');
  });

  it('emits the chosen scope', () => {
    let emitted: ChatScopeSelection | undefined;
    fixture.componentInstance.scopeChange.subscribe((scope) => (emitted = scope));

    fixture.componentInstance.select('folder:f1');

    expect(emitted).toEqual({ type: 'folder', targetId: 'f1', label: 'Umowy' });
  });

  it('offers a demo option only when demo documents exist, and emits the demo scope (US-03)', () => {
    // No demo documents yet → no demo option.
    let values = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('option')).map((o) => (o as HTMLOptionElement).value);
    expect(values).not.toContain('demo');

    tree.demoDocuments.set([doc('demo1', 'demo.pdf', 'Ready')]);
    fixture.detectChanges();

    values = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('option')).map((o) => (o as HTMLOptionElement).value);
    expect(values).toContain('demo');

    let emitted: ChatScopeSelection | undefined;
    fixture.componentInstance.scopeChange.subscribe((scope) => (emitted = scope));
    fixture.componentInstance.select('demo');
    expect(emitted).toEqual({ type: 'demo', label: 'Dokumenty demo' });
  });
});
