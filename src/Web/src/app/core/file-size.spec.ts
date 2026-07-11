import { formatFileSize } from './file-size';

describe('formatFileSize', () => {
  it('formats bytes below 1 KB as B', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(999)).toBe('999 B');
  });

  it('formats KB with one decimal (decimal thousands)', () => {
    expect(formatFileSize(1_500)).toBe('1.5 KB');
    expect(formatFileSize(900_000)).toBe('900.0 KB');
  });

  it('formats MB with one decimal (1 MB = 1,000,000 bytes)', () => {
    expect(formatFileSize(12_300_000)).toBe('12.3 MB');
    expect(formatFileSize(2_000_000)).toBe('2.0 MB');
  });
});
