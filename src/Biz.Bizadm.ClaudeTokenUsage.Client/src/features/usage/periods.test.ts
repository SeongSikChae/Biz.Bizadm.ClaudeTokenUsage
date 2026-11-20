import { clampFrom, defaultPeriod, isoToKstWall, kstWallToIso, shiftPeriodToNow } from "./periods";
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

  it("shifts a period to now keeping its length", () => {
    // 2시간 30분 기간을 지금(14:37:12)의 다음 분 경계 14:38로 옮긴다.
    expect(shiftPeriodToNow("2026-11-20T09:00", "2026-11-20T11:30", NOW, null)).toEqual({
      from: "2026-11-20T12:08",
      to: "2026-11-20T14:38",
    });
    // 같은 분 안에서 다시 옮기면 그대로다.
    expect(shiftPeriodToNow("2026-11-20T12:08", "2026-11-20T14:38", "2026-11-20T14:37:55+09:00", null)).toEqual({
      from: "2026-11-20T12:08",
      to: "2026-11-20T14:38",
    });
  });

  it("clamps a shifted period to the retention lower bound", () => {
    expect(shiftPeriodToNow("2026-11-10T00:00", "2026-11-20T00:00", NOW, "2026-11-13T14:00:00+09:00")).toEqual({
      from: "2026-11-13T14:00",
      to: "2026-11-20T14:38",
    });
  });

  it("clamps start to the retention lower bound", () => {
    expect(clampFrom("2025-01-01T00:00", "2025-11-17T00:00:00+09:00")).toBe("2025-11-17T00:00");
    expect(clampFrom("2026-01-01T00:00", "2025-11-17T00:00:00+09:00")).toBe("2026-01-01T00:00");
    expect(clampFrom("1999-01-01T00:00", null)).toBe("1999-01-01T00:00");
  });
});
