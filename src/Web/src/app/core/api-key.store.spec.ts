import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiKeyStore } from './api-key.store';

describe('ApiKeyStore', () => {
  let store: ApiKeyStore;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(ApiKeyStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should read status via GET and expose active + mask', () => {
    store.refresh();

    controller
      .expectOne((req) => req.method === 'GET' && req.url === '/api/settings/api-key')
      .flush({ status: 'active', maskedKey: 'sk-ant-api03-…B7fA' });

    expect(store.status()).toBe('active');
    expect(store.maskedKey()).toBe('sk-ant-api03-…B7fA');
    expect(store.chatLocked()).toBeFalse();
  });

  it('should POST the key and apply the returned active status', () => {
    store.save('sk-ant-api03-secretvalue');

    const post = controller.expectOne((req) => req.method === 'POST' && req.url === '/api/settings/api-key');
    expect(post.request.body).toEqual({ apiKey: 'sk-ant-api03-secretvalue' });
    post.flush({ status: 'active', maskedKey: 'sk-ant-api03-…alue' });

    expect(store.status()).toBe('active');
    expect(store.maskedKey()).toBe('sk-ant-api03-…alue');
    expect(store.saving()).toBeFalse();
    expect(store.error()).toBeNull();
  });

  it('should surface the stable error code message on rejection and keep the key unset', () => {
    store.save('bad-key');

    controller
      .expectOne('/api/settings/api-key')
      .flush({ code: 'settings.invalid_api_key' }, { status: 400, statusText: 'Bad Request' });

    expect(store.error()).toContain('Nieprawidłowy klucz');
    expect(store.status()).not.toBe('active');
    expect(store.saving()).toBeFalse();
  });

  it('should map the transient validation error to a distinct retry message', () => {
    store.save('sk-ant-api03-secretvalue');

    controller
      .expectOne('/api/settings/api-key')
      .flush({ code: 'settings.validation_unavailable' }, { status: 503, statusText: 'Service Unavailable' });

    expect(store.error()).toContain('Spróbuj ponownie');
  });

  it('should DELETE the key and return to the none/locked state', () => {
    store.delete();

    controller
      .expectOne((req) => req.method === 'DELETE' && req.url === '/api/settings/api-key')
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(store.status()).toBe('none');
    expect(store.maskedKey()).toBeNull();
    expect(store.chatLocked()).toBeTrue();
  });
});
