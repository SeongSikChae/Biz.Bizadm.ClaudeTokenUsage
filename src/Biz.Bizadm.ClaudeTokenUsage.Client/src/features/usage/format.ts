import { isoToKstWall } from "./periods";
import type { UsageUnit } from "./types";

/** 구간 시작을 단위에 맞는 KST 라벨로 바꾼다. */
export function formatBucketLabel(unit: UsageUnit, startIso: string): string {
  const wall = isoToKstWall(startIso); // YYYY-MM-DDTHH:mm
  switch (unit) {
    case "minute":
    case "hour":
      return `${wall.slice(5, 10)} ${wall.slice(11, 16)}`;
    case "day":
    case "week":
      return wall.slice(0, 10);
    case "month":
      return wall.slice(0, 7);
    case "year":
      return wall.slice(0, 4);
  }
}

export function formatUsd(value: number): string {
  return `$${value.toFixed(4)}`;
}

const compactFormat = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });

/** 축 눈금용 줄임 표기. 160000 → "160K", 1500000 → "1.5M". */
export function formatCompact(value: number): string {
  return compactFormat.format(value);
}

/** 토큰 수를 천 단위 구분으로 쓴다. */
export function formatTokens(value: number): string {
  return value.toLocaleString("ko-KR");
}

/** 전체 대비 비율. 전체가 0이면 나눌 수 없으므로 "—"를 쓴다. */
export function formatPercent(part: number, total: number): string {
  if (total === 0) return "—";
  return `${((part / total) * 100).toFixed(1)}%`;
}

/** 조회 시각을 KST `MM-DD HH:mm:ss`로 쓴다. */
export function formatKstTime(iso: string): string {
  const kst = new Date(new Date(iso).getTime() + 9 * 60 * 60 * 1000).toISOString();
  return `${kst.slice(5, 10)} ${kst.slice(11, 19)}`;
}
