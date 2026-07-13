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
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), ChatStore] });
    store = TestBed.inject(ChatStore);
  });

  it('streams sources then tokens then completes, sending the session cookie', async () => {
    const spy = stubStream([
      SOURCES,
      'event: token\ndata: {"text":"Ok"}\n\n',
      'event: token\ndata: {"text":"res [1]."}\n\n',
      'event: done\ndata: {"groundsFound":true}\n\n',
    ]);

    await store.ask('okres?', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('complete');
    expect(exchange.answer).toBe('Okres [1].');
    expect(exchange.sources.length).toBe(1); // set from the `sources` event (before the answer text — A3)
    expect(exchange.groundsFound).toBeTrue();
    expect((spy.calls.mostRecent().args[1] as RequestInit).credentials).toBe('same-origin'); // C1
  });

  it('shows the no-basis outcome (groundsFound:false) with no answer text', async () => {
    stubStream(['event: done\ndata: {"groundsFound":false}\n\n']);

    await store.ask('nieznane', ALL);

    const exchange = store.thread()[0];
    expect(exchange.status).toBe('complete');
    expect(exchange.groundsFound).toBeFalse();
    expect(exchange.answer).toBe('');
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
