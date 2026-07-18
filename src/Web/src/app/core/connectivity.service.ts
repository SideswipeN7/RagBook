import { Injectable, signal } from '@angular/core';

/**
 * Tracks browser connectivity for the global offline banner (US-19 edge case). Seeds {@link isOnline} from
 * `navigator.onLine` and flips it on the `window` `online` / `offline` events — a framework-native signal, no
 * polling. The banner reads {@link isOnline} to show/hide "Brak połączenia z internetem".
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  /** True while the browser reports connectivity. */
  readonly isOnline = signal(typeof navigator === 'undefined' ? true : navigator.onLine);

  constructor() {
    if (typeof window === 'undefined') {
      return;
    }
    window.addEventListener('online', () => this.isOnline.set(true));
    window.addEventListener('offline', () => this.isOnline.set(false));
  }
}
