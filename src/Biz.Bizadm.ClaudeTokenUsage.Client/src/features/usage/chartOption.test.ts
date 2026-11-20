/// <reference types="node" />
import { readFileSync } from "node:fs";
import { buildChartOption, COST_SERIES_NAME, type ChartPalette } from "./chartOption";
import { sampleSeries } from "./testData";
import type { UsageSeries } from "./types";

const palette: ChartPalette = {
  series: ["#000001", "#000002", "#000003", "#000004", "#000005", "#000006", "#000007", "#000008"],
  cost: "#c0c0c0",
  axis: "#a0a0a0",
  grid: "#b0b0b0",
};

const noCost: UsageSeries = { ...sampleSeries, buckets: sampleSeries.buckets.map((bucket) => ({ ...bucket, costUsd: null })) };

type Loose = Record<string, unknown> & { [key: string]: never | unknown };
// 옵션은 ECharts 유니언 타입이라 테스트에서는 느슨한 객체로 본다.
function build(data: UsageSeries, hidden: string[] = [], theme: "light" | "dark" = "light") {
  const { option, hasCost } = buildChartOption({ data, colorIndex: { input: 0, output: 5 }, hidden, palette, theme });
  return { option: option as unknown as Record<string, Loose[] & Loose>, hasCost };
}

function tooltipText(data: UsageSeries, hidden: string[], dataIndex: number): string {
  const { option } = build(data, hidden);
  const tooltip = option.tooltip as unknown as { formatter: (params: unknown) => string };
  return tooltip.formatter([{ dataIndex }]).replace(/<[^>]+>/g, " ");
}

describe("buildChartOption", () => {
  it("uses two linked grids with a cost bar when cost exists", () => {
    const { option, hasCost } = build(sampleSeries);
    expect(hasCost).toBe(true);
    expect(option.grid).toHaveLength(2);
    expect(option.xAxis).toHaveLength(2);
    expect(option.yAxis).toHaveLength(2);
    const series = option.series as unknown as { name: string; xAxisIndex: number; stack?: string; itemStyle: { color: string } }[];
    const cost = series.filter((item) => item.name === COST_SERIES_NAME);
    expect(cost).toHaveLength(1);
    expect(cost[0]).toMatchObject({ xAxisIndex: 1, itemStyle: { color: "#c0c0c0" } });
    for (const zoom of option.dataZoom as unknown as { xAxisIndex: number[] }[]) {
      expect(zoom.xAxisIndex).toEqual([0, 1]);
    }
    expect(option.axisPointer).toEqual({ link: [{ xAxisIndex: "all" }] });
  });

  it("uses one grid and no cost bar when cost is null", () => {
    const { option, hasCost } = build(noCost);
    expect(hasCost).toBe(false);
    expect(option.grid).toHaveLength(1);
    const series = option.series as unknown as { name: string }[];
    expect(series.map((item) => item.name)).toEqual(["input", "output"]);
    for (const zoom of option.dataZoom as unknown as { xAxisIndex: number[] }[]) {
      expect(zoom.xAxisIndex).toEqual([0]);
    }
  });

  it("stacks token bars with the assigned palette colors", () => {
    const series = build(sampleSeries).option.series as unknown as { name: string; stack?: string; itemStyle: { color: string } }[];
    expect(series[0]).toMatchObject({ name: "input", stack: "tokens", itemStyle: { color: "#000001" } });
    expect(series[1]).toMatchObject({ name: "output", stack: "tokens", itemStyle: { color: "#000006" } });
  });

  it("hides series through legend selection", () => {
    const legend = build(sampleSeries, ["output"]).option.legend as unknown as { show: boolean; selected: Record<string, boolean> };
    expect(legend).toEqual({ show: false, selected: { input: true, output: false } });
  });

  it("formats token axis compactly and cost axis with a dollar sign", () => {
    const yAxis = build(sampleSeries).option.yAxis as unknown as { axisLabel: { formatter: (value: number) => string } }[];
    expect(yAxis[0].axisLabel.formatter(160000)).toBe("160K");
    expect(yAxis[1].axisLabel.formatter(0.5)).toBe("$0.5");
  });

  it("shows visible series, their sum and cost in the tooltip", () => {
    const text = tooltipText(sampleSeries, [], 0);
    expect(text).toContain("11-20 10:00");
    expect(text).toMatch(/input\s+100/);
    expect(text).toMatch(/output\s+30/);
    expect(text).toMatch(/합계\s+130/);
    expect(text).toMatch(/비용\s+\$0\.2500/);
  });

  it("excludes hidden series from the tooltip sum and omits null cost", () => {
    const text = tooltipText(noCost, ["output"], 0);
    expect(text).not.toContain("output");
    expect(text).toMatch(/합계\s+100/);
    expect(text).not.toContain("비용");
  });

  it("does not set zoom start or end so the zoom ratio survives updates", () => {
    for (const zoom of build(sampleSeries).option.dataZoom as unknown as Record<string, unknown>[]) {
      expect(zoom).not.toHaveProperty("start");
      expect(zoom).not.toHaveProperty("end");
    }
  });

  it("uses theme colors for the tooltip", () => {
    const light = build(sampleSeries, [], "light").option.tooltip as unknown as { backgroundColor: string };
    const dark = build(sampleSeries, [], "dark").option.tooltip as unknown as { backgroundColor: string };
    expect(light.backgroundColor).not.toBe(dark.backgroundColor);
  });

  it("defines every chart color variable as hex in light and dark", () => {
    // Vitest는 CSS를 처리하지 않아(?raw도 빈 문자열) 파일을 직접 읽는다.
    // jsdom 환경에서는 import.meta.url이 file: 주소가 아니어서 Vitest가 넣어 주는 __dirname을 쓴다.
    const css = readFileSync(`${__dirname}/../../index.css`, "utf8");
    const names = [...Array.from({ length: 8 }, (_, index) => `--series-${index + 1}`), "--chart-cost", "--chart-axis", "--chart-grid"];
    for (const name of names) {
      const values = [...css.matchAll(new RegExp(`${name}:\\s*([^;]+);`, "g"))].map((match) => match[1].trim());
      expect(values, name).toHaveLength(2);
      for (const value of values) expect(value, name).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });
});
