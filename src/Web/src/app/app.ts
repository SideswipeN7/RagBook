import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { NotFoundNotifier } from './core/not-found-notifier';
import { SessionService } from './core/session.service';

/** Root shell. Standalone, OnPush, signals, new control flow. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly session = inject(SessionService);
  private readonly notFound = inject(NotFoundNotifier);

  readonly state = this.session.state;
  readonly notFoundMessage = this.notFound.message;

  ngOnInit(): void {
    this.session.load();
  }
}
