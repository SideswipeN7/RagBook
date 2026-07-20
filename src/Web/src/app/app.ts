import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { ChatStore } from './core/chat.store';
import { ConversationsStore } from './core/conversations.store';
import { DemoStore } from './core/demo.store';
import { DocumentStatusStore } from './core/document-status.store';
import { NotFoundNotifier } from './core/not-found-notifier';
import { QuotaStore } from './core/quota.store';
import { SessionService } from './core/session.service';
import { WorkspaceStore } from './core/workspace.store';
import { Chat } from './chat/chat';
import { ConversationList } from './chat/conversation-list/conversation-list';
import { DocumentTree } from './documents/tree/document-tree';
import { DocumentUpload } from './documents/upload/document-upload';
import { QuotaBar } from './documents/quota-bar/quota-bar';
import { OfflineBanner } from './shared/offline-banner/offline-banner';
import { Onboarding } from './workspace/onboarding/onboarding';
import { Studio } from './workspace/studio/studio';

/**
 * Root workspace shell (US-21). Config-first: shows onboarding until access is configured (a key or "continue in
 * demo"), then a 4-column grid — conversations (collapsible) | sources | chat | Studio — all driven by one shared
 * active-conversation id (owned by {@link ConversationsStore}). The shell orchestrates conversation
 * select/create/delete so the sources, chat, and Studio columns react to one selection. Standalone, OnPush, signals.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [QuotaBar, DocumentUpload, DocumentTree, Chat, OfflineBanner, ConversationList, Studio, Onboarding],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly session = inject(SessionService);
  private readonly notFound = inject(NotFoundNotifier);
  private readonly quota = inject(QuotaStore);
  private readonly documentStatus = inject(DocumentStatusStore);
  private readonly workspace = inject(WorkspaceStore);
  private readonly conversationsStore = inject(ConversationsStore);
  private readonly chat = inject(ChatStore);
  private readonly demo = inject(DemoStore);

  readonly state = this.session.state;
  readonly notFoundMessage = this.notFound.message;

  readonly configured = this.workspace.configured;
  readonly conversations = this.conversationsStore.conversations;
  readonly activeConversationId = this.conversationsStore.activeId;
  readonly conversationsCollapsed = this.workspace.conversationsCollapsed;
  readonly sourcesCollapsed = this.workspace.sourcesCollapsed;

  ngOnInit(): void {
    this.session.load().subscribe(() => this.quota.refresh());
    this.documentStatus.connect(); // live status pushes (US-06)
    this.demo.refresh(); // demo availability + counter (US-03)
    void this.bootstrapConversations();
  }

  /** Loads the session's conversations and makes one active (US-18); the columns then follow the shared active id. */
  private async bootstrapConversations(): Promise<void> {
    const list = await this.conversationsStore.list();
    if (list.length > 0) {
      this.conversationsStore.setActive(list[0].id);
      await this.chat.load(list[0].id);
    } else {
      const created = await this.conversationsStore.create();
      this.chat.reset(created.id);
    }
  }

  async onSelectConversation(id: string): Promise<void> {
    this.conversationsStore.setActive(id);
    await this.chat.load(id);
  }

  async onNewConversation(): Promise<void> {
    const created = await this.conversationsStore.create();
    this.chat.reset(created.id);
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

  toggleConversations(): void {
    this.workspace.toggleConversations();
  }

  toggleSources(): void {
    this.workspace.toggleSources();
  }
}
