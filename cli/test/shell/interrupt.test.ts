import { describe, expect, it } from 'vitest';
import {
  CTRL_C_EXIT_SENTINEL,
  INTERRUPT_SENTINEL,
  isAbortError,
  isExitInput,
  raceAbort,
  selectCtrlCSentinel
} from '../../src/commands/shell.js';
import { buildInterruptHint } from '../../src/shell/renderer.js';

describe('Ctrl+C interrupt handling', () => {
  describe('INTERRUPT_SENTINEL', () => {
    it('is not treated as an exit command', () => {
      expect(isExitInput(INTERRUPT_SENTINEL)).toBe(false);
    });

    it('is distinct from /quit, /exit, and /eot', () => {
      expect(INTERRUPT_SENTINEL).not.toBe('/quit');
      expect(INTERRUPT_SENTINEL).not.toBe('/exit');
      expect(INTERRUPT_SENTINEL).not.toBe('/eot');
    });

    it('is the ASCII ETX character (Ctrl+C)', () => {
      expect(INTERRUPT_SENTINEL).toBe('\x03');
    });

    it('is distinct from CTRL_C_EXIT_SENTINEL', () => {
      expect(INTERRUPT_SENTINEL).not.toBe(CTRL_C_EXIT_SENTINEL);
    });
  });

  describe('CTRL_C_EXIT_SENTINEL', () => {
    it('is not treated as an exit command (handled separately by the main loop)', () => {
      expect(isExitInput(CTRL_C_EXIT_SENTINEL)).toBe(false);
    });

    it('is distinct from /quit, /exit, and /eot', () => {
      expect(CTRL_C_EXIT_SENTINEL).not.toBe('/quit');
      expect(CTRL_C_EXIT_SENTINEL).not.toBe('/exit');
      expect(CTRL_C_EXIT_SENTINEL).not.toBe('/eot');
    });

    it('is distinct from INTERRUPT_SENTINEL', () => {
      expect(CTRL_C_EXIT_SENTINEL).not.toBe(INTERRUPT_SENTINEL);
    });
  });

  describe('selectCtrlCSentinel — Ctrl+C sentinel selection by input content', () => {
    it('returns CTRL_C_EXIT_SENTINEL when the input zone is empty', () => {
      expect(selectCtrlCSentinel('')).toBe(CTRL_C_EXIT_SENTINEL);
    });

    it('returns INTERRUPT_SENTINEL when the input zone has content', () => {
      expect(selectCtrlCSentinel('hello')).toBe(INTERRUPT_SENTINEL);
    });

    it('returns INTERRUPT_SENTINEL for single-character input', () => {
      expect(selectCtrlCSentinel('a')).toBe(INTERRUPT_SENTINEL);
    });

    it('returns INTERRUPT_SENTINEL for whitespace-only input (input is not empty)', () => {
      expect(selectCtrlCSentinel(' ')).toBe(INTERRUPT_SENTINEL);
    });
  });

  describe('isAbortError', () => {
    it('returns true for a DOMException with name AbortError', () => {
      const err = new DOMException('Aborted', 'AbortError');
      expect(isAbortError(err)).toBe(true);
    });

    it('returns false for a regular Error', () => {
      expect(isAbortError(new Error('something went wrong'))).toBe(false);
    });

    it('returns false for a DOMException with a different name', () => {
      expect(isAbortError(new DOMException('timeout', 'TimeoutError'))).toBe(false);
    });

    it('returns false for a non-error value', () => {
      expect(isAbortError(null)).toBe(false);
      expect(isAbortError('abort')).toBe(false);
      expect(isAbortError(42)).toBe(false);
    });
  });

  describe('raceAbort — idle state (no active operation)', () => {
    it('resolves normally when the signal is never aborted', async () => {
      const controller = new AbortController();
      const result = await raceAbort(Promise.resolve('done'), controller.signal);
      expect(result).toBe('done');
    });

    it('rejects immediately when the signal is already aborted before the race starts', async () => {
      const controller = new AbortController();
      controller.abort();
      await expect(raceAbort(Promise.resolve('done'), controller.signal)).rejects.toThrow(
        DOMException
      );
    });
  });

  describe('raceAbort — long-running operation interrupt path', () => {
    it('interrupts a long-running operation when the signal is aborted mid-flight', async () => {
      const controller = new AbortController();

      // Simulate a long-running operation (resolves after 500 ms)
      const longRunning = new Promise<string>(resolve => {
        setTimeout(() => resolve('finished'), 500);
      });

      // Abort after a short tick — before the operation resolves
      setTimeout(() => controller.abort(), 10);

      await expect(raceAbort(longRunning, controller.signal)).rejects.toMatchObject({
        name: 'AbortError'
      });
    });

    it('resolves with the operation result when it finishes before the signal fires', async () => {
      const controller = new AbortController();

      // Short operation resolves quickly
      const quickOp = Promise.resolve('result');

      // Signal fires much later — after the operation is already done
      setTimeout(() => controller.abort(), 200);

      const result = await raceAbort(quickOp, controller.signal);
      expect(result).toBe('result');
    });

    it('propagates non-abort errors from the wrapped promise', async () => {
      const controller = new AbortController();
      const failing = Promise.reject(new Error('network error'));
      await expect(raceAbort(failing, controller.signal)).rejects.toThrow('network error');
    });
  });

  describe('interrupt hint — running-state UX guidance', () => {
    it('interrupt hint references Ctrl+C (not Esc)', () => {
      const hint = buildInterruptHint();
      expect(hint).toContain('Ctrl+C');
      expect(hint.toLowerCase()).not.toContain('esc');
    });

    it('raceAbort throws an AbortError when SIGINT is simulated via AbortController during a long-running flow', async () => {
      const controller = new AbortController();

      // Simulate a long-running operation (resolves after 400 ms)
      const longRunning = new Promise<string>(resolve => {
        setTimeout(() => resolve('done'), 400);
      });

      // Simulate Ctrl+C (SIGINT) by aborting the controller after a short delay
      setTimeout(() => controller.abort(), 20);

      await expect(raceAbort(longRunning, controller.signal)).rejects.toMatchObject({
        name: 'AbortError'
      });
    });
  });
});
