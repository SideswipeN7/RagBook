import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ChatStore } from './core/chat.store';
import { ConversationsStore } from './core/conversations.store';
import { DemoStore } from './core/demo.store';
import { DocumentStatusStore } from './core/document-status.store';
import { App } from './app';

// Fake the orchestration/status stores so the shell's ngOnInit does not hit HTTP for conversations/demo/status —
// this test focuses on the session→quota ordering invariant.
class FakeConversationsStore {
  readonly conversations = signal<readonly never[]>([]);
  readonly activeId = signal<string | null>('c1');
  readonly list = jasmine.createSpy('list').and.resolveTo([{ id: 'c1' }]);
  readonly setActive = jasmine.createSpy('setActive');
  readonly create = jasmine.createSpy('create').and.resolveTo({ id: 'c1' });
  readonly remove = jasmine.createSpy('remove').and.resolveTo(undefined);
}
class FakeChatStore {
  readonly load = jasmine.createSpy('load').and.resolveTo(undefined);
  readonly reset = jasmine.createSpy('reset');
}
class FakeDemoStore {
  readonly available = signal(false);
  readonly refresh = jasmine.createSpy('refresh');
}
class FakeDocumentStatusStore {
  readonly connect = jasmine.createSpy('connect');
}

describe('App', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConversationsStore, useValue: new FakeConversationsStore() },
        { provide: ChatStore, useValue: new FakeChatStore() },
        { provide: DemoStore, useValue: new FakeDemoStore() },
        { provide: DocumentStatusStore, useValue: new FakeDocumentStatusStore() },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should not request the quota until the session has been established', () => {
    const fixture = TestBed.createComponent(App);

    fixture.detectChanges(); // ngOnInit
    const session = controller.expectOne('/api/session');

    // No cookie-less quota request races the session request.
    expect(() => controller.expectNone('/api/quota')).not.toThrow();
    session.flush({ isNew: true, resourceCount: 0 });
    expect(() =>
      controller.expectOne('/api/quota').flush({
        usedDocuments: 0,
        maxDocuments: 10,
        usedMb: 0,
        maxTotalMb: 50,
        maxFileSizeMb: 10,
        canUpload: true,
      }),
    ).not.toThrow();
  });
});
