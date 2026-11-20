import { createAsyncThunk, createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { fetchMeta, fetchSeries } from "./api";
import { clampFrom, defaultPeriod, shiftPeriodToNow } from "./periods";
import { assignColors } from "./seriesColors";
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
  /** 실시간 갱신. 켜져 있으면 갱신마다 기간을 지금 기준으로 옮긴다. */
  live: boolean;
  /** 새로고침·자동 갱신마다 1씩 늘린다. 조회 effect가 이 값과 필터에 걸려 정확히 한 번 조회한다. */
  refreshSeq: number;
  /** 마지막으로 성공한 조회 시각(ISO). 실패하면 그대로 둔다. */
  lastSuccessAt: string | null;
  /** 계열 이름 → 팔레트 번호 */
  colorIndex: Record<string, number>;
  /** 색을 매긴 요청의 나눠 볼 기준·필터. 이것이 바뀌면 색을 처음부터 다시 매긴다. */
  colorKey: string | null;
  /** 범례로 숨긴 계열 이름. 계열 이름에 묶이며 응답이 바뀌어도 지우지 않는다. */
  hidden: string[];
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
  live: true,
  refreshSeq: 0,
  lastSuccessAt: null,
  colorIndex: {},
  colorKey: null,
  hidden: [],
};

function earliestOf(state: UsageState, unit: UsageUnit): string | null | undefined {
  return state.meta?.units.find((entry) => entry.unit === unit)?.earliest;
}

/** 색 배정을 다시 시작해야 하는 조건(나눠 볼 기준과 사용자·모델·토큰 종류 필터). 기간은 넣지 않는다. */
function colorKeyOf(filters: UsageFilters): string {
  return JSON.stringify([filters.groupBy, filters.users, filters.models, filters.tokenTypes]);
}

/** 실시간이면 기간을 지금 기준으로 옮기고, 조회 effect가 한 번 돌도록 refreshSeq를 올린다. */
function requestRefresh(state: UsageState): void {
  if (state.live) {
    const shifted = shiftPeriodToNow(state.filters.from, state.filters.to, new Date().toISOString(), earliestOf(state, state.filters.unit));
    state.filters.from = shifted.from;
    state.filters.to = shifted.to;
  }
  state.refreshSeq += 1;
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
    /** 사용자가 기간을 직접 고르면 그 기간을 지키도록 실시간을 끈다. */
    setPeriod(state, action: PayloadAction<{ from: string; to: string }>) {
      state.filters.from = clampFrom(action.payload.from, earliestOf(state, state.filters.unit));
      state.filters.to = action.payload.to;
      state.live = false;
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
    /** 켜면 곧바로 기간을 지금 기준으로 옮겨 한 번 조회한다. */
    setLive(state, action: PayloadAction<boolean>) {
      state.live = action.payload;
      if (action.payload) requestRefresh(state);
    },
    /** 새로고침 버튼과 10초 자동 갱신이 함께 쓴다. */
    refresh(state) {
      requestRefresh(state);
    },
    toggleHidden(state, action: PayloadAction<string>) {
      const index = state.hidden.indexOf(action.payload);
      if (index >= 0) state.hidden.splice(index, 1);
      else state.hidden.push(action.payload);
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(loadMeta.fulfilled, (state, action) => {
        state.meta = action.payload;
        state.filters.from = clampFrom(state.filters.from, earliestOf(state, state.filters.unit));
      })
      .addCase(loadSeries.pending, (state, action) => {
        // 오류는 지우지 않는다. 실시간 갱신 중 실패가 이어져도 오류가 깜빡이지 않고 성공할 때만 사라진다.
        state.status = "loading";
        state.seriesRequestId = action.meta.requestId;
      })
      .addCase(loadSeries.fulfilled, (state, action) => {
        if (action.meta.requestId !== state.seriesRequestId) return;
        const key = colorKeyOf(action.meta.arg);
        state.colorIndex = assignColors(state.colorIndex, action.payload.series, key !== state.colorKey);
        state.colorKey = key;
        state.status = "idle";
        state.error = null;
        state.series = action.payload;
        state.lastSuccessAt = new Date().toISOString();
      })
      .addCase(loadSeries.rejected, (state, action) => {
        if (action.meta.requestId !== state.seriesRequestId) return;
        state.status = "failed";
        state.error = action.error.message ?? "조회에 실패했습니다.";
      });
  },
});

export const { setUnit, setPeriod, setGroupBy, toggleFilter, setLive, refresh, toggleHidden } = usageSlice.actions;
export default usageSlice.reducer;
