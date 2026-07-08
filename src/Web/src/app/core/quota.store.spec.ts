import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { QuotaState, QuotaStore } from './quota.store';

describe('QuotaStore', () => {
  let store: QuotaStore;
  let controller: HttpTestingController;

  const quota: QuotaState = {
    usedDocuments: 7,
    maxDocuments: 10,
    usedMb: 12.3,
    maxTotalMb: 50,
    maxFileSizeMb: 10,
    canUpload: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(QuotaStore);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('should load the quota state from GET /api/quota', () => {
    store.refresh();

    controller.expectOne('/api/quota').flush(quota);

    expect(store.state()).toEqual(quota);
    expect(store.canUpload()).toBeTrue();
    expect(store.isFull()).toBeFalse();
  });

  it('should report full when the backend says upload is not possible', () => {
    store.refresh();

    controller.expectOne('/api/quota').flush({ ...quota, usedDocuments: 10, canUpload: false });

    expect(store.isFull()).toBeTrue();
    expect(store.canUpload()).toBeFalse();
  });
});
