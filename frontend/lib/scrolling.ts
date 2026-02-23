export const DEFAULT_BOTTOM_THRESHOLD = 24;

export function isAtBottom(distanceFromBottom: number, threshold = DEFAULT_BOTTOM_THRESHOLD): boolean {
  return distanceFromBottom <= threshold;
}

export function shouldShowJumpToLatest(hasUserScrolled: boolean, atBottom: boolean): boolean {
  return hasUserScrolled && !atBottom;
}
