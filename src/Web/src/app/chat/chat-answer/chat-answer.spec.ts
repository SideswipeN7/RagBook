import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ChatExchange, Source } from '../../core/chat.store';
import { ChatAnswer } from './chat-answer';

function source(number: number, overrides: Partial<Source> = {}): Source {
  return {
    number,
    documentId: `doc-${number}`,
    fileName: `plik-${number}.pdf`,
    pageNumber: number,
    text: `Pełny tekst fragmentu numer ${number}.`,
    chunkId: `chunk-${number}`,
    ...overrides,
  };
}

function exchange(overrides: Partial<ChatExchange>): ChatExchange {
  return {
    id: 'x',
    question: 'q',
    scope: { type: 'all', label: 'Wszystkie' },
    status: 'complete',
    answer: '',
    sources: [],
    groundsFound: true,
    errorMessage: null,
    ...overrides,
  };
}

describe('ChatAnswer', () => {
  let fixture: ComponentFixture<ChatAnswer>;

  function render(ex: ChatExchange): HTMLElement {
    fixture = TestBed.createComponent(ChatAnswer);
    fixture.componentRef.setInput('exchange', ex);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function text(el: HTMLElement): string {
    return el.textContent ?? '';
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  it('renders [n] as a clickable citation and opens a preview with the full chunk text', () => {
    const el = render(exchange({ answer: 'Okres wynosi trzy miesiące [1].', sources: [source(1)] }));

    const cite = Array.from(el.querySelectorAll('button.cite')).find((b) => b.textContent?.includes('[1]'));
    expect(cite).toBeTruthy();

    cite!.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();

    const preview = el.querySelector('.preview') as HTMLElement;
    expect(preview).toBeTruthy();
    expect(text(preview)).toContain('Pełny tekst fragmentu numer 1.');
  });

  it('separates used sources from the collapsed "pozostałe przeszukane fragmenty"', () => {
    const el = render(exchange({ answer: 'Zgodnie z umową [1] tak jest.', sources: [source(1), source(2)] }));

    const used = el.querySelector('ul[aria-label="Użyte źródła"]') as HTMLElement;
    expect(text(used)).toContain('plik-1.pdf');
    expect(text(used)).not.toContain('plik-2.pdf');

    const details = el.querySelector('details.sources__more') as HTMLElement;
    expect(text(details)).toContain('Pozostałe przeszukane fragmenty (1)');
    expect(text(details)).toContain('plik-2.pdf');
  });

  it('shows all sources with a note when the model cited nothing (edge case)', () => {
    const el = render(exchange({ answer: 'Treściwa odpowiedź bez znaczników.', sources: [source(1), source(2)] }));

    expect(el.querySelector('ul[aria-label="Użyte źródła"]')).toBeNull();
    expect(text(el)).toContain('Model nie wskazał źródeł');
    expect(text(el)).toContain('plik-1.pdf');
    expect(text(el)).toContain('plik-2.pdf');
  });

  it('leaves an out-of-range marker as plain text, not a link (edge case)', () => {
    const el = render(exchange({ answer: 'Zdanie [9] koniec.', sources: [source(1)] }));

    expect(el.querySelector('button.cite')).toBeNull();
    expect(text(el)).toContain('[9]');
  });

  it('renders no source list for a no-basis answer (AC-5)', () => {
    const el = render(exchange({ status: 'complete', groundsFound: false, answer: '', sources: [] }));

    expect(el.querySelector('.sources')).toBeNull();
    expect(el.querySelector('button.cite')).toBeNull();
  });

  it('previews the captured chunk text so it survives document deletion (AC-4)', () => {
    // The source metadata is captured on the exchange — the preview never re-fetches the chunk.
    const el = render(exchange({ answer: 'Fakt [1].', sources: [source(1, { text: 'Zachowany tekst usuniętego dokumentu.' })] }));

    (el.querySelector('button.cite') as HTMLButtonElement).dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();

    expect(text(el.querySelector('.preview') as HTMLElement)).toContain('Zachowany tekst usuniętego dokumentu.');
  });
});
