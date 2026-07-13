import { Injectable, computed, signal } from '@angular/core';
import { SseParser } from './sse-parser';

/** The scope a question searches, mirroring US-13/14 (`all` / `folder` / `document`). */
export interface ChatScopeSelection {
  readonly type: 'all' | 'folder' | 'document';
  readonly targetId?: string;
  readonly label: string;
}

/** A grounding source from the `sources` event (US-14 `SourceDto`); rendered plainly (clickable in US-16). */
export interface Source {
  readonly number: number;
  readonly documentId: string;
  readonly fileName: string;
  readonly pageNumber: number | null;
}

/** One question/answer exchange in the in-memory thread (US-15). */
export interface ChatExchange {
  readonly id: string;
  readonly question: string;
  readonly scope: ChatScopeSelection;
  readonly status: 'streaming' | 'complete' | 'interrupted' | 'error';
  readonly answer: string;
  readonly sources: readonly Source[];
  readonly groundsFound: boolean;
  readonly errorMessage: string | null;
}

/** Stable error codes → Polish user messages (US-19-consistent). */
const ERROR_MESSAGES: Record<string, string> = {
  'settings.api_key_missing': 'Skonfiguruj klucz API, aby zadać pytanie.',
  'settings.invalid_api_key': 'Klucz API został odrzucony — sprawdź go w ustawieniach.',
  'chat.provider_rate_limited': 'Zbyt wiele zapytań — spróbuj ponownie za chwilę.',
  'chat.provider_unavailable': 'Usługa AI jest chwilowo niedostępna. Spróbuj ponownie.',
  'chat.scope_not_found': 'Wybrany zakres już nie istnieje — przełącz na „Wszystkie".',
  'chat.invalid_question': 'Pytanie jest puste lub zbyt długie.',
};
const GENERIC_ERROR = 'Coś poszło nie tak podczas generowania. Spróbuj ponownie.';

/**
 * Consumes US-14's streaming ask (`POST /api/chat/ask`, events `sources`/`token`/`done`/`error`) via a
 * streaming **fetch** + {@link SseParser} (US-15). Holds a multi-turn in-memory thread; `ask` appends tokens
 * incrementally, `stop` aborts the active stream (the server cancels generation on disconnect). One active
 * generation at a time — a new ask aborts the previous.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  readonly thread = signal<readonly ChatExchange[]>([]);

  /** True while an exchange is streaming. */
  readonly isStreaming = computed(() => this.thread().some((exchange) => exchange.status === 'streaming'));

  private controller: AbortController | null = null;

  /** Asks a question in the given scope; aborts any in-flight stream first (one active). */
  async ask(question: string, scope: ChatScopeSelection): Promise<void> {
    this.abortActive();

    const id = crypto.randomUUID();
    this.thread.update((list) => [
      ...list,
      { id, question, scope, status: 'streaming', answer: '', sources: [], groundsFound: true, errorMessage: null },
    ]);

    const controller = new AbortController();
    this.controller = controller;

    try {
      const response = await fetch('/api/chat/ask', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, scope: { type: scope.type, targetId: scope.targetId ?? null } }),
        signal: controller.signal,
      });

      if (!response.ok || !response.body) {
        this.patch(id, { status: 'error', errorMessage: this.message(await this.readCode(response)) });

        return;
      }

      await this.consume(id, response.body);
    } catch {
      this.patch(id, controller.signal.aborted ? { status: 'interrupted' } : { status: 'error', errorMessage: GENERIC_ERROR });
    } finally {
      if (this.controller === controller) {
        this.controller = null;
      }
    }
  }

  /** Stops the active stream; the partial answer stays, marked interrupted. */
  stop(): void {
    this.controller?.abort();
  }

  /** Re-runs a failed/interrupted exchange's question in the same scope. */
  retry(exchange: ChatExchange): void {
    void this.ask(exchange.question, exchange.scope);
  }

  private async consume(id: string, body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    const parser = new SseParser();
    let completed = false;

    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }

      for (const event of parser.push(decoder.decode(value, { stream: true }))) {
        if (event.event === 'sources') {
          this.patch(id, { sources: JSON.parse(event.data) as Source[] });
        } else if (event.event === 'token') {
          const { text } = JSON.parse(event.data) as { text: string };
          this.appendToken(id, text);
        } else if (event.event === 'done') {
          completed = true;
          const { groundsFound } = JSON.parse(event.data) as { groundsFound: boolean };
          this.patch(id, { status: 'complete', groundsFound });
        } else if (event.event === 'error') {
          const { code } = JSON.parse(event.data) as { code: string };
          this.patch(id, { status: 'error', errorMessage: this.message(code) });

          return;
        }
      }
    }

    if (!completed) {
      // The stream ended without a `done` — a truncation, not a clean answer.
      this.patch(id, { status: 'error', errorMessage: GENERIC_ERROR });
    }
  }

  private appendToken(id: string, text: string): void {
    this.thread.update((list) =>
      list.map((exchange) => (exchange.id === id ? { ...exchange, answer: exchange.answer + text } : exchange)),
    );
  }

  private patch(id: string, changes: Partial<ChatExchange>): void {
    this.thread.update((list) => list.map((exchange) => (exchange.id === id ? { ...exchange, ...changes } : exchange)));
  }

  private abortActive(): void {
    this.controller?.abort();
    this.controller = null;
  }

  private async readCode(response: Response): Promise<string | undefined> {
    try {
      const body = (await response.json()) as { code?: string };

      return body?.code;
    } catch {
      return undefined;
    }
  }

  private message(code: string | undefined): string {
    return (code && ERROR_MESSAGES[code]) || GENERIC_ERROR;
  }
}
