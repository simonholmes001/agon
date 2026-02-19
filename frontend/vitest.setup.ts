import "@testing-library/jest-dom/vitest";

// Polyfill APIs missing from jsdom
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}

globalThis.ResizeObserver = globalThis.ResizeObserver ?? ResizeObserverStub;
