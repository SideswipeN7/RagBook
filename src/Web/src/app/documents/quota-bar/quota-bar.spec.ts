import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuotaState, QuotaStore } from '../../core/quota.store';
import { QuotaBar } from './quota-bar';

describe('QuotaBar', () => {
  let fixture: ComponentFixture<QuotaBar>;
  let store: QuotaStore;

  const quota: QuotaState = {
    usedDocuments: 5,
    maxDocuments: 10,
    usedMb: 25,
    maxTotalMb: 50,
    maxFileSizeMb: 10,
    canUpload: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    fixture = TestBed.createComponent(QuotaBar);
    store = TestBed.inject(QuotaStore);
  });

  it('should render both quota labels once the state is known', () => {
    store.state.set(quota);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('5 / 10 plików');
    expect(text).toContain('25 / 50 MB');
  });

  it('should compute the meter percentages from used over max', () => {
    store.state.set(quota);

    expect(fixture.componentInstance.documentsPercent()).toBe(50);
    expect(fixture.componentInstance.storagePercent()).toBe(50);
  });

  it('should clamp the meter percentages to 100 when usage exceeds the limit', () => {
    store.state.set({ ...quota, usedDocuments: 12, usedMb: 80 });

    expect(fixture.componentInstance.documentsPercent()).toBe(100);
    expect(fixture.componentInstance.storagePercent()).toBe(100);
  });

  it('should surface the delete-files notice when the quota is full', () => {
    store.state.set({ ...quota, usedDocuments: 10, canUpload: false });
    fixture.detectChanges();

    const notice = (fixture.nativeElement as HTMLElement).querySelector('.quota__notice');
    expect(notice).not.toBeNull();
    expect(notice?.textContent).toContain('usuń pliki');
  });

  it('should not render anything before the state is loaded', () => {
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.quota')).toBeNull();
  });
});
