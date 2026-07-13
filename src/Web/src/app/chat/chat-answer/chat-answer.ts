import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { ChatExchange, Source } from '../../core/chat.store';
import { AnswerSegment, parseCitations } from '../../core/citation-parser';

/** First N chars of a chunk shown in the source list; the full text lives behind the preview (US-16). */
const SNIPPET_CHARS = 200;

/**
 * Renders an answer with **clickable** `[n]` citations (US-16). Splits the text via {@link parseCitations},
 * separates sources into **used** (their `[n]` appears in the answer) and a collapsible **"pozostałe
 * przeszukane fragmenty"**, and opens an in-app preview (full chunk text + file + page — never a native
 * dialog). The preview reads the chunk `text` captured on the exchange, so it survives document deletion
 * (AC-4). Standalone, OnPush, signals.
 */
@Component({
  selector: 'app-chat-answer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  templateUrl: './chat-answer.html',
  styleUrl: './chat-answer.scss',
})
export class ChatAnswer {
  readonly exchange = input.required<ChatExchange>();

  /** The source currently shown in the preview panel, or null when closed. */
  readonly preview = signal<Source | null>(null);

  private readonly validNumbers = computed(() => new Set(this.exchange().sources.map((source) => source.number)));

  /** The answer split into plain-text and clickable-citation runs. */
  readonly segments = computed<readonly AnswerSegment[]>(() => parseCitations(this.exchange().answer, this.validNumbers()));

  private readonly usedNumbers = computed(
    () => new Set(this.segments().flatMap((segment) => (segment.type === 'citation' ? [segment.number] : []))),
  );

  /** Sources actually cited in the answer — highlighted directly under it. */
  readonly usedSources = computed(() => this.exchange().sources.filter((source) => this.usedNumbers().has(source.number)));

  /** Retrieved-but-not-cited sources — collapsed, or all sources when the model cited nothing. */
  readonly otherSources = computed(() => this.exchange().sources.filter((source) => !this.usedNumbers().has(source.number)));

  open(source: Source): void {
    this.preview.set(source);
  }

  openByNumber(number: number): void {
    const source = this.exchange().sources.find((candidate) => candidate.number === number);
    if (source) {
      this.preview.set(source);
    }
  }

  close(): void {
    this.preview.set(null);
  }

  snippet(text: string): string {
    return text.length > SNIPPET_CHARS ? `${text.slice(0, SNIPPET_CHARS).trimEnd()}…` : text;
  }
}
