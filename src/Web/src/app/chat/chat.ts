import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ApiKeyStore } from '../core/api-key.store';
import { ChatExchange, ChatScopeSelection, ChatStore } from '../core/chat.store';
import { ConversationsStore } from '../core/conversations.store';
import { ChatAnswer } from './chat-answer/chat-answer';
import { ConversationList } from './conversation-list/conversation-list';
import { ScopeSelector } from './scope-selector/scope-selector';
import { shouldStickToBottom } from './scroll-stick';

/**
 * The chat surface (US-15/18): a per-session conversation list ("Nowa rozmowa" + switching + delete), and the
 * active conversation's thread streamed from US-14's SSE via {@link ChatStore}. A scope selector + active chip, a
 * question input (locked when no key — US-02), a Stop control while streaming, error + Try-again, and neutral
 * no-answer rendering. Conversations persist (US-18); reopening restores messages with states + citations.
 */
@Component({
  selector: 'app-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScopeSelector, ChatAnswer, ConversationList],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements AfterViewChecked, OnInit {
  private readonly store = inject(ChatStore);
  private readonly conversationsStore = inject(ConversationsStore);
  private readonly apiKey = inject(ApiKeyStore);

  private readonly threadEl = viewChild<ElementRef<HTMLElement>>('threadEl');

  readonly thread = this.store.thread;
  readonly isStreaming = this.store.isStreaming;
  readonly chatLocked = this.apiKey.chatLocked;
  readonly conversations = this.conversationsStore.conversations;
  readonly activeConversationId = this.conversationsStore.activeId;
  readonly scope = signal<ChatScopeSelection>({ type: 'all', label: 'Wszystkie dokumenty' });

  private stick = true;

  async ngOnInit(): Promise<void> {
    const list = await this.conversationsStore.list();
    if (list.length > 0) {
      this.conversationsStore.setActive(list[0].id);
      await this.store.load(list[0].id);
    } else {
      const created = await this.conversationsStore.create();
      this.store.reset(created.id);
    }
  }

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

  async onSelectConversation(id: string): Promise<void> {
    this.conversationsStore.setActive(id);
    this.stick = true;
    await this.store.load(id);
  }

  async onNewConversation(): Promise<void> {
    const created = await this.conversationsStore.create();
    this.stick = true;
    this.store.reset(created.id);
  }

  async onDeleteConversation(id: string): Promise<void> {
    const wasActive = this.conversationsStore.activeId() === id;
    await this.conversationsStore.remove(id);
    if (!wasActive) {
      return;
    }

    const remaining = this.conversationsStore.conversations();
    if (remaining.length > 0) {
      await this.onSelectConversation(remaining[0].id);
    } else {
      await this.onNewConversation();
    }
  }

  send(input: HTMLTextAreaElement): void {
    const question = input.value.trim();
    if (question.length === 0 || this.chatLocked()) {
      return;
    }

    // Title the conversation from its first question (optimistic; the server does the same).
    const activeId = this.activeConversationId();
    if (activeId !== null && this.thread().length === 0) {
      this.conversationsStore.patchTitle(activeId, question.slice(0, 60));
    }

    this.stick = true;
    void this.store.ask(question, this.scope());
    input.value = '';
  }

  stop(): void {
    this.store.stop();
  }

  retry(exchange: ChatExchange): void {
    this.store.retry(exchange);
  }
}
