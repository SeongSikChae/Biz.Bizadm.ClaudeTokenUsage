import "@testing-library/jest-dom/vitest";

// jsdom에는 ResizeObserver가 없어 차트 크기 감시가 실패하므로 빈 구현을 둔다.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;

// jsdom에는 matchMedia가 없다. 운영체제 테마는 기본으로 라이트로 본다(테스트에서 바꿔 쓸 수 있음).
if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

// jsdom에는 canvas가 없어 ECharts가 그리지 못하므로 인스턴스를 가짜로 바꾼다.
// 테스트는 vi.mocked(init).mock.results로 마지막 인스턴스의 setOption 호출을 확인한다.
vi.mock("@/lib/echarts", () => ({
  init: vi.fn(() => ({
    setOption: vi.fn(),
    resize: vi.fn(),
    dispose: vi.fn(),
    dispatchAction: vi.fn(),
  })),
}));
