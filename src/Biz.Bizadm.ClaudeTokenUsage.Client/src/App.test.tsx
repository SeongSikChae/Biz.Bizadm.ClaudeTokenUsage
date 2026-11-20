import { act, fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import type { Mock } from "vitest";
import App from "./App";
import { createStore } from "./app/store";
import { TIMEOUT_MESSAGE } from "./features/usage/api";
import { sampleMeta, sampleSeries } from "./features/usage/testData";
import { init } from "./lib/echarts";
import { initTheme, setTheme } from "./lib/theme";

type FetchMock = Mock<(url: string) => Promise<Response>>;

function json(body: unknown): Promise<Response> {
  return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
}

/** 메타는 늘 응답하고, 조회는 seriesResponder가 정한 대로 응답한다. */
function stubFetch(seriesResponder: (call: number) => Promise<Response> = () => json(sampleSeries)): FetchMock {
  let seriesCalls = 0;
  const mock: FetchMock = vi.fn((url: string) => (url.startsWith("/api/usage/meta") ? json(sampleMeta) : seriesResponder(seriesCalls++)));
  vi.stubGlobal("fetch", mock);
  return mock;
}

function seriesCallCount(mock: FetchMock): number {
  return mock.mock.calls.filter(([url]) => url.startsWith("/api/usage/series")).length;
}

function renderApp() {
  render(
    <Provider store={createStore()}>
      <App />
    </Provider>,
  );
}

/** 가짜 타이머에서 시간을 흘리고 그 사이 끝난 fetch·상태 갱신을 반영한다. */
async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe("App", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("renders the usage screen with header, filters, totals and chart", async () => {
    const fetchMock = stubFetch();
    renderApp();

    expect(screen.getByRole("heading", { name: "Claude Code 토큰 사용량" })).toBeInTheDocument();
    expect(await screen.findAllByText("130")).not.toHaveLength(0);
    expect(screen.getByText("구간별 사용량")).toBeInTheDocument();
    expect(await screen.findByText("a@example.com")).toBeInTheDocument();
    expect(screen.getByText(/마지막 조회 \d{2}-\d{2} \d{2}:\d{2}:\d{2} KST/)).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(expect.stringMatching(/^\/api\/usage\/series\?unit=hour/), expect.anything());
  });

  it("queries again when the refresh button is pressed", async () => {
    const fetchMock = stubFetch();
    renderApp();
    await screen.findAllByText("130");
    const before = seriesCallCount(fetchMock);

    fireEvent.click(screen.getByRole("button", { name: "새로고침" }));
    await vi.waitFor(() => expect(seriesCallCount(fetchMock)).toBe(before + 1));
  });

  it("refreshes every 10 seconds while live and stops when live is turned off", async () => {
    vi.useFakeTimers();
    const fetchMock = stubFetch();
    renderApp();
    await advance(0);
    const start = seriesCallCount(fetchMock);

    await advance(10_000);
    expect(seriesCallCount(fetchMock)).toBe(start + 1);
    await advance(10_000);
    expect(seriesCallCount(fetchMock)).toBe(start + 2);

    fireEvent.click(screen.getByRole("button", { name: "실시간" }));
    expect(screen.getByRole("button", { name: "실시간" })).toHaveAttribute("aria-pressed", "false");
    await advance(30_000);
    expect(seriesCallCount(fetchMock)).toBe(start + 2);
  });

  it("skips live ticks while a query is pending so a timeout shows as an error", async () => {
    vi.useFakeTimers();
    let rejectHung: (reason: unknown) => void = () => {};
    const fetchMock = stubFetch((call) =>
      call === 0 ? json(sampleSeries) : new Promise<Response>((_, reject) => (rejectHung = reject)),
    );
    renderApp();
    await advance(0);
    const start = seriesCallCount(fetchMock);

    await advance(10_000); // 응답하지 않는 조회가 나감
    expect(seriesCallCount(fetchMock)).toBe(start + 1);
    await advance(30_000); // 조회 중이라 주기를 건너뜀
    expect(seriesCallCount(fetchMock)).toBe(start + 1);

    await act(async () => {
      rejectHung(new DOMException("signal timed out", "TimeoutError"));
    });
    expect(screen.getByRole("alert")).toHaveTextContent(TIMEOUT_MESSAGE);
  });

  describe("theme", () => {
    const originalMatchMedia = window.matchMedia;

    afterEach(() => {
      window.matchMedia = originalMatchMedia;
      window.localStorage.clear();
      initTheme();
      document.head.querySelectorAll("style[data-test-theme]").forEach((style) => style.remove());
    });

    it("toggles the theme, stores the choice and redraws the chart with the new colors", async () => {
      window.localStorage.clear();
      initTheme();
      const style = document.createElement("style");
      style.dataset.testTheme = "";
      style.textContent = ":root { --series-1: #111111; } .dark { --series-1: #222222; }";
      document.head.append(style);
      stubFetch();
      renderApp();
      await screen.findByRole("button", { name: "input" });

      fireEvent.click(screen.getByRole("button", { name: "다크 모드로 바꾸기" }));

      expect(document.documentElement).toHaveClass("dark");
      expect(window.localStorage.getItem("theme")).toBe("dark");
      const results = vi.mocked(init).mock.results;
      const calls = (results[results.length - 1].value as { setOption: Mock }).setOption.mock.calls;
      const option = calls[calls.length - 1][0] as { series: { name: string; itemStyle: { color: string } }[] };
      // 토글 한 번에 차트가 새 테마의 변수 값을 읽는다(클래스를 먼저 바꾼 뒤 알리므로).
      expect(option.series.find((item) => item.name === "input")?.itemStyle.color).toBe("#222222");
      expect(screen.getByRole("button", { name: "라이트 모드로 바꾸기" })).toBeInTheDocument();
    });

    it("follows the operating system until a theme is chosen, without storing it", () => {
      let listener: (event: { matches: boolean }) => void = () => {};
      window.matchMedia = ((query: string) => ({
        matches: true,
        media: query,
        addEventListener: (_: string, handler: (event: { matches: boolean }) => void) => (listener = handler),
        removeEventListener: () => {},
      })) as unknown as typeof window.matchMedia;
      window.localStorage.clear();

      initTheme();
      expect(document.documentElement).toHaveClass("dark");

      listener({ matches: false });
      expect(document.documentElement).not.toHaveClass("dark");
      expect(window.localStorage.getItem("theme")).toBeNull();

      setTheme("dark");
      listener({ matches: false }); // 고른 값이 있으면 운영체제 변경을 무시한다
      expect(document.documentElement).toHaveClass("dark");
      expect(window.localStorage.getItem("theme")).toBe("dark");
    });
  });
});
