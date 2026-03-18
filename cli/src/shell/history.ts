/**
 * Manages the in-session prompt history buffer and history navigation state.
 *
 * Navigation state (cursor + draft) tracks the user's position as they step
 * through history with the Up/Down arrow keys.  The cursor starts at
 * `entries.length` (past-the-end = "not navigating").  The draft holds
 * whatever the user had typed in the input field before they first pressed Up,
 * so it can be restored when they press Down back past the most-recent entry.
 */
export class PromptHistory {
  private readonly entries: string[] = [];
  private cursor: number = 0; // = entries.length when not navigating
  private draft: string = '';

  /**
   * Add an entry to history.
   * Empty / whitespace-only entries are ignored.
   * Consecutive duplicate entries are de-duplicated.
   * Navigation cursor is reset to the new end of history.
   */
  push(entry: string): void {
    const trimmed = entry.trim();
    if (trimmed.length === 0) return;
    if (this.entries.length > 0 && this.entries[this.entries.length - 1] === trimmed) return;
    this.entries.push(trimmed);
    this.reset();
  }

  /**
   * Reset navigation state so the cursor points past the end of history
   * (i.e. "not navigating").  Call this at the start of each new prompt.
   */
  reset(): void {
    this.cursor = this.entries.length;
    this.draft = '';
  }

  /**
   * Navigate to the previous (older) history entry.
   *
   * On the first call after `reset()`, `currentValue` is saved as the draft
   * so it can be restored via `navigateForward()` later.
   *
   * Returns the entry text, or `undefined` when there is nothing to navigate
   * (history is empty).  When already at the oldest entry, that entry is
   * returned again (clamped at the boundary).
   */
  navigateBack(currentValue: string): string | undefined {
    if (this.entries.length === 0) return undefined;
    if (this.cursor === this.entries.length) {
      this.draft = currentValue;
    }
    if (this.cursor > 0) {
      this.cursor -= 1;
    }
    return this.entries[this.cursor];
  }

  /**
   * Navigate to the next (newer) history entry.
   *
   * When the cursor moves past the most-recent entry the saved draft is
   * returned and navigation mode is exited.
   *
   * Returns `undefined` when not currently in navigation mode.
   */
  navigateForward(): string | undefined {
    if (this.cursor >= this.entries.length) return undefined;
    this.cursor += 1;
    if (this.cursor >= this.entries.length) {
      return this.draft;
    }
    return this.entries[this.cursor];
  }

  /**
   * Whether the cursor is currently pointing at a history entry (i.e. the
   * user has navigated into history and has not yet returned to the draft).
   */
  isNavigating(): boolean {
    return this.cursor < this.entries.length;
  }

  /** Total number of entries stored in history. */
  get length(): number {
    return this.entries.length;
  }
}
