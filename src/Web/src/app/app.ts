import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { NotFoundNotifier } from './core/not-found-notifier';
import { QuotaStore } from './core/quota.store';
import { SessionService } from './core/session.service';
import { QuotaBar } from './documents/quota-bar/quota-bar';

/** Root shell. Standalone, OnPush, signals, new control flow. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [QuotaBar],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly session = inject(SessionService);
  private readonly notFound = inject(NotFoundNotifier);
  private readonly quota = inject(QuotaStore);

  readonly state = this.session.state;
  readonly notFoundMessage = this.notFound.message;

  ngOnInit(): void {
    this.session.load().subscribe(() => this.quota.refresh());
  }
}
