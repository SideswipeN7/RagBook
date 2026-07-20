import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ApiKeyStore } from '../core/api-key.store';
import { AppConfigStore } from '../core/app-config.store';
import { ChatExchange, ChatScopeSelection, ChatStore } from '../core/chat.store';
import { ConversationsStore } from '../core/conversations.store';
import { DemoStore } from '../core/demo.store';
import { ChatAnswer } from './chat-answer/chat-answer';
import { ScopeSelector } from './scope-selector/scope-selector';
import { shouldStickToBottom } from './scroll-stick';

/**
 * The chat surface (US-15/18) — the active conversation's thread streamed from US-14's SSE via {@link ChatStore},
 * plus a scope selector + active chip, a question input (locked when no key — US-02, keyless in demo), a Stop
 * control while streaming, error + Try-again, and neutral no-answer rendering. The conversation **list** now lives
 * in the workspace shell (US-21); this component just renders/asks in whatever conversation is active (owned by
 * {@link ConversationsStore}). Standalone, OnPush.
 */
@Component({
  selector: 'app-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScopeSelector, ChatAnswer],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements AfterViewChecked {
  private readonly store = inject(ChatStore);
  private readonly conversationsStore = inject(ConversationsStore);
  private readonly apiKey = inject(ApiKeyStore);
  private readonly appConfig = inject(AppConfigStore);
  private readonly demoStore = inject(DemoStore);

  private readonly threadEl = viewChild<ElementRef<HTMLElement>>('threadEl');

  readonly thread = this.store.thread;
  readonly isStreaming = this.store.isStreaming;
  readonly chatLocked = this.apiKey.chatLocked;
  readonly activeConversationId = this.conversationsStore.activeId;
  readonly scope = signal<ChatScopeSelection>({ type: 'all', label: 'Wszystkie dokumenty' });

  // US-03 — demo mode: a keyless scope with its own per-session counter + BYOK nudge.
  readonly isDemoScope = computed(() => this.scope().type === 'demo');
  // US-22 — keyless generation (application key or local CLI fallback) unlocks the composer without a BYOK key.
  readonly keylessGeneration = this.appConfig.keylessGeneration;
  /** The composer is locked only when no key AND not in demo scope AND the server can't generate keyless. */
  readonly locked = computed(
    () => this.chatLocked() && !this.isDemoScope() && !this.keylessGeneration(),
  );
  readonly demoRemaining = this.demoStore.remaining;
  readonly demoMax = this.demoStore.max;
  readonly demoExhausted = this.demoStore.isExhausted;

  // US-20 — the evaluator's one-minute path: ready demo questions shown in the empty chat, matching the seeded
  // demo documents (a sample lease + the technical doc). Clicking one asks it in the demo scope, keyless.
  readonly suggestedQuestions: readonly string[] = [
    'Jaki jest okres wypowiedzenia w umowie?',
    'Ile wynosi kaucja i kiedy jest zwracana?',
    'Jak działa wyszukiwanie dokumentów w RagBook?',
  ];

  private stick = true;

  ngAfterViewChecked(): void {
    if (this.stick) {
      const element = this.threadEl()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    }
  }

  onScroll(): void {
    const element = this.threadEl()?.nativeElement;
    if (element) {
      this.stick = shouldStickToBottom(element.scrollTop, element.clientHeight, element.scrollHeight);
    }
  }

  onScope(scope: ChatScopeSelection): void {
    this.scope.set(scope);
  }

  send(input: HTMLTextAreaElement): void {
    const question = input.value.trim();
    if (question.length === 0 || this.locked()) {
      return;
    }

    // Title the conversation from its first question (optimistic; the server does the same).
    const activeId = this.activeConversationId();
    if (activeId !== null && this.thread().length === 0) {
      this.conversationsStore.patchTitle(activeId, question.slice(0, 60));
    }

    this.stick = true;
    const isDemo = this.isDemoScope();
    void this.store.ask(question, this.scope()).then(() => {
      if (isDemo) {
        this.demoStore.refresh(); // reconcile the "X / N pytań demo" counter after the ask
      }
    });
    input.value = '';
  }

  /** Asks a ready demo question in the demo scope (US-20 AC-3) — the keyless one-click evaluator path. */
  askSuggested(question: string): void {
    this.scope.set({ type: 'demo', label: 'Dokumenty demo' });
    this.stick = true;
    void this.store.ask(question, this.scope()).then(() => this.demoStore.refresh());
  }

  stop(): void {
    this.store.stop();
  }

  retry(exchange: ChatExchange): void {
    this.store.retry(exchange);
  }
}
