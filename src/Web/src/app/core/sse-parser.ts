/** One parsed Server-Sent Event. */
export interface SseEvent {
  readonly event: string;
  readonly data: string;
}

/**
 * Incremental SSE parser (US-15). Fed decoded text chunks from a streaming fetch, it emits complete
 * `{event, data}` records as their blank-line boundary (`\n\n`) arrives — buffering an event split across
 * chunks — and ignores `:` comment lines (the keep-alive heartbeat). Pure and framework-free, so it is
 * unit-tested directly without a network.
 */
export class SseParser {
  private buffer = '';

  /** Appends a chunk and returns any events that completed. */
  push(chunk: string): SseEvent[] {
    this.buffer += chunk;
    const events: SseEvent[] = [];

    let boundary = this.buffer.indexOf('\n\n');
    while (boundary >= 0) {
      const block = this.buffer.slice(0, boundary);
      this.buffer = this.buffer.slice(boundary + 2);

      const parsed = parseBlock(block);
      if (parsed) {
        events.push(parsed);
      }

      boundary = this.buffer.indexOf('\n\n');
    }

    return events;
  }
}

function parseBlock(block: string): SseEvent | null {
  let event = 'message';
  let data = '';
  let hasData = false;

  for (const line of block.split('\n')) {
    if (line.startsWith(':')) {
      continue; // comment (keep-alive) — ignored
    }
    if (line.startsWith('event:')) {
      event = line.slice('event:'.length).trim();
    } else if (line.startsWith('data:')) {
      data += line.slice('data:'.length).trim();
      hasData = true;
    }
  }

  return hasData ? { event, data } : null;
}
