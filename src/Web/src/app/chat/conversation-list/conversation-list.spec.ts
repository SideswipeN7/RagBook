import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConversationSummary } from '../../core/conversations.store';
import { ConversationList } from './conversation-list';

function summary(id: string, title = ''): ConversationSummary {
  return { id, title, scopeType: 'all', scopeTargetId: null, createdAt: '' };
}

describe('ConversationList', () => {
  let fixture: ComponentFixture<ConversationList>;

  function render(conversations: ConversationSummary[], activeId: string | null = null): HTMLElement {
    fixture = TestBed.createComponent(ConversationList);
    fixture.componentRef.setInput('conversations', conversations);
    fixture.componentRef.setInput('activeId', activeId);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function button(el: HTMLElement, text: string): HTMLButtonElement {
    return Array.from(el.querySelectorAll('button')).find((candidate) => candidate.textContent?.includes(text)) as HTMLButtonElement;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  it('emits create when "Nowa rozmowa" is clicked', () => {
    const el = render([]);
    const spy = jasmine.createSpy();
    fixture.componentInstance.create.subscribe(spy);

    button(el, 'Nowa rozmowa').click();

    expect(spy).toHaveBeenCalled();
  });

  it('shows a placeholder title for an untitled conversation and emits select', () => {
    const el = render([summary('c1')]);
    const spy = jasmine.createSpy();
    fixture.componentInstance.select.subscribe(spy);

    const open = el.querySelector('.convs__open') as HTMLButtonElement;
    expect(open.textContent).toContain('Nowa rozmowa');

    open.click();
    expect(spy).toHaveBeenCalledWith('c1');
  });

  it('deletes only after an inline confirmation (no native dialog)', () => {
    const el = render([summary('c1', 'Umowa najmu')]);
    const spy = jasmine.createSpy();
    fixture.componentInstance.remove.subscribe(spy);

    (el.querySelector('.convs__del') as HTMLButtonElement).click();
    fixture.detectChanges();

    // The confirm is shown; nothing deleted yet.
    expect(spy).not.toHaveBeenCalled();
    const confirm = button(el, 'Tak, usuń');
    expect(confirm).toBeTruthy();

    confirm.click();
    expect(spy).toHaveBeenCalledWith('c1');
  });
});
