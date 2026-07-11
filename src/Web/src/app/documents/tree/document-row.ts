import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DocumentNode } from '../../core/tree.store';

/**
 * Renders one document row in the tree (US-07 AC-2): name (truncated with a full-name tooltip), a
 * human-readable size, and a status-specific badge — processing → spinner, failed → error with the
 * reason on hover, ready → chunk count — plus the upload date. Standalone, OnPush, design tokens.
 */
@Component({
  selector: 'app-document-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  templateUrl: './document-row.html',
  styleUrl: './document-row.scss',
})
export class DocumentRow {
  readonly node = input.required<DocumentNode>();
}
