import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { DocumentStatusStore } from './core/document-status.store';
import { NotFoundNotifier } from './core/not-found-notifier';
import { QuotaStore } from './core/quota.store';
import { SessionService } from './core/session.service';
import { QuotaBar } from './documents/quota-bar/quota-bar';
import { DocumentTree } from './documents/tree/document-tree';
import { DocumentUpload } from './documents/upload/document-upload';

/** Root shell. Standalone, OnPush, signals, new control flow. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [QuotaBar, DocumentUpload, DocumentTree],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly session = inject(SessionService);
  private readonly notFound = inject(NotFoundNotifier);
  private readonly quota = inject(QuotaStore);
  private readonly documentStatus = inject(DocumentStatusStore);

  readonly state = this.session.state;
  readonly notFoundMessage = this.notFound.message;

  ngOnInit(): void {
    this.session.load().subscribe(() => this.quota.refresh());
    // Live document status pushes (US-06) refresh the tree without a reload.
    this.documentStatus.connect();
  }
}
