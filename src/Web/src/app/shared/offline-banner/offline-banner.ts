import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConnectivityService } from '../../core/connectivity.service';

/**
 * A global banner shown while the browser is offline (US-19 edge case). Reads {@link ConnectivityService.isOnline}
 * and renders a `role="status"` notice; it disappears the moment connectivity returns. Standalone, OnPush, tokens.
 */
@Component({
  selector: 'app-offline-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!isOnline()) {
      <div class="offline-banner" role="status">Brak połączenia z internetem — niektóre funkcje mogą nie działać.</div>
    }
  `,
  styleUrl: './offline-banner.scss',
})
export class OfflineBanner {
  readonly isOnline = inject(ConnectivityService).isOnline;
}
