import type { UsageUnit } from "./types";

const KST_OFFSET_MS = 9 * 60 * 60 * 1000;

/**
 * KST 벽시계 문자열(`YYYY-MM-DDTHH:mm`)을 UTC로 읽은 Date로 바꾼다.
 * 달력 계산(월·년 빼기)을 브라우저 시간대와 상관없이 하기 위해 쓴다.
 */
function wallToDate(wall: string): Date {
  return new Date(`${wall}:00Z`);
}

function dateToWall(date: Date): string {
  return date.toISOString().slice(0, 16);
}

/** 오프셋이 있는 ISO 시각을 KST 벽시계 문자열로 바꾼다. */
export function isoToKstWall(iso: string): string {
  return dateToWall(new Date(new Date(iso).getTime() + KST_OFFSET_MS));
}

/** KST 벽시계 문자열을 API에 보낼 `+09:00` ISO 시각으로 바꾼다. */
export function kstWallToIso(wall: string): string {
  return `${wall}:00+09:00`;
}

/**
 * 단위를 고르면 쓰는 기본 기간: 분 60분, 시 24시간, 일 14일, 주 12주, 월 12개월, 년 5년.
 * 끝은 지금이 속한 분의 다음 분 경계로 올려, 진행 중인 현재 구간이 들어가게 한다.
 */
export function defaultPeriod(unit: UsageUnit, nowIso: string): { from: string; to: string } {
  const end = wallToDate(isoToKstWall(nowIso));
  end.setUTCMinutes(end.getUTCMinutes() + 1);
  const to = dateToWall(end);
  const from = wallToDate(to);
  switch (unit) {
    case "minute":
      from.setUTCMinutes(from.getUTCMinutes() - 60);
      break;
    case "hour":
      from.setUTCHours(from.getUTCHours() - 24);
      break;
    case "day":
      from.setUTCDate(from.getUTCDate() - 14);
      break;
    case "week":
      from.setUTCDate(from.getUTCDate() - 84);
      break;
    case "month":
      from.setUTCMonth(from.getUTCMonth() - 12);
      break;
    case "year":
      from.setUTCFullYear(from.getUTCFullYear() - 5);
      break;
  }
  return { from: dateToWall(from), to };
}

/** 보관 하한(`earliest`, null이면 영구)보다 이른 시작을 하한으로 올린다. */
export function clampFrom(from: string, earliestIso: string | null | undefined): string {
  if (!earliestIso) return from;
  const earliest = isoToKstWall(earliestIso);
  return from < earliest ? earliest : from;
}
