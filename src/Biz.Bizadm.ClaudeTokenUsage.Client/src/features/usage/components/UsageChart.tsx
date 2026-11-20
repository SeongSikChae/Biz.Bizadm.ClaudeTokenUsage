import { useEffect, useRef } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { init, type ChartInstance } from "@/lib/echarts";
import { useTheme } from "@/lib/theme";
import { buildChartOption, COST_SERIES_NAME, type ChartPalette } from "../chartOption";
import { PALETTE_SIZE } from "../seriesColors";
import type { UsageSeries } from "../types";
import { toggleHidden } from "../usageSlice";

interface UsageChartProps {
  data: UsageSeries;
}

/** ECharts에 넘길 hex 색을 지금 테마의 CSS 변수에서 읽는다. */
function readPalette(): ChartPalette {
  const style = getComputedStyle(document.documentElement);
  const value = (name: string) => style.getPropertyValue(name).trim();
  return {
    series: Array.from({ length: PALETTE_SIZE }, (_, index) => value(`--series-${index + 1}`)),
    cost: value("--chart-cost"),
    axis: value("--chart-axis"),
    grid: value("--chart-grid"),
  };
}

/**
 * 토큰 누적 막대와 비용 막대를 ECharts로 그리고, 계열을 숨기고 보이는 범례를 DOM 버튼으로 둔다.
 * 옵션 갱신은 비용 그리드 유무가 같으면 계열만 바꿔(확대 범위 유지) 병합하고, 다르면 새로 그린다.
 * 기본 병합은 배열 컴포넌트를 인덱스별로 합쳐 그리드·축이 줄어도 뒤의 것이 남기 때문이다.
 */
export function UsageChart({ data }: UsageChartProps) {
  const dispatch = useAppDispatch();
  const colorIndex = useAppSelector((state) => state.usage.colorIndex);
  const hidden = useAppSelector((state) => state.usage.hidden);
  const theme = useTheme();
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<ChartInstance | null>(null);
  const hasCostRef = useRef<boolean | null>(null);
  const hasCost = data.buckets.some((bucket) => bucket.costUsd !== null);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const chart = init(element);
    chartRef.current = chart;
    const observer = new ResizeObserver(() => chart.resize());
    observer.observe(element);
    return () => {
      observer.disconnect();
      chart.dispose();
      chartRef.current = null;
      hasCostRef.current = null;
    };
  }, []);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;
    const built = buildChartOption({ data, colorIndex, hidden, palette: readPalette(), theme });
    if (hasCostRef.current === built.hasCost) chart.setOption(built.option, { replaceMerge: ["series"] });
    else chart.setOption(built.option, { notMerge: true });
    hasCostRef.current = built.hasCost;
  }, [data, colorIndex, hidden, theme]);

  return (
    <div className="space-y-3">
      <div ref={containerRef} className="h-[420px] w-full" />
      <ul aria-label="계열" className="flex flex-wrap gap-2 text-sm">
        {data.series.map((name) => {
          const visible = !hidden.includes(name);
          return (
            <li key={name}>
              <button
                type="button"
                aria-pressed={visible}
                onClick={() => dispatch(toggleHidden(name))}
                className="flex max-w-full items-center gap-1.5 rounded-md px-2 py-1 hover:bg-muted aria-[pressed=false]:opacity-50"
              >
                <span
                  className="inline-block size-3 shrink-0 rounded-sm"
                  style={{ background: `var(--series-${((colorIndex[name] ?? 0) % PALETTE_SIZE) + 1})` }}
                />
                <span className={visible ? "break-all" : "break-all line-through"}>{name}</span>
              </button>
            </li>
          );
        })}
        {hasCost && (
          <li className="flex items-center gap-1.5 px-2 py-1 text-muted-foreground">
            <span className="inline-block size-3 rounded-sm" style={{ background: "var(--chart-cost)" }} />
            {COST_SERIES_NAME}
          </li>
        )}
      </ul>
    </div>
  );
}
