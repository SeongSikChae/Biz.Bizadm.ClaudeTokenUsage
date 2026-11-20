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
