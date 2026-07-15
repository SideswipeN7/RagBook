import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ChatScopeSelection, ChatStore } from './chat.store';

const ALL: ChatScopeSelection = { type: 'all', label: 'Wszystkie dokumenty' };

function bytes(chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();

  return new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((chunk) => controller.enqueue(encoder.encode(chunk)));
      controller.close();
    },
  });
}

function stubStream(chunks: string[]): jasmine.Spy {
  return spyOn(window, 'fetch').and.resolveTo(new Response(bytes(chunks), { status: 200 }));
}

function stubProblem(code: string, status = 400): void {
  spyOn(window, 'fetch').and.resolveTo(new Response(JSON.stringify({ code }), { status }));
}

/** A stream that stays open (never closes) and errors when its request is aborted — to drive stop(). */
function stubPending(chunks: string[]): void {
  const encoder = new TextEncoder();
  spyOn(window, 'fetch').and.callFake((_url, options) =>
    Promise.resolve(
      new Response(
        new ReadableStream<Uint8Array>({
          start(controller) {
            chunks.forEach((chunk) => controller.enqueue(encoder.encode(chunk)));
            (options!.signal as AbortSignal).addEventListener('abort', () =>
              controller.error(new DOMException('aborted', 'AbortError')),
            );
          },
        }),
        { status: 200 },
      ),
    ),
  );
}

async function poll(condition: () => boolean, ms = 1500): Promise<void> {
  const end = Date.now() + ms;
  while (!condition() && Date.now() < end) {
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  if (!condition()) {
    throw new Error('poll timed out');
  }
}

const SOURCES = 'event: sources\ndata: [{"number":1,"documentId":"d1","fileName":"a.pdf","pageNumber":2}]\n\n';

describe('ChatStore', () => {
  let store: ChatStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ChatStore],
    });
    store = TestBed.inject(ChatStore);
    store.reset('c1'); // US-18 — an ask targets the active conversation
  });

  it('loads a persisted conversation into the thread with states and citations', async () => {
    const detail = {
      id: 'c9',
      scopeType: 'all',
      scopeTargetId: null,
      messages: [
        { id: 'm1', role: 'user', content: 'pytanie', state: null, sources: null, createdAt: '2026-01-01T00:00:00Z' },
        {
          id: 'm2',
          role: 'assistant',
          content: 'odpowiedź [1]',
          state: 'answered',
          sources: [{ number: 1, documentId: 'd1', fileName: 'a.pdf', pageNumber: 2, text: 'tekst', chunkId: 'ch1' }],
          createdAt: '2026-01-01T00:00:01Z',
        },
      ],
    };

    const loading = store.load('c9');
    TestBed.inject(HttpTestingController).expectOne('/api/conversations/c9').flush(detail);
    await loading;

    const thread = store.thread();
    expect(thread.length).toBe(1);
    expect(thread[0].question).toBe('pytanie');
    expect(thread[0].answer).toBe('odpowiedź [1]');
    expect(thread[0].status).toBe('complete');
    expect(thread[0].sources[0].chunkId).toBe('ch1');
    expect(store.activeConversationId()).toBe('c9');
  });

  it('maps a persisted no_answer message to the no_answer status', async () => {
    const detail = {
      id: 'c9',
      scopeType: 'all',
      scopeTargetId: null,
      messages: [
        { id: 'm1', role: 'user', content: 'pytanie', state: null, sources: null, createdAt: '2026-01-01T00:00:00Z' },
        { id: 'm2', role: 'assistant', content: '', state: 'no_answer', sources: null, createdAt: '2026-01-01T00:00:01Z' },
      ],
    };

    const loading = store.load('c9');
    TestBed.inject(HttpTestingController).expectOne('/api/conversations/c9').flush(detail);
    await loading;

    expect(store.thread()[0].status).toBe('no_answer');
  });

  it('streams sources then tokens then completes, sending the session cookie', async () => {
    const spy = stubStream([
      SOURCES,
      'event: token\ndata: {"text":"Ok"}\n\n',
      'event: token\ndata: {"text":"res [1]."}\n\n',
      'event: done\ndata: {"groundsFound":true,"state":"answered"}\n\n',
    ]);

    await store.ask('okres?', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('complete');
    expect(exchange.answer).toBe('Okres [1].');
    expect(exchange.sources.length).toBe(1); // set from the `sources` event (before the answer text — A3)
    expect(exchange.groundsFound).toBeTrue();
    expect((spy.calls.mostRecent().args[1] as RequestInit).credentials).toBe('same-origin'); // C1
  });

  it('maps a no_answer done (deterministic cut-off) to the no_answer status', async () => {
    stubStream(['event: done\ndata: {"groundsFound":false,"state":"no_answer"}\n\n']);

    await store.ask('nieznane', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('no_answer'); // US-17 — a distinct, neutral state, not 'complete'
    expect(exchange.groundsFound).toBeFalse();
    expect(exchange.answer).toBe('');
  });

  it('maps a no_answer done after streamed tokens (prompt refusal) to the no_answer status', async () => {
    stubStream([
      SOURCES,
      'event: token\ndata: {"text":"Nie znalazlem..."}\n\n',
      'event: done\ndata: {"groundsFound":true,"state":"no_answer"}\n\n',
    ]);

    await store.ask('pytanie bez pokrycia', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('no_answer');
    expect(exchange.sources.length).toBe(1); // grounds existed → searched fragments available
  });

  it('maps a mid-stream error event to a message, keeping the partial answer', async () => {
    stubStream([
      SOURCES,
      'event: token\ndata: {"text":"Częściowa"}\n\n',
      'event: error\ndata: {"code":"chat.provider_unavailable"}\n\n',
    ]);

    await store.ask('q', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('error');
    expect(exchange.answer).toBe('Częściowa');
    expect(exchange.errorMessage).toContain('niedostępna');
  });

  it('maps a pre-stream ProblemDetails to its code message', async () => {
    stubProblem('chat.invalid_question', 400);

    await store.ask('', ALL);

    expect(store.thread()[0].status).toBe('error');
    expect(store.thread()[0].errorMessage).toContain('puste');
  });

  it('treats a stream that ends without done as an error', async () => {
    stubStream([SOURCES, 'event: token\ndata: {"text":"A"}\n\n']);

    await store.ask('q', ALL);

    expect(store.thread()[0].status).toBe('error');
  });

  it('stop() marks the exchange interrupted and keeps the partial answer', async () => {
    stubPending([SOURCES, 'event: token\ndata: {"text":"Zacz"}\n\n']);

    const pending = store.ask('q', ALL);
    await poll(() => store.thread()[0]?.answer === 'Zacz');
    store.stop();
    await pending;

    expect(store.thread()[0].status).toBe('interrupted');
    expect(store.thread()[0].answer).toBe('Zacz');
  });

  it('a second ask aborts the first (one active generation)', async () => {
    stubPending(['event: token\ndata: {"text":"A"}\n\n']);

    const first = store.ask('q1', ALL);
    await poll(() => store.thread().length === 1 && store.thread()[0].answer === 'A');
    const second = store.ask('q2', ALL);
    await poll(() => store.thread().length === 2 && store.thread()[0].status === 'interrupted');

    expect(store.thread()[0].status).toBe('interrupted');
    expect(store.thread()[1].status).toBe('streaming');

    store.stop();
    await Promise.allSettled([first, second]);
  });
});
