import "@testing-library/jest-dom/vitest";

// jsdom에는 ResizeObserver가 없어 recharts ResponsiveContainer가 실패하므로 빈 구현을 둔다.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;
