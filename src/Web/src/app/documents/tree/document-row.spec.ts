import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentNode } from '../../core/tree.store';
import { DocumentRow } from './document-row';

function node(overrides: Partial<DocumentNode> = {}): DocumentNode {
  return {
    kind: 'document',
    id: 'd1',
    folderId: null,
    fileName: 'umowa.pdf',
    contentType: 'application/pdf',
    sizeBytes: 12_300_000,
    status: 'Ready',
    chunkCount: 8,
    uploadedAt: '2026-07-11T10:00:00Z',
    failureReason: null,
    displaySize: '12.3 MB',
    displayFailureReason: 'Przetwarzanie nie powiodło się.',
    ...overrides,
  };
}

describe('DocumentRow', () => {
  let fixture: ComponentFixture<DocumentRow>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(DocumentRow);
  });

  function render(n: DocumentNode): HTMLElement {
    fixture.componentRef.setInput('node', n);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('shows name, size and chunk count for a ready document', () => {
    const el = render(node());
    const text = el.textContent ?? '';

    expect(text).toContain('umowa.pdf');
    expect(text).toContain('12.3 MB');
    expect(text).toContain('8 fragm.');
    expect(el.querySelector('.row__badge--ready')).not.toBeNull();
  });

  it('shows a spinner and no chunk count while processing', () => {
    const el = render(node({ status: 'Processing', chunkCount: 0 }));

    expect(el.querySelector('.row__spinner')).not.toBeNull();
    expect(el.textContent).not.toContain('fragm.');
  });

  it('shows an error badge with the failure reason on hover', () => {
    const el = render(node({ status: 'Failed', displayFailureReason: 'Encrypted PDF' }));
    const badge = el.querySelector('.row__badge--failed');

    expect(badge).not.toBeNull();
    expect(badge?.getAttribute('title')).toBe('Encrypted PDF');
  });

  it('truncates a long name with the full name in a title tooltip', () => {
    const long = 'a-really-really-really-long-document-name-that-overflows.pdf';
    const el = render(node({ fileName: long }));
    const name = el.querySelector('.row__name');

    expect(name?.getAttribute('title')).toBe(long);
  });
});
