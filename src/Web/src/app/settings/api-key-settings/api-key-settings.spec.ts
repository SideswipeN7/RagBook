import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiKeySettings } from './api-key-settings';

describe('ApiKeySettings', () => {
  let fixture: ComponentFixture<ApiKeySettings>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(ApiKeySettings);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function loadStatus(status: 'none' | 'active', maskedKey: string | null = null): HTMLElement {
    fixture.detectChanges(); // ngOnInit → refresh() → GET
    controller.expectOne('/api/settings/api-key').flush({ status, maskedKey });
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('shows the password input and Save when no key is configured', () => {
    const el = loadStatus('none');

    const input = el.querySelector('input.apikey__input') as HTMLInputElement;
    expect(input).not.toBeNull();
    expect(input.type).toBe('password');
    expect(el.textContent).toContain('Zapisz');
  });

  it('saves a typed key and then shows the active mask, never the full key', () => {
    const el = loadStatus('none');

    const input = el.querySelector('input.apikey__input') as HTMLInputElement;
    input.value = 'sk-ant-api03-secretvalue';
    fixture.componentInstance.save(input.value);

    controller
      .expectOne((req) => req.method === 'POST' && req.url === '/api/settings/api-key')
      .flush({ status: 'active', maskedKey: 'sk-ant-api03-…alue' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('sk-ant-api03-…alue');
    expect(text).not.toContain('sk-ant-api03-secretvalue');
  });

  it('deletes an active key after inline confirmation', () => {
    loadStatus('active', 'sk-ant-api03-…B7fA');

    fixture.componentInstance.askDelete();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Usunąć zapisany klucz');

    fixture.componentInstance.confirmDelete();
    controller
      .expectOne((req) => req.method === 'DELETE' && req.url === '/api/settings/api-key')
      .flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(fixture.componentInstance.confirmingDelete()).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Podaj własny klucz');
  });
});
