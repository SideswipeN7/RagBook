import { parseCitations } from './citation-parser';

const VALID = new Set([1, 2]);

describe('parseCitations', () => {
  it('splits text and in-range citation markers', () => {
    expect(parseCitations('Okres wynosi [1] miesiące.', VALID)).toEqual([
      { type: 'text', value: 'Okres wynosi ' },
      { type: 'citation', number: 1 },
      { type: 'text', value: ' miesiące.' },
    ]);
  });

  it('handles adjacent and repeated markers', () => {
    expect(parseCitations('A [1][2] i znów [1].', VALID)).toEqual([
      { type: 'text', value: 'A ' },
      { type: 'citation', number: 1 },
      { type: 'citation', number: 2 },
      { type: 'text', value: ' i znów ' },
      { type: 'citation', number: 1 },
      { type: 'text', value: '.' },
    ]);
  });

  it('leaves an out-of-range marker as plain text', () => {
    const warn = spyOn(console, 'warn');

    expect(parseCitations('Zdanie [9] koniec.', VALID)).toEqual([{ type: 'text', value: 'Zdanie [9] koniec.' }]);
    expect(warn).toHaveBeenCalled();
  });

  it('leaves an incomplete marker as text until it is closed (mid-stream)', () => {
    expect(parseCitations('Trwa [1', VALID)).toEqual([{ type: 'text', value: 'Trwa [1' }]);
  });

  it('returns a single text segment when there are no markers', () => {
    expect(parseCitations('Bez cytatów.', VALID)).toEqual([{ type: 'text', value: 'Bez cytatów.' }]);
  });
});
