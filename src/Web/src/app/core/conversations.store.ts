import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** A conversation in the session's list (US-18), from `GET /api/conversations`. */
export interface ConversationSummary {
  readonly id: string;
  readonly title: string;
  readonly scopeType: 'all' | 'folder' | 'document';
  readonly scopeTargetId: string | null;
  readonly createdAt: string;
}

/**
 * Signal-backed store of the session's persisted conversations (US-18): the list, the active id, and the
 * create/delete lifecycle. It owns the sidebar state; {@link ChatStore} owns the active conversation's thread.
 * Session isolation is entirely backend-managed (a cross-session id resolves to 404).
 */
@Injectable({ providedIn: 'root' })
export class ConversationsStore {
  private readonly http = inject(HttpClient);

  readonly conversations = signal<readonly ConversationSummary[]>([]);
  readonly activeId = signal<string | null>(null);

  /** Loads the session's conversations, most-recent first. */
  async list(): Promise<readonly ConversationSummary[]> {
    const list = await firstValueFrom(this.http.get<ConversationSummary[]>('/api/conversations'));
    this.conversations.set(list);

    return list;
  }

  /** Creates a new empty conversation (default scope "Wszystkie"), prepends it, and makes it active. */
  async create(): Promise<ConversationSummary> {
    const created = await firstValueFrom(
      this.http.post<ConversationSummary>('/api/conversations', { scope: { type: 'all', targetId: null } }),
    );
    this.conversations.update((list) => [created, ...list]);
    this.activeId.set(created.id);

    return created;
  }

  /** Marks a conversation active (selection). */
  setActive(id: string): void {
    this.activeId.set(id);
  }

  /** Deletes a conversation (and its messages, cascaded server-side); clears the active id if it was active. */
  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/conversations/${id}`));
    this.conversations.update((list) => list.filter((conversation) => conversation.id !== id));
    if (this.activeId() === id) {
      this.activeId.set(null);
    }
  }

  /** Refreshes the title of a conversation in the list (after its first question titles it). */
  patchTitle(id: string, title: string): void {
    this.conversations.update((list) =>
      list.map((conversation) => (conversation.id === id ? { ...conversation, title } : conversation)),
    );
  }
}
