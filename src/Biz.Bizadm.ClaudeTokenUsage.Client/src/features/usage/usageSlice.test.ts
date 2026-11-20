import { createStore } from "@/app/store";
import { buildSeriesParams } from "./api";
import { sampleMeta, sampleSeries } from "./testData";
import reducer, { loadMeta, loadSeries, setGroupBy, setPeriod, setUnit, toggleFilter } from "./usageSlice";

describe("usageSlice", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("builds request parameters from filters", () => {
    const store = createStore();
    store.dispatch(setPeriod({ from: "2026-11-20T10:00", to: "2026-11-20T12:00" }));
    store.dispatch(setGroupBy("model"));
    store.dispatch(toggleFilter({ list: "users", value: "a@example.com" }));
    store.dispatch(toggleFilter({ list: "tokenTypes", value: "input" }));
    store.dispatch(toggleFilter({ list: "tokenTypes", value: "output" }));
    store.dispatch(toggleFilter({ list: "tokenTypes", value: "input" })); // 다시 누르면 해제

    const params = buildSeriesParams(store.getState().usage.filters);

    expect(params.get("unit")).toBe("hour");
    expect(params.get("from")).toBe("2026-11-20T10:00:00+09:00");
    expect(params.get("to")).toBe("2026-11-20T12:00:00+09:00");
    expect(params.get("groupBy")).toBe("model");
    expect(params.getAll("user")).toEqual(["a@example.com"]);
    expect(params.getAll("tokenType")).toEqual(["output"]);
    expect(params.getAll("model")).toEqual([]);
  });

  it("uses the default period from the current time and clamps to retention", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-20T14:37:12+09:00"));
    let state = reducer(undefined, loadMeta.fulfilled(sampleMeta, "req", undefined));
    state = reducer(state, setUnit("minute"));
    expect(state.filters).toMatchObject({ unit: "minute", from: "2026-11-20T13:38", to: "2026-11-20T14:38" });

    state = reducer(state, setPeriod({ from: "2026-11-01T00:00", to: "2026-11-20T14:38" }));
    expect(state.filters.from).toBe("2026-11-19T14:37"); // 분 보관 하한

    // 화면을 3시간 열어 둔 뒤 단위를 바꿔도 지금 기준 기간이 된다(meta.now는 그대로).
    vi.setSystemTime(new Date("2026-11-20T17:37:12+09:00"));
    state = reducer(state, setUnit("hour"));
    expect(state.filters).toMatchObject({ from: "2026-11-19T17:38", to: "2026-11-20T17:38" });
  });

  it("ignores a stale series response that arrives after a newer one", async () => {
    const resolvers: ((response: Response) => void)[] = [];
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>((resolve) => resolvers.push(resolve))));
    const store = createStore();
    const filters = store.getState().usage.filters;

    const first = store.dispatch(loadSeries(filters));
    const second = store.dispatch(loadSeries({ ...filters, groupBy: "model" }));
    const newer = { ...sampleSeries, groupBy: "model" as const, totals: { tokens: 2, costUsd: 0 } };
    resolvers[1](new Response(JSON.stringify(newer), { status: 200 }));
    await second;
    resolvers[0](new Response(JSON.stringify(sampleSeries), { status: 200 }));
    await first;

    expect(store.getState().usage.series?.groupBy).toBe("model");
    expect(store.getState().usage.series?.totals.tokens).toBe(2);
    expect(store.getState().usage.status).toBe("idle");
  });
});
