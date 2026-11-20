import type { ChartOption } from "@/lib/echarts";
import type { Theme } from "@/lib/theme";
import { formatBucketLabel, formatCompact, formatTokens, formatUsd } from "./format";
import type { UsageSeries } from "./types";

/** ECharts에 넘기는 색. 모두 hex다(index.css의 --series-*, --chart-*). */
export interface ChartPalette {
  series: string[];
  cost: string;
  axis: string;
  grid: string;
}

export interface ChartOptionInput {
  data: UsageSeries;
  /** 계열 이름 → 팔레트 번호 */
  colorIndex: Record<string, number>;
  /** 범례로 숨긴 계열 이름 */
  hidden: string[];
  palette: ChartPalette;
  theme: Theme;
}

export interface BuiltChart {
  option: ChartOption;
  /** 비용 그리드를 그렸는지. 이 값이 바뀌면 옵션을 병합하지 않고 새로 그려야 한다. */
  hasCost: boolean;
}

export const COST_SERIES_NAME = "비용 (USD)";

/** 툴팁·확대 슬라이더처럼 ECharts 기본값이 밝은 색인 요소의 테마별 색. */
const THEME_COLORS: Record<Theme, { tooltipBg: string; tooltipBorder: string; text: string; zoomFill: string; zoomBorder: string }> = {
  light: { tooltipBg: "#ffffff", tooltipBorder: "#e5e5e5", text: "#171717", zoomFill: "#dbeafe", zoomBorder: "#d4d4d4" },
  dark: { tooltipBg: "#262626", tooltipBorder: "#404040", text: "#fafafa", zoomFill: "#1e3a5f", zoomBorder: "#404040" },
};

const GRID_LEFT = 56;
const GRID_RIGHT = 16;

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (char) => `&#${char.charCodeAt(0)};`);
}

export function seriesColor(palette: ChartPalette, colorIndex: Record<string, number>, name: string): string {
  return palette.series[(colorIndex[name] ?? 0) % palette.series.length];
}

/**
 * 토큰 누적 막대(위)와 비용 막대(아래)를 한 인스턴스의 그리드 두 개로 그리는 옵션을 만든다.
 * 확대(dataZoom)와 마우스 위치 표시(axisPointer)는 두 x축에 함께 건다. 비용이 모두 null이면 그리드 하나만 쓴다.
 * dataZoom의 start/end는 넣지 않는다. 옵션을 병합해 갱신할 때 ECharts가 사용자가 고른 확대 비율을 유지하게 하기 위해서다.
 */
export function buildChartOption({ data, colorIndex, hidden, palette, theme }: ChartOptionInput): BuiltChart {
  const hasCost = data.buckets.some((bucket) => bucket.costUsd !== null);
  const colors = THEME_COLORS[theme];
  const labels = data.buckets.map((bucket) => formatBucketLabel(data.unit, bucket.start));
  const xAxisIndex = hasCost ? [0, 1] : [0];
  const axisLabel = { color: palette.axis };
  const axisLine = { lineStyle: { color: palette.grid } };
  const splitLine = { lineStyle: { color: palette.grid } };

  const tokenSeries = data.series.map((name) => ({
    type: "bar" as const,
    name,
    stack: "tokens",
    xAxisIndex: 0,
    yAxisIndex: 0,
    barMaxWidth: 40,
    itemStyle: { color: seriesColor(palette, colorIndex, name) },
    data: data.buckets.map((bucket) => bucket.tokens[name] ?? 0),
  }));
  const costSeries = hasCost
    ? [
        {
          type: "bar" as const,
          name: COST_SERIES_NAME,
          xAxisIndex: 1,
          yAxisIndex: 1,
          barMaxWidth: 40,
          itemStyle: { color: palette.cost },
          data: data.buckets.map((bucket) => bucket.costUsd ?? 0),
        },
      ]
    : [];

  const formatter = (params: unknown): string => {
    const first = (Array.isArray(params) ? params[0] : params) as { dataIndex?: number } | undefined;
    const index = first?.dataIndex;
    if (index === undefined) return "";
    const bucket = data.buckets[index];
    // 숨긴 계열은 막대에서 빠지므로 툴팁 목록과 합계에서도 뺀다(요약은 숨김과 무관).
    const visible = data.series.filter((name) => !hidden.includes(name));
    const rows = visible.map((name) => {
      const value = bucket.tokens[name] ?? 0;
      return (
        `<div style="display:flex;justify-content:space-between;gap:16px">` +
        `<span><span style="display:inline-block;width:8px;height:8px;border-radius:2px;margin-right:6px;background:${seriesColor(palette, colorIndex, name)}"></span>${escapeHtml(name)}</span>` +
        `<b>${formatTokens(value)}</b></div>`
      );
    });
    const total = visible.reduce((sum, name) => sum + (bucket.tokens[name] ?? 0), 0);
    const lines = [
      `<div style="margin-bottom:4px">${escapeHtml(labels[index])}</div>`,
      ...rows,
      `<div style="display:flex;justify-content:space-between;gap:16px;margin-top:4px"><span>합계</span><b>${formatTokens(total)}</b></div>`,
    ];
    if (bucket.costUsd !== null) {
      lines.push(`<div style="display:flex;justify-content:space-between;gap:16px"><span>비용</span><b>${formatUsd(bucket.costUsd)}</b></div>`);
    }
    return lines.join("");
  };

  const option: ChartOption = {
    animationDuration: 300,
    grid: hasCost
      ? [
          { left: GRID_LEFT, right: GRID_RIGHT, top: 16, height: 220 },
          { left: GRID_LEFT, right: GRID_RIGHT, top: 272, height: 72 },
        ]
      : [{ left: GRID_LEFT, right: GRID_RIGHT, top: 16, bottom: 72 }],
    xAxis: hasCost
      ? [
          { type: "category", gridIndex: 0, data: labels, axisLabel: { show: false }, axisTick: { show: false }, axisLine },
          { type: "category", gridIndex: 1, data: labels, axisLabel, axisLine },
        ]
      : [{ type: "category", gridIndex: 0, data: labels, axisLabel, axisLine }],
    yAxis: hasCost
      ? [
          { type: "value", gridIndex: 0, axisLabel: { ...axisLabel, formatter: (value: number) => formatCompact(value) }, splitLine },
          { type: "value", gridIndex: 1, splitNumber: 2, axisLabel: { ...axisLabel, formatter: (value: number) => `$${value}` }, splitLine },
        ]
      : [{ type: "value", gridIndex: 0, axisLabel: { ...axisLabel, formatter: (value: number) => formatCompact(value) }, splitLine }],
    dataZoom: [
      { type: "inside", xAxisIndex },
      {
        type: "slider",
        xAxisIndex,
        height: 20,
        bottom: 12,
        borderColor: colors.zoomBorder,
        fillerColor: colors.zoomFill,
        textStyle: { color: palette.axis },
      },
    ],
    axisPointer: { link: [{ xAxisIndex: "all" }] },
    tooltip: {
      trigger: "axis",
      axisPointer: { type: "shadow" },
      backgroundColor: colors.tooltipBg,
      borderColor: colors.tooltipBorder,
      textStyle: { color: colors.text },
      formatter,
    },
    legend: { show: false, selected: Object.fromEntries(data.series.map((name) => [name, !hidden.includes(name)])) },
    series: [...tokenSeries, ...costSeries],
  };
  return { option, hasCost };
}
