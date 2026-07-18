import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DemoStatusDto, DemoStore } from './demo.store';

describe('DemoStore', () => {
  let store: DemoStore;
  let controller: HttpTestingController;

  const status: DemoStatusDto = { asked: 3, max: 10, remaining: 7, available: true };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(DemoStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('loads demo usage from GET /api/demo/status', () => {
    store.refresh();

    controller.expectOne('/api/demo/status').flush(status);

    expect(store.asked()).toBe(3);
    expect(store.max()).toBe(10);
    expect(store.remaining()).toBe(7);
    expect(store.available()).toBeTrue();
    expect(store.isExhausted()).toBeFalse();
  });

  it('reports exhausted only when demo is available and nothing remains', () => {
    store.refresh();
    controller.expectOne('/api/demo/status').flush({ asked: 10, max: 10, remaining: 0, available: true });

    expect(store.isExhausted()).toBeTrue();
  });

  it('does not report exhausted when demo is unavailable', () => {
    store.refresh();
    controller.expectOne('/api/demo/status').flush({ asked: 0, max: 10, remaining: 0, available: false });

    expect(store.isExhausted()).toBeFalse();
  });

  it('optimistically decrements remaining on noteAsked, then reconciles', () => {
    store.refresh();
    controller.expectOne('/api/demo/status').flush(status); // remaining 7

    store.noteAsked();

    // Optimistic: remaining 6, asked 4 — before the reconciling GET resolves.
    expect(store.remaining()).toBe(6);
    expect(store.asked()).toBe(4);
    controller.expectOne('/api/demo/status').flush({ asked: 4, max: 10, remaining: 6, available: true });
    expect(store.remaining()).toBe(6);
  });
});
