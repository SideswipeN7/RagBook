import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiKeyStore } from '../core/api-key.store';
import { ChatExchange, ChatStore } from '../core/chat.store';
import { Chat } from './chat';

class FakeChatStore {
  readonly thread = signal<readonly ChatExchange[]>([]);
  readonly isStreaming = signal(false);
  readonly ask = jasmine.createSpy('ask');
  readonly stop = jasmine.createSpy('stop');
  readonly retry = jasmine.createSpy('retry');
}

function exchange(overrides: Partial<ChatExchange>): ChatExchange {
  return {
    id: 'x',
    question: 'q',
    scope: { type: 'all', label: 'Wszystkie dokumenty' },
    status: 'complete',
    answer: '',
    sources: [],
    groundsFound: true,
    errorMessage: null,
    ...overrides,
  };
}

describe('Chat', () => {
  let fixture: ComponentFixture<Chat>;
  let store: FakeChatStore;
  let apiKey: ApiKeyStore;

  beforeEach(() => {
    store = new FakeChatStore();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), { provide: ChatStore, useValue: store }],
    });
    apiKey = TestBed.inject(ApiKeyStore);
    apiKey.status.set('active'); // unlocked by default
    fixture = TestBed.createComponent(Chat);
    fixture.detectChanges();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('sends the question with the selected scope', () => {
    fixture.componentInstance.onScope({ type: 'folder', targetId: 'f1', label: 'Umowy' });
    const input = (fixture.nativeElement as HTMLElement).querySelector('textarea') as HTMLTextAreaElement;
    input.value = 'jaki okres?';

    fixture.componentInstance.send(input);

    expect(store.ask).toHaveBeenCalledWith('jaki okres?', jasmine.objectContaining({ type: 'folder', targetId: 'f1' }));
    expect(input.value).toBe('');
  });

  it('shows Stop while streaming and calls stop()', () => {
    store.isStreaming.set(true);
    fixture.detectChanges();

    const stop = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes('Zatrzymaj'));
    expect(stop).toBeTruthy();
    stop!.click();
    expect(store.stop).toHaveBeenCalled();
  });

  it('locks the composer with a settings hint when no key', () => {
    apiKey.status.set('none');
    fixture.detectChanges();

    expect(text()).toContain('Skonfiguruj klucz API');
    expect((fixture.nativeElement as HTMLElement).querySelector('textarea')).toBeNull();
  });

  it('shows a neutral no-basis note when grounds not found', () => {
    store.thread.set([exchange({ status: 'complete', groundsFound: false, answer: '' })]);
    fixture.detectChanges();

    expect(text()).toContain('Brak podstaw w wybranym zakresie');
  });

  it('shows an error message with a Try again action', () => {
    store.thread.set([exchange({ status: 'error', errorMessage: 'Usługa AI jest chwilowo niedostępna. Spróbuj ponownie.' })]);
    fixture.detectChanges();

    expect(text()).toContain('niedostępna');
    const retry = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes('Spróbuj ponownie'));
    expect(retry).toBeTruthy();
    retry!.click();
    expect(store.retry).toHaveBeenCalled();
  });
});
