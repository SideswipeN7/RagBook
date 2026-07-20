import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { WorkspaceStore } from '../../core/workspace.store';
import { ApiKeySettings } from '../../settings/api-key-settings/api-key-settings';

/**
 * The config-first onboarding step (US-21): shown before the workspace. The visitor either configures their own
 * Anthropic key (via {@link ApiKeySettings}) or continues without one (demo / read-only). Once a key is active or
 * the visitor continues, {@link WorkspaceStore.configured} flips and the shell mounts the workspace. Standalone,
 * OnPush, design tokens.
 */
@Component({
  selector: 'app-onboarding',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ApiKeySettings],
  templateUrl: './onboarding.html',
  styleUrl: './onboarding.scss',
})
export class Onboarding {
  private readonly workspace = inject(WorkspaceStore);

  readonly demoAvailable = this.workspace.demoAvailable;

  continueWithoutKey(): void {
    this.workspace.continueWithoutKey();
  }
}
