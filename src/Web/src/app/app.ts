import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { ApiKeyStore } from './core/api-key.store';
import { DocumentStatusStore } from './core/document-status.store';
import { NotFoundNotifier } from './core/not-found-notifier';
import { QuotaStore } from './core/quota.store';
import { SessionService } from './core/session.service';
import { QuotaBar } from './documents/quota-bar/quota-bar';
import { DocumentTree } from './documents/tree/document-tree';
import { DocumentUpload } from './documents/upload/document-upload';
import { ApiKeySettings } from './settings/api-key-settings/api-key-settings';

/** Root shell. Standalone, OnPush, signals, new control flow. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [QuotaBar, DocumentUpload, DocumentTree, ApiKeySettings],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly session = inject(SessionService);
  private readonly notFound = inject(NotFoundNotifier);
  private readonly quota = inject(QuotaStore);
  private readonly apiKey = inject(ApiKeyStore);
  private readonly documentStatus = inject(DocumentStatusStore);

  readonly state = this.session.state;
  readonly notFoundMessage = this.notFound.message;

  /** Drives the "chat locked until a key is set" hint (US-02 AC-3, FR-015). */
  readonly chatLocked = this.apiKey.chatLocked;

  ngOnInit(): void {
    this.session.load().subscribe(() => this.quota.refresh());
    // Live document status pushes (US-06) refresh the tree without a reload.
    this.documentStatus.connect();
  }
}
