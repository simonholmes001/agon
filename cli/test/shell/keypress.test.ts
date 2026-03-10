import { describe, expect, it, vi } from 'vitest';
import { createKeypressInitializer } from '../../src/shell/keypress.js';

describe('shell keypress initializer', () => {
  it('emits keypress events only once per initializer instance', () => {
    const emit = vi.fn();
    const initialize = createKeypressInitializer(
      {} as NodeJS.ReadableStream,
      emit
    );

    initialize();
    initialize();
    initialize();

    expect(emit).toHaveBeenCalledTimes(1);
  });

  it('keeps initializers independent', () => {
    const emit = vi.fn();
    const first = createKeypressInitializer({} as NodeJS.ReadableStream, emit);
    const second = createKeypressInitializer({} as NodeJS.ReadableStream, emit);

    first();
    second();

    expect(emit).toHaveBeenCalledTimes(2);
  });
});
