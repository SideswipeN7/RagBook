/**
 * Pure auto-scroll decision (US-15 FR-009, A2): the view "sticks" to the bottom (auto-scrolls to follow new
 * text) only while the user is at/near the bottom; once they scroll up past the threshold it detaches, and
 * re-attaches when they return. Extracted so it is unit-testable without a DOM.
 */
export function shouldStickToBottom(
  scrollTop: number,
  clientHeight: number,
  scrollHeight: number,
  threshold = 48,
): boolean {
  return scrollHeight - (scrollTop + clientHeight) <= threshold;
}
