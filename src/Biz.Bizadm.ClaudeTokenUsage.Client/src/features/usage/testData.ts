import type { UsageMeta, UsageSeries } from "./types";

export const sampleMeta: UsageMeta = {
  now: "2026-11-20T14:37:12+09:00",
  units: [
    { unit: "minute", earliest: "2026-11-19T14:37:00+09:00" },
    { unit: "hour", earliest: "2026-11-13T14:00:00+09:00" },
    { unit: "day", earliest: "2026-10-20T00:00:00+09:00" },
    { unit: "week", earliest: "2025-11-17T00:00:00+09:00" },
    { unit: "month", earliest: "2021-11-01T00:00:00+09:00" },
    { unit: "year", earliest: null },
  ],
  users: ["a@example.com"],
  models: ["claude-opus-5-5"],
  tokenTypes: ["input", "output"],
};

export const sampleSeries: UsageSeries = {
  unit: "hour",
  from: "2026-11-20T10:00:00+09:00",
  to: "2026-11-20T12:00:00+09:00",
  groupBy: "tokenType",
  series: ["input", "output"],
  buckets: [
    { start: "2026-11-20T10:00:00+09:00", tokens: { input: 100, output: 30 }, totalTokens: 130, costUsd: 0.25 },
    { start: "2026-11-20T11:00:00+09:00", tokens: {}, totalTokens: 0, costUsd: 0 },
  ],
  totals: { tokens: 130, costUsd: 0.25 },
};
