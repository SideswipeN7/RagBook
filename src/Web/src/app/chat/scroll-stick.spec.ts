import { shouldStickToBottom } from './scroll-stick';

describe('shouldStickToBottom', () => {
  it('sticks when at the bottom', () => {
    expect(shouldStickToBottom(940, 60, 1000)).toBeTrue(); // 1000 - (940+60) = 0
  });

  it('detaches when scrolled up past the threshold', () => {
    expect(shouldStickToBottom(400, 60, 1000)).toBeFalse(); // 1000 - 460 = 540 > 48
  });

  it('re-attaches near the bottom within the threshold', () => {
    expect(shouldStickToBottom(910, 60, 1000)).toBeTrue(); // 1000 - 970 = 30 <= 48
  });
});
