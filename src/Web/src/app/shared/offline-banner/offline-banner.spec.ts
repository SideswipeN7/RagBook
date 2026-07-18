import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectivityService } from '../../core/connectivity.service';
import { OfflineBanner } from './offline-banner';

describe('OfflineBanner', () => {
  let fixture: ComponentFixture<OfflineBanner>;
  let connectivity: ConnectivityService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(OfflineBanner);
    connectivity = TestBed.inject(ConnectivityService);
  });

  it('is hidden while online', () => {
    connectivity.isOnline.set(true);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.offline-banner')).toBeNull();
  });

  it('shows the offline notice when connectivity is lost, and clears on restore (US-19)', () => {
    connectivity.isOnline.set(false);
    fixture.detectChanges();
    const banner = (fixture.nativeElement as HTMLElement).querySelector('.offline-banner');
    expect(banner).toBeTruthy();
    expect(banner?.textContent).toContain('Brak połączenia');

    connectivity.isOnline.set(true);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.offline-banner')).toBeNull();
  });
});

describe('ConnectivityService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  it('reflects window online/offline events', () => {
    const service = TestBed.inject(ConnectivityService);

    window.dispatchEvent(new Event('offline'));
    expect(service.isOnline()).toBeFalse();

    window.dispatchEvent(new Event('online'));
    expect(service.isOnline()).toBeTrue();
  });
});
