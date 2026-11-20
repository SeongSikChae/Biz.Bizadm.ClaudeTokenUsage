import { createAsyncThunk, createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { fetchMeta, fetchSeries } from "./api";
import { clampFrom, defaultPeriod } from "./periods";
import type { GroupBy, UsageFilters, UsageMeta, UsageSeries, UsageUnit } from "./types";

type ListFilter = "users" | "models" | "tokenTypes";

export interface UsageState {
  filters: UsageFilters;
  meta: UsageMeta | null;
  series: UsageSeries | null;
  status: "idle" | "loading" | "failed";
  error: string | null;
  /** 마지막으로 보낸 조회 요청. 늦게 도착한 이전 응답이 최신 결과를 덮어쓰지 않게 이 요청의 결과만 반영한다. */
  seriesRequestId: string | null;
}

function initialFilters(): UsageFilters {
  return { unit: "hour", ...defaultPeriod("hour", new Date().toISOString()), groupBy: "tokenType", users: [], models: [], tokenTypes: [] };
}

const initialState: UsageState = {
  filters: initialFilters(),
  meta: null,
  series: null,
  status: "idle",
  error: null,
  seriesRequestId: null,
};

function earliestOf(state: UsageState, unit: UsageUnit): string | null | undefined {
  return state.meta?.units.find((entry) => entry.unit === unit)?.earliest;
}

export const loadMeta = createAsyncThunk("usage/loadMeta", () => fetchMeta());

export const loadSeries = createAsyncThunk("usage/loadSeries", (filters: UsageFilters) => fetchSeries(filters));

const usageSlice = createSlice({
  name: "usage",
  initialState,
  reducers: {
    /**
     * 단위를 바꾸면 그 단위의 기본 기간(지금 기준)을 쓰고 보관 하한으로 자른다.
     * 화면을 오래 열어 두어도 "최근" 기간이 되도록 meta.now가 아니라 지금 시각을 쓴다.
     */
    setUnit(state, action: PayloadAction<UsageUnit>) {
      const unit = action.payload;
      const period = defaultPeriod(unit, new Date().toISOString());
      state.filters.unit = unit;
      state.filters.from = clampFrom(period.from, earliestOf(state, unit));
      state.filters.to = period.to;
    },
    setPeriod(state, action: PayloadAction<{ from: string; to: string }>) {
      state.filters.from = clampFrom(action.payload.from, earliestOf(state, state.filters.unit));
      state.filters.to = action.payload.to;
    },
    setGroupBy(state, action: PayloadAction<GroupBy>) {
      state.filters.groupBy = action.payload;
    },
    toggleFilter(state, action: PayloadAction<{ list: ListFilter; value: string }>) {
      const values = state.filters[action.payload.list];
      const index = values.indexOf(action.payload.value);
      if (index >= 0) values.splice(index, 1);
      else values.push(action.payload.value);
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(loadMeta.fulfilled, (state, action) => {
        state.meta = action.payload;
        state.filters.from = clampFrom(state.filters.from, earliestOf(state, state.filters.unit));
      })
      .addCase(loadSeries.pending, (state, action) => {
        state.status = "loading";
        state.error = null;
        state.seriesRequestId = action.meta.requestId;
      })
      .addCase(loadSeries.fulfilled, (state, action) => {
        if (action.meta.requestId !== state.seriesRequestId) return;
        state.status = "idle";
        state.series = action.payload;
      })
      .addCase(loadSeries.rejected, (state, action) => {
        if (action.meta.requestId !== state.seriesRequestId) return;
        state.status = "failed";
        state.error = action.error.message ?? "조회에 실패했습니다.";
      });
  },
});

export const { setUnit, setPeriod, setGroupBy, toggleFilter } = usageSlice.actions;
export default usageSlice.reducer;
