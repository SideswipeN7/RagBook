import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiKeyStore } from './api-key.store';
import { DemoStore } from './demo.store';

/**
 * Shell-level state for the NotebookLM-style workspace (US-21): the **config-first** gate and the collapse state of
 * the side columns. The active-conversation id remains owned by {@link ConversationsStore} (the single source of
 * truth every column reads); this store adds only the workspace-chrome concerns so the shell stays thin.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceStore {
  private readonly apiKey = inject(ApiKeyStore);
  private readonly demo = inject(DemoStore);

  /** True once the visitor has chosen to proceed without setting a key (demo / read-only). */
  private readonly continued = signal(false);

  /** Whether demo mode is offered by the server (drives the "continue in demo" affordance). */
  readonly demoAvailable = this.demo.available;

  /** Collapse flags for the two side columns (kept in the shell so toggling never loses the active selection). */
  readonly conversationsCollapsed = signal(false);
  readonly sourcesCollapsed = signal(false);

  /**
   * The config-first gate: the workspace mounts once the visitor has an active API key **or** has explicitly chosen
   * to continue without one (demo / read-only). Demo availability alone does not auto-skip the config step.
   */
  readonly configured = computed(() => this.apiKey.status() === 'active' || this.continued());

  /** Proceed without configuring a key (use demo / browse read-only). */
  continueWithoutKey(): void {
    this.continued.set(true);
  }

  toggleConversations(): void {
    this.conversationsCollapsed.update((value) => !value);
  }

  toggleSources(): void {
    this.sourcesCollapsed.update((value) => !value);
  }
}
