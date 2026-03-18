import { beforeEach, describe, expect, it } from 'vitest';
import { PromptHistory } from '../../src/shell/history.js';

describe('PromptHistory', () => {
  let history: PromptHistory;

  beforeEach(() => {
    history = new PromptHistory();
  });

  // ---------------------------------------------------------------------------
  // push
  // ---------------------------------------------------------------------------

  describe('push', () => {
    it('adds an entry and increments length', () => {
      history.push('hello');
      expect(history.length).toBe(1);
    });

    it('ignores empty entries', () => {
      history.push('');
      expect(history.length).toBe(0);
    });

    it('ignores whitespace-only entries', () => {
      history.push('   ');
      expect(history.length).toBe(0);
    });

    it('ignores consecutive duplicate entries', () => {
      history.push('hello');
      history.push('hello');
      expect(history.length).toBe(1);
    });

    it('allows non-consecutive duplicates', () => {
      history.push('hello');
      history.push('world');
      history.push('hello');
      expect(history.length).toBe(3);
    });

    it('resets navigation cursor after push', () => {
      history.push('first');
      history.navigateBack('');
      expect(history.isNavigating()).toBe(true);

      history.push('second');
      expect(history.isNavigating()).toBe(false);
    });
  });

  // ---------------------------------------------------------------------------
  // empty history
  // ---------------------------------------------------------------------------

  describe('empty history', () => {
    it('navigateBack returns undefined', () => {
      expect(history.navigateBack('')).toBeUndefined();
    });

    it('navigateForward returns undefined', () => {
      expect(history.navigateForward()).toBeUndefined();
    });

    it('isNavigating returns false', () => {
      expect(history.isNavigating()).toBe(false);
    });
  });

  // ---------------------------------------------------------------------------
  // navigateBack
  // ---------------------------------------------------------------------------

  describe('navigateBack', () => {
    it('returns the most-recent entry on first call', () => {
      history.push('first');
      history.push('second');
      expect(history.navigateBack('')).toBe('second');
    });

    it('returns progressively older entries on repeated calls', () => {
      history.push('first');
      history.push('second');
      history.push('third');

      expect(history.navigateBack('')).toBe('third');
      expect(history.navigateBack('third')).toBe('second');
      expect(history.navigateBack('second')).toBe('first');
    });

    it('clamps at the oldest entry when navigating past the beginning', () => {
      history.push('only');
      expect(history.navigateBack('')).toBe('only');
      expect(history.navigateBack('only')).toBe('only');
    });

    it('sets isNavigating to true after the first call', () => {
      history.push('first');
      history.navigateBack('');
      expect(history.isNavigating()).toBe(true);
    });

    it('saves the current value as draft on the first navigation', () => {
      history.push('first');
      history.push('second');

      history.navigateBack('my draft');   // saves 'my draft'
      history.navigateBack('second');     // further back

      history.navigateForward();          // → 'second'
      const restored = history.navigateForward(); // → draft
      expect(restored).toBe('my draft');
    });

    it('does not overwrite draft on subsequent back navigations', () => {
      history.push('a');
      history.push('b');
      history.push('c');

      history.navigateBack('draft'); // saves 'draft'
      history.navigateBack('c');     // cursor moves, draft unchanged
      history.navigateBack('b');

      // Navigate all the way forward to restore the draft
      history.navigateForward(); // → 'b'
      history.navigateForward(); // → 'c'
      const restored = history.navigateForward(); // → draft
      expect(restored).toBe('draft');
    });
  });

  // ---------------------------------------------------------------------------
  // navigateForward
  // ---------------------------------------------------------------------------

  describe('navigateForward', () => {
    it('returns undefined when not in navigation mode', () => {
      history.push('first');
      expect(history.navigateForward()).toBeUndefined();
    });

    it('navigates forward through history entries', () => {
      history.push('first');
      history.push('second');
      history.push('third');

      history.navigateBack(''); // → 'third'
      history.navigateBack('third'); // → 'second'

      expect(history.navigateForward()).toBe('third');
    });

    it('restores draft when navigating past the newest entry', () => {
      history.push('first');

      history.navigateBack('my draft'); // saves 'my draft'
      const restored = history.navigateForward();
      expect(restored).toBe('my draft');
    });

    it('restores empty string when the draft was empty', () => {
      history.push('first');

      history.navigateBack('');
      const restored = history.navigateForward();
      expect(restored).toBe('');
    });

    it('exits navigation mode after restoring draft', () => {
      history.push('first');

      history.navigateBack('');
      history.navigateForward(); // returns draft
      expect(history.isNavigating()).toBe(false);
    });

    it('returns undefined immediately after exiting navigation', () => {
      history.push('first');

      history.navigateBack('');
      history.navigateForward(); // exits navigation
      expect(history.navigateForward()).toBeUndefined();
    });
  });

  // ---------------------------------------------------------------------------
  // reset
  // ---------------------------------------------------------------------------

  describe('reset', () => {
    it('exits navigation mode', () => {
      history.push('first');
      history.navigateBack('');
      expect(history.isNavigating()).toBe(true);

      history.reset();
      expect(history.isNavigating()).toBe(false);
    });

    it('clears the saved draft', () => {
      history.push('first');
      history.navigateBack('old draft'); // saves 'old draft'
      history.reset();

      // Navigate back after reset: should save a fresh draft
      history.navigateBack('new draft');
      const restored = history.navigateForward();
      expect(restored).toBe('new draft');
    });

    it('does not remove history entries', () => {
      history.push('first');
      history.push('second');
      history.reset();
      expect(history.length).toBe(2);
    });
  });

  // ---------------------------------------------------------------------------
  // typed draft restoration (acceptance-criteria scenario)
  // ---------------------------------------------------------------------------

  describe('typed draft restoration', () => {
    it('restores a non-empty typed draft after full history traversal', () => {
      history.push('prev1');
      history.push('prev2');

      // User had typed 'current draft' before pressing Up
      history.navigateBack('current draft'); // → 'prev2', draft='current draft'
      history.navigateBack('prev2');         // → 'prev1'
      history.navigateForward();             // → 'prev2'
      const draft = history.navigateForward(); // → 'current draft'

      expect(draft).toBe('current draft');
      expect(history.isNavigating()).toBe(false);
    });

    it('returns to empty input when input was empty before navigating', () => {
      history.push('prev');

      history.navigateBack(''); // → 'prev', draft=''
      const draft = history.navigateForward(); // → ''

      expect(draft).toBe('');
      expect(history.isNavigating()).toBe(false);
    });
  });
});
