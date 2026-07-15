import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SseParser } from './sse-parser';

/** The scope a question searches, mirroring US-13/14 (`all` / `folder` / `document`). */
export interface ChatScopeSelection {
  readonly type: 'all' | 'folder' | 'document';
  readonly targetId?: string;
  readonly label: string;
}

/** A grounding source from the `sources` event (US-14 `SourceDto`); its `[n]` is clickable in US-16. */
export interface Source {
  readonly number: number;
  readonly documentId: string;
  readonly fileName: string;
  readonly pageNumber: number | null;
  /** Full chunk text — powers the citation preview; captured in the exchange so it survives document deletion (US-16 AC-4). */
  readonly text: string;
  /** The chunk id — the deterministic `[n]`→chunk mapping key (US-16). */
  readonly chunkId: string;
}

/** One question/answer exchange in the in-memory thread (US-15). */
export interface ChatExchange {
  readonly id: string;
  readonly question: string;
  readonly scope: ChatScopeSelection;
  readonly status: 'streaming' | 'complete' | 'no_answer' | 'interrupted' | 'error';
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

/** A persisted message from `GET /api/conversations/{id}` (US-18). */
interface MessageResponse {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  readonly content: string;
  readonly state: 'answered' | 'no_answer' | 'interrupted' | null;
  readonly sources: Source[] | null;
  readonly createdAt: string;
}

/** A conversation with its messages (US-18). */
interface ConversationDetailResponse {
  readonly id: string;
  readonly scopeType: 'all' | 'folder' | 'document';
  readonly scopeTargetId: string | null;
  readonly messages: MessageResponse[];
}

/**
 * Consumes US-14's streaming ask (`POST /api/chat/ask`, events `sources`/`token`/`done`/`error`) via a
 * streaming **fetch** + {@link SseParser} (US-15). Backed by a persisted conversation (US-18): {@link load}
 * hydrates the thread from stored messages (with their states + citations), {@link reset} starts a fresh
 * conversation, and `ask` carries the active `conversationId`. One active generation at a time.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly http = inject(HttpClient);

  readonly thread = signal<readonly ChatExchange[]>([]);

  /** The conversation the thread belongs to (US-18); asks target it. */
  readonly activeConversationId = signal<string | null>(null);

  /** True while an exchange is streaming. */
  readonly isStreaming = computed(() => this.thread().some((exchange) => exchange.status === 'streaming'));

  private controller: AbortController | null = null;

  /** Starts a fresh, empty conversation thread (US-18 "Nowa rozmowa"). */
  reset(conversationId: string): void {
    this.abortActive();
    this.activeConversationId.set(conversationId);
    this.thread.set([]);
  }

  /** Loads a persisted conversation's messages into the thread, preserving states + citations (US-18). */
  async load(conversationId: string): Promise<void> {
    this.abortActive();
    this.activeConversationId.set(conversationId);
    const detail = await firstValueFrom(this.http.get<ConversationDetailResponse>(`/api/conversations/${conversationId}`));
    this.thread.set(this.toThread(detail));
  }

  /** Asks a question in the active conversation; aborts any in-flight stream first (one active). */
  async ask(question: string, scope: ChatScopeSelection): Promise<void> {
    const conversationId = this.activeConversationId();
    if (conversationId === null) {
      return;
    }

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
        body: JSON.stringify({ conversationId, question, scope: { type: scope.type, targetId: scope.targetId ?? null } }),
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
          const { groundsFound, state } = JSON.parse(event.data) as { groundsFound: boolean; state?: 'answered' | 'no_answer' };
          // US-17: `no_answer` (deterministic cut-off OR prompt refusal) is a distinct, neutral message state.
          this.patch(id, { status: state === 'no_answer' ? 'no_answer' : 'complete', groundsFound });
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

  /** Maps persisted messages into thread exchanges, pairing each user question with its assistant answer. */
  private toThread(detail: ConversationDetailResponse): ChatExchange[] {
    const scope = this.scopeSelection(detail.scopeType, detail.scopeTargetId);
    const exchanges: ChatExchange[] = [];
    let pendingUser: MessageResponse | null = null;

    for (const message of detail.messages) {
      if (message.role === 'user') {
        pendingUser = message;
      } else if (message.role === 'assistant' && pendingUser !== null) {
        exchanges.push({
          id: message.id,
          question: pendingUser.content,
          scope,
          status: this.statusOf(message.state),
          answer: message.content,
          sources: message.sources ?? [],
          groundsFound: message.state !== 'no_answer',
          errorMessage: null,
        });
        pendingUser = null;
      }
    }

    if (pendingUser !== null) {
      exchanges.push({
        id: pendingUser.id,
        question: pendingUser.content,
        scope,
        status: 'complete',
        answer: '',
        sources: [],
        groundsFound: true,
        errorMessage: null,
      });
    }

    return exchanges;
  }

  private statusOf(state: MessageResponse['state']): ChatExchange['status'] {
    return state === 'no_answer' ? 'no_answer' : state === 'interrupted' ? 'interrupted' : 'complete';
  }

  private scopeSelection(type: ChatScopeSelection['type'], targetId: string | null): ChatScopeSelection {
    const label = type === 'all' ? 'Wszystkie dokumenty' : type === 'folder' ? 'Wybrany folder' : 'Wybrany dokument';

    return { type, targetId: targetId ?? undefined, label };
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
