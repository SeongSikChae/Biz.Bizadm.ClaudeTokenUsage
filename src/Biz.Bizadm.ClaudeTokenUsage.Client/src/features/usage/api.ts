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

/** 조회 시간 제한. 서버가 응답 없이 멈춰도 실패로 보이게 한다. */
const REQUEST_TIMEOUT_MS = 15_000;
export const TIMEOUT_MESSAGE = "응답 시간이 초과되었습니다";

function isTimeout(error: unknown): boolean {
  return (error as { name?: unknown } | null)?.name === "TimeoutError";
}

async function getJson<T>(url: string): Promise<T> {
  try {
    return await requestJson<T>(url);
  } catch (error) {
    // 시간 제한 신호는 응답 본문을 읽는 중에도 걸리므로 본문 읽기까지 함께 감싼다.
    if (isTimeout(error)) throw new Error(TIMEOUT_MESSAGE);
    throw error;
  }
}

async function requestJson<T>(url: string): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: "application/json" },
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
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
