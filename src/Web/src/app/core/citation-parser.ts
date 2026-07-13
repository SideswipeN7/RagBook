/** A run of the answer: plain text, or an in-range `[n]` citation marker. */
export type AnswerSegment = { readonly type: 'text'; readonly value: string } | { readonly type: 'citation'; readonly number: number };

const MARKER = /\[(\d+)\]/g;

/**
 * Splits an answer into text + citation segments (US-16). A `[n]` whose number is in
 * <paramref name="validNumbers"/> becomes a **citation** segment (clickable); an **out-of-range** `[n]`
 * (a number not among the sources) is left inside the surrounding **text** and a quality warning is logged.
 * An incomplete marker (`[1` before its `]`, e.g. mid-stream) never matches, so it stays text until closed.
 * Pure — unit-tested without a DOM.
 */
export function parseCitations(answer: string, validNumbers: ReadonlySet<number>): AnswerSegment[] {
  const segments: AnswerSegment[] = [];
  let lastIndex = 0;
  MARKER.lastIndex = 0;

  let match: RegExpExecArray | null;
  while ((match = MARKER.exec(answer)) !== null) {
    const number = Number(match[1]);
    if (!validNumbers.has(number)) {
      // Out of range — the model referenced a source it wasn't given. Leave the marker in the text.
      console.warn(`Citation marker [${number}] is out of range — no such source.`);
      continue;
    }

    if (match.index > lastIndex) {
      segments.push({ type: 'text', value: answer.slice(lastIndex, match.index) });
    }
    segments.push({ type: 'citation', number });
    lastIndex = match.index + match[0].length;
  }

  if (lastIndex < answer.length) {
    segments.push({ type: 'text', value: answer.slice(lastIndex) });
  }

  return segments;
}
