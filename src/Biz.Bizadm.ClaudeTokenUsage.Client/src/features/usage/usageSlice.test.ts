import { createStore } from "@/app/store";
import { buildSeriesParams, TIMEOUT_MESSAGE } from "./api";
import { sampleMeta, sampleSeries } from "./testData";
import type { UsageFilters, UsageSeries } from "./types";
import reducer, { loadMeta, loadSeries, refresh, setGroupBy, setLive, setPeriod, setUnit, toggleFilter, toggleHidden } from "./usageSlice";

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

  const filters = reducer(undefined, { type: "init" }).filters;
  const fulfilled = (series: UsageSeries, arg: UsageFilters = filters, requestId = "r1") => loadSeries.fulfilled(series, requestId, arg);
  const pending = (arg: UsageFilters = filters, requestId = "r1") => loadSeries.pending(requestId, arg);

  it("starts live and stops live when the period is chosen", () => {
    let state = reducer(undefined, { type: "init" });
    expect(state.live).toBe(true);
    state = reducer(state, setPeriod({ from: "2026-11-20T10:00", to: "2026-11-20T12:00" }));
    expect(state.live).toBe(false);
  });

  it("refresh shifts the period to now only when live and always bumps the sequence", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-20T14:37:12+09:00"));
    let state = reducer(undefined, setPeriod({ from: "2026-11-20T09:00", to: "2026-11-20T11:00" }));
    state = reducer(state, refresh());
    expect(state.filters).toMatchObject({ from: "2026-11-20T09:00", to: "2026-11-20T11:00" });
    expect(state.refreshSeq).toBe(1);

    state = reducer(state, setLive(true)); // 켜면 곧바로 옮기고 한 번 조회
    expect(state.filters).toMatchObject({ from: "2026-11-20T12:38", to: "2026-11-20T14:38" });
    expect(state.refreshSeq).toBe(2);

    vi.setSystemTime(new Date("2026-11-20T14:45:00+09:00"));
    state = reducer(state, refresh());
    expect(state.filters).toMatchObject({ from: "2026-11-20T12:46", to: "2026-11-20T14:46" });
    expect(state.refreshSeq).toBe(3);

    state = reducer(state, setLive(false));
    expect(state.live).toBe(false);
    expect(state.refreshSeq).toBe(3);
  });

  it("records the last success time and keeps it and the error until the next success", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-20T05:00:00Z"));
    let state = reducer(undefined, pending());
    state = reducer(state, fulfilled(sampleSeries));
    expect(state.lastSuccessAt).toBe("2026-11-20T05:00:00.000Z");

    vi.setSystemTime(new Date("2026-11-20T05:00:10Z"));
    state = reducer(state, pending(filters, "r2"));
    state = reducer(state, loadSeries.rejected(new Error("서버 오류"), "r2", filters));
    expect(state.error).toBe("서버 오류");
    expect(state.lastSuccessAt).toBe("2026-11-20T05:00:00.000Z");

    state = reducer(state, pending(filters, "r3"));
    expect(state.error).toBe("서버 오류"); // 요청을 보낼 때는 지우지 않는다
    state = reducer(state, fulfilled(sampleSeries, filters, "r3"));
    expect(state.error).toBeNull();
    expect(state.lastSuccessAt).toBe("2026-11-20T05:00:10.000Z");
  });

  it("reports a request timeout as an error", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new DOMException("signal timed out", "TimeoutError"))));
    const store = createStore();
    await store.dispatch(loadSeries(store.getState().usage.filters));
    expect(store.getState().usage.error).toBe(TIMEOUT_MESSAGE);
  });

  it("keeps colors across refreshes and reassigns them when grouping or filters change", () => {
    const first = { ...sampleSeries, series: ["input", "output"] };
    let state = reducer(undefined, pending());
    state = reducer(state, fulfilled(first));
    expect(state.colorIndex).toEqual({ input: 0, output: 1 });

    // 기간만 바뀐 갱신: 색을 유지하고 앞에 끼어든 새 계열은 빈 색을 받는다.
    const shifted = { ...filters, from: "2026-11-20T00:00" };
    state = reducer(state, pending(shifted, "r2"));
    state = reducer(state, fulfilled({ ...sampleSeries, series: ["cacheRead", "input", "output"] }, shifted, "r2"));
    expect(state.colorIndex).toEqual({ cacheRead: 2, input: 0, output: 1 });

    // 나눠 볼 기준이 바뀌면 응답 순서대로 다시 매긴다.
    const byModel = { ...filters, groupBy: "model" as const };
    state = reducer(state, pending(byModel, "r3"));
    state = reducer(state, fulfilled({ ...sampleSeries, series: ["m1", "m2"] }, byModel, "r3"));
    expect(state.colorIndex).toEqual({ m1: 0, m2: 1 });

    // 필터가 바뀌어도 다시 매긴다.
    const filtered = { ...byModel, users: ["a@example.com"] };
    state = reducer(state, pending(filtered, "r4"));
    state = reducer(state, fulfilled({ ...sampleSeries, series: ["m2"] }, filtered, "r4"));
    expect(state.colorIndex).toEqual({ m2: 0 });
  });

  it("toggles hidden series by name and keeps them across responses", () => {
    let state = reducer(undefined, toggleHidden("output"));
    expect(state.hidden).toEqual(["output"]);
    state = reducer(state, pending());
    state = reducer(state, fulfilled({ ...sampleSeries, series: ["input"] }));
    expect(state.hidden).toEqual(["output"]);
    state = reducer(state, toggleHidden("output"));
    expect(state.hidden).toEqual([]);
  });
});
