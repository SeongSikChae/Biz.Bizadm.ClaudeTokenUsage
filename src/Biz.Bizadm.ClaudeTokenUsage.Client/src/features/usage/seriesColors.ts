/** 계열 색 팔레트 크기(index.css의 --series-1 ~ --series-8). */
export const PALETTE_SIZE = 8;

/**
 * 계열 이름에 팔레트 번호(0 ~ 7)를 매긴다. 결과에는 이번 응답의 계열만 담는다(빠진 계열은 색을 잃는다).
 * - reset: 응답 순서대로 0, 1, 2…를 매기고 8개를 넘으면 처음부터 다시 쓴다.
 * - 아니면 이전에 매긴 번호를 먼저 모두 지키고, 새 계열에는 아직 쓰지 않은 가장 작은 번호를 준다.
 *   서버가 계열 이름을 정렬해 돌려주므로 새 이름이 기존 이름보다 앞에 올 수 있어, 두 단계로 나눠야 겹치지 않는다.
 */
export function assignColors(prev: Record<string, number>, series: string[], reset: boolean): Record<string, number> {
  const result: Record<string, number> = {};
  if (reset) {
    series.forEach((name, index) => {
      result[name] = index % PALETTE_SIZE;
    });
    return result;
  }
  for (const name of series) {
    if (Object.hasOwn(prev, name)) result[name] = prev[name];
  }
  for (const name of series) {
    if (Object.hasOwn(result, name)) continue;
    const used = new Set(Object.values(result));
    let pick = -1;
    for (let index = 0; index < PALETTE_SIZE; index++) {
      if (!used.has(index)) {
        pick = index;
        break;
      }
    }
    // 8색을 모두 쓰고 있으면 처음부터 다시 쓴다.
    result[name] = pick >= 0 ? pick : Object.keys(result).length % PALETTE_SIZE;
  }
  return result;
}
