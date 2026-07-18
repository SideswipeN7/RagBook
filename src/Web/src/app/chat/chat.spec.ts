import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiKeyStore } from '../core/api-key.store';
import { ChatExchange, ChatStore } from '../core/chat.store';
import { ConversationSummary, ConversationsStore } from '../core/conversations.store';
import { DemoStore } from '../core/demo.store';
import { Chat } from './chat';

class FakeChatStore {
  readonly thread = signal<readonly ChatExchange[]>([]);
  readonly isStreaming = signal(false);
  readonly activeConversationId = signal<string | null>('c1');
  readonly ask = jasmine.createSpy('ask').and.resolveTo(undefined);
  readonly stop = jasmine.createSpy('stop');
  readonly retry = jasmine.createSpy('retry');
  readonly load = jasmine.createSpy('load').and.resolveTo(undefined);
  readonly reset = jasmine.createSpy('reset');
}

class FakeDemoStore {
  readonly asked = signal(0);
  readonly max = signal(10);
  readonly remaining = signal(10);
  readonly available = signal(true);
  readonly isExhausted = signal(false);
  readonly refresh = jasmine.createSpy('refresh');
  readonly noteAsked = jasmine.createSpy('noteAsked');
}

class FakeConversationsStore {
  readonly conversations = signal<readonly ConversationSummary[]>([]);
  readonly activeId = signal<string | null>('c1');
  readonly list = jasmine.createSpy('list').and.resolveTo([]);
  readonly create = jasmine.createSpy('create').and.resolveTo({ id: 'c1', title: '', scopeType: 'all', scopeTargetId: null, createdAt: '' });
  readonly setActive = jasmine.createSpy('setActive');
  readonly remove = jasmine.createSpy('remove').and.resolveTo(undefined);
  readonly patchTitle = jasmine.createSpy('patchTitle');
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
  let conversations: FakeConversationsStore;
  let apiKey: ApiKeyStore;

  beforeEach(() => {
    store = new FakeChatStore();
    conversations = new FakeConversationsStore();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: ChatStore, useValue: store },
        { provide: ConversationsStore, useValue: conversations },
        { provide: DemoStore, useValue: new FakeDemoStore() },
      ],
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

  it('shows the neutral no-answer view when the state is no_answer (US-17)', () => {
    store.thread.set([exchange({ status: 'no_answer', groundsFound: false, answer: '' })]);
    fixture.detectChanges();

    expect(text()).toContain('Nie znalazłem tego w dokumentach');
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
