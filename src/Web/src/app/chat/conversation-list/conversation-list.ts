import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { ConversationSummary } from '../../core/conversations.store';

/**
 * The conversation sidebar (US-18): the session's conversations, "Nowa rozmowa", switching, and delete behind an
 * inline design-system confirm (never `window.confirm`). Presentational — the parent owns the stores and reacts
 * to {@link select}/{@link create}/{@link remove}. Standalone, OnPush, signals, tokens.
 */
@Component({
  selector: 'app-conversation-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversation-list.html',
  styleUrl: './conversation-list.scss',
})
export class ConversationList {
  readonly conversations = input.required<readonly ConversationSummary[]>();
  readonly activeId = input<string | null>(null);

  readonly select = output<string>();
  readonly create = output<void>();
  readonly remove = output<string>();

  /** The conversation currently awaiting delete confirmation, or null. */
  readonly confirmingId = signal<string | null>(null);

  onSelect(id: string): void {
    this.select.emit(id);
  }

  onNew(): void {
    this.create.emit();
  }

  askDelete(id: string, event: Event): void {
    event.stopPropagation();
    this.confirmingId.set(id);
  }

  cancelDelete(event: Event): void {
    event.stopPropagation();
    this.confirmingId.set(null);
  }

  confirmDelete(id: string, event: Event): void {
    event.stopPropagation();
    this.confirmingId.set(null);
    this.remove.emit(id);
  }

  title(conversation: ConversationSummary): string {
    return conversation.title.length > 0 ? conversation.title : 'Nowa rozmowa';
  }
}
