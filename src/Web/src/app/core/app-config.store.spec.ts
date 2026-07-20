import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppConfigStore } from './app-config.store';

describe('AppConfigStore', () => {
  let store: AppConfigStore;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(AppConfigStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('defaults keylessGeneration to false before the first response', () => {
    expect(store.keylessGeneration()).toBeFalse();
  });

  it('loads keylessGeneration from GET /api/config', () => {
    store.refresh();

    controller.expectOne('/api/config').flush({ keylessGeneration: true });

    expect(store.keylessGeneration()).toBeTrue();
  });

  it('keeps the composer-blocking default when the server reports no keyless generation', () => {
    store.refresh();

    controller.expectOne('/api/config').flush({ keylessGeneration: false });

    expect(store.keylessGeneration()).toBeFalse();
  });
});
