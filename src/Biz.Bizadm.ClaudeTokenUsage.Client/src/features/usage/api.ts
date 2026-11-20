import { kstWallToIso } from "./periods";
import type { UsageFilters, UsageMeta, UsageSeries } from "./types";

/** 조회 조건을 `GET /api/usage/series` 쿼리 문자열로 바꾼다. */
export function buildSeriesParams(filters: UsageFilters): URLSearchParams {
  const params = new URLSearchParams({
    unit: filters.unit,
    from: kstWallToIso(filters.from),
    to: kstWallToIso(filters.to),
    groupBy: filters.groupBy,
  });
  filters.users.forEach((user) => params.append("user", user));
  filters.models.forEach((model) => params.append("model", model));
  filters.tokenTypes.forEach((tokenType) => params.append("tokenType", tokenType));
  return params;
}

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: "application/json" } });
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const problem = (await response.json()) as { detail?: string };
      if (problem.detail) detail = problem.detail;
    } catch {
      // 본문이 JSON이 아니면 상태 줄만 쓴다.
    }
    throw new Error(detail);
  }
  return (await response.json()) as T;
}

export function fetchSeries(filters: UsageFilters): Promise<UsageSeries> {
  return getJson<UsageSeries>(`/api/usage/series?${buildSeriesParams(filters).toString()}`);
}

export function fetchMeta(): Promise<UsageMeta> {
  return getJson<UsageMeta>("/api/usage/meta");
}
