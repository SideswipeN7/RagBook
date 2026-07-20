import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The Studio column (US-21) — visualizations over the active conversation's sources, NotebookLM-style. In Stage 1
 * the tiles are presentational placeholders; the "Podsumowanie" tile becomes a real AI summary in Stage 3, the
 * others remain "wkrótce". Standalone, OnPush, design tokens.
 */
@Component({
  selector: 'app-studio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './studio.html',
  styleUrl: './studio.scss',
})
export class Studio {
  /** The visualization tiles; only "Podsumowanie" will be wired up (Stage 3). */
  readonly tiles: readonly { readonly title: string; readonly soon: boolean }[] = [
    { title: 'Podsumowanie', soon: true },
    { title: 'Mapa myśli', soon: true },
    { title: 'Prezentacja', soon: true },
    { title: 'Raporty', soon: true },
  ];
}
