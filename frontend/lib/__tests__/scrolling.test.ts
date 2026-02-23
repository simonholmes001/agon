import { describe, it, expect } from "vitest";
import {
  DEFAULT_BOTTOM_THRESHOLD,
  isAtBottom,
  shouldShowJumpToLatest,
} from "@/lib/scrolling";

describe("scrolling helpers", () => {
  it("treats distances within the threshold as bottom", () => {
    expect(isAtBottom(DEFAULT_BOTTOM_THRESHOLD)).toBe(true);
    expect(isAtBottom(DEFAULT_BOTTOM_THRESHOLD - 1)).toBe(true);
    expect(isAtBottom(DEFAULT_BOTTOM_THRESHOLD + 1)).toBe(false);
  });

  it("shows jump to latest only when the user has scrolled away", () => {
    expect(shouldShowJumpToLatest(false, false)).toBe(false);
    expect(shouldShowJumpToLatest(true, true)).toBe(false);
    expect(shouldShowJumpToLatest(true, false)).toBe(true);
  });
});
