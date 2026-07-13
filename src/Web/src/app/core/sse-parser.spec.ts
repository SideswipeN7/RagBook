import { SseParser } from './sse-parser';

describe('SseParser', () => {
  it('parses events in order', () => {
    const parser = new SseParser();

    const events = parser.push('event: sources\ndata: [1]\n\nevent: token\ndata: {"text":"hi"}\n\n');

    expect(events.map((e) => e.event)).toEqual(['sources', 'token']);
    expect(events[1].data).toBe('{"text":"hi"}');
  });

  it('assembles an event split across two chunks', () => {
    const parser = new SseParser();

    expect(parser.push('event: token\nda')).toEqual([]);
    const events = parser.push('ta: {"text":"world"}\n\n');

    expect(events).toEqual([{ event: 'token', data: '{"text":"world"}' }]);
  });

  it('ignores keep-alive comment blocks', () => {
    const parser = new SseParser();

    const events = parser.push(': keep-alive\n\nevent: done\ndata: {"groundsFound":true}\n\n');

    expect(events.map((e) => e.event)).toEqual(['done']);
  });
});
