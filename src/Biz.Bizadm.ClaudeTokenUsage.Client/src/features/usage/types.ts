export const USAGE_UNITS = ["minute", "hour", "day", "week", "month", "year"] as const;
export type UsageUnit = (typeof USAGE_UNITS)[number];

export const GROUP_BYS = ["tokenType", "model", "user"] as const;
export type GroupBy = (typeof GROUP_BYS)[number];

/** 조회 API의 구간 하나. 시각은 `+09:00` 오프셋이 붙은 ISO 문자열이다. */
export interface UsageBucket {
  start: string;
  tokens: Record<string, number>;
  totalTokens: number;
  costUsd: number | null;
}

/** `GET /api/usage/series` 응답. */
export interface UsageSeries {
  unit: UsageUnit;
  from: string;
  to: string;
  groupBy: GroupBy;
  series: string[];
  buckets: UsageBucket[];
  totals: { tokens: number; costUsd: number | null };
}

/** `GET /api/usage/meta` 응답. `earliest`가 null이면 영구 보관이다. */
export interface UsageMeta {
  now: string;
  units: { unit: UsageUnit; earliest: string | null }[];
  users: string[];
  models: string[];
  tokenTypes: string[];
}

/**
 * 화면의 조회 조건. `from`·`to`는 KST 벽시계 `YYYY-MM-DDTHH:mm`(datetime-local 입력값)이다.
 * 빈 필터 목록은 전체를 뜻한다.
 */
export interface UsageFilters {
  unit: UsageUnit;
  from: string;
  to: string;
  groupBy: GroupBy;
  users: string[];
  models: string[];
  tokenTypes: string[];
}

export const UNIT_LABELS: Record<UsageUnit, string> = {
  minute: "분",
  hour: "시",
  day: "일",
  week: "주",
  month: "월",
  year: "년",
};

export const GROUP_BY_LABELS: Record<GroupBy, string> = {
  tokenType: "토큰 종류",
  model: "모델",
  user: "사용자",
};
