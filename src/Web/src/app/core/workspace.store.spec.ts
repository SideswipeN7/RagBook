import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiKeyStore } from './api-key.store';
import { DemoStore } from './demo.store';
import { WorkspaceStore } from './workspace.store';

function signalOf<T>(value: T): WritableSignal<T> {
  return signal(value);
}

describe('WorkspaceStore', () => {
  let store: WorkspaceStore;
  const status = signalOf<'unknown' | 'none' | 'active'>('unknown');
  const available = signalOf(false);

  beforeEach(() => {
    status.set('unknown');
    available.set(false);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiKeyStore, useValue: { status } },
        { provide: DemoStore, useValue: { available } },
      ],
    });
    store = TestBed.inject(WorkspaceStore);
  });

  it('is not configured before a key is set or the user continues', () => {
    expect(store.configured()).toBeFalse();
  });

  it('is configured once the API key is active', () => {
    status.set('active');
    expect(store.configured()).toBeTrue();
  });

  it('is configured once the user continues without a key', () => {
    store.continueWithoutKey();
    expect(store.configured()).toBeTrue();
  });

  it('does not auto-configure just because demo is available (config-first)', () => {
    available.set(true);
    expect(store.configured()).toBeFalse();
  });

  it('toggles the column collapse flags', () => {
    expect(store.conversationsCollapsed()).toBeFalse();
    store.toggleConversations();
    expect(store.conversationsCollapsed()).toBeTrue();

    expect(store.sourcesCollapsed()).toBeFalse();
    store.toggleSources();
    expect(store.sourcesCollapsed()).toBeTrue();
  });
});
