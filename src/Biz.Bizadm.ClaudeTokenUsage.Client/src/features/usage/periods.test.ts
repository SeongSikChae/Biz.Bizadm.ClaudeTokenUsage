import { clampFrom, defaultPeriod, isoToKstWall, kstWallToIso } from "./periods";
import type { UsageUnit } from "./types";

const NOW = "2026-11-20T14:37:12+09:00";

describe("periods", () => {
  it("converts ISO to KST wall time regardless of offset", () => {
    expect(isoToKstWall("2026-11-20T05:00:00Z")).toBe("2026-11-20T14:00");
    expect(isoToKstWall("2026-11-20T15:00:00+09:00")).toBe("2026-11-20T15:00");
    expect(kstWallToIso("2026-11-20T15:00")).toBe("2026-11-20T15:00:00+09:00");
  });

  it.each<[UsageUnit, string]>([
    ["minute", "2026-11-20T13:38"],
    ["hour", "2026-11-19T14:38"],
    ["day", "2026-11-06T14:38"],
    ["week", "2026-08-28T14:38"],
    ["month", "2025-11-20T14:38"],
    ["year", "2021-11-20T14:38"],
  ])("default period for %s ends at the next minute boundary and starts at %s", (unit, from) => {
    // 끝은 14:37이 속한 분의 다음 경계(14:38)라 진행 중인 현재 분이 포함된다.
    expect(defaultPeriod(unit, NOW)).toEqual({ from, to: "2026-11-20T14:38" });
  });

  it("clamps start to the retention lower bound", () => {
    expect(clampFrom("2025-01-01T00:00", "2025-11-17T00:00:00+09:00")).toBe("2025-11-17T00:00");
    expect(clampFrom("2026-01-01T00:00", "2025-11-17T00:00:00+09:00")).toBe("2026-01-01T00:00");
    expect(clampFrom("1999-01-01T00:00", null)).toBe("1999-01-01T00:00");
  });
});
