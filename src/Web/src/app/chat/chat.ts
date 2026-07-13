import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ApiKeyStore } from '../core/api-key.store';
import { ChatExchange, ChatScopeSelection, ChatStore } from '../core/chat.store';
import { ScopeSelector } from './scope-selector/scope-selector';
import { shouldStickToBottom } from './scroll-stick';

/**
 * The chat surface (US-15): a multi-turn thread of exchanges streamed from US-14's SSE via {@link ChatStore},
 * a scope selector + active chip, a question input (locked when no key — US-02), a Stop control while
 * streaming, error + Try-again, and a neutral no-basis note. Auto-scrolls to follow new text, detaching when
 * the user scrolls up. Standalone, OnPush, signals, design tokens.
 */
@Component({
  selector: 'app-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScopeSelector],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat implements AfterViewChecked {
  private readonly store = inject(ChatStore);
  private readonly apiKey = inject(ApiKeyStore);

  private readonly threadEl = viewChild<ElementRef<HTMLElement>>('threadEl');

  readonly thread = this.store.thread;
  readonly isStreaming = this.store.isStreaming;
  readonly chatLocked = this.apiKey.chatLocked;
  readonly scope = signal<ChatScopeSelection>({ type: 'all', label: 'Wszystkie dokumenty' });

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
    if (question.length === 0 || this.chatLocked()) {
      return;
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
