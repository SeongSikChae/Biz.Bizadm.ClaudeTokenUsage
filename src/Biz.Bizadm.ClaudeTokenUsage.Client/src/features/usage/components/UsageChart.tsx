import { Bar, CartesianGrid, ComposedChart, Line, XAxis, YAxis } from "recharts";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";
import { formatBucketLabel } from "../format";
import type { UsageSeries } from "../types";

const COLOR_COUNT = 5;

interface UsageChartProps {
  data: UsageSeries;
}

/**
 * 토큰 사용량을 계열별로 쌓은 막대와 비용(USD) 보조 축 선으로 그린다. 비용이 null이면 선을 그리지 않는다.
 * 계열 이름(이메일·모델명)은 CSS 변수 이름으로 쓸 수 없으므로 s0, s1… 키로 바꿔 쓴다.
 */
export function UsageChart({ data }: UsageChartProps) {
  const keys = data.series.map((_, index) => `s${index}`);
  const config: ChartConfig = Object.fromEntries(
    data.series.map((name, index) => [keys[index], { label: name, color: `var(--chart-${(index % COLOR_COUNT) + 1})` }]),
  );
  config.costUsd = { label: "비용 (USD)", color: "var(--foreground)" };
  const hasCost = data.buckets.some((bucket) => bucket.costUsd !== null);

  const rows = data.buckets.map((bucket) => {
    const row: Record<string, string | number | null> = {
      label: formatBucketLabel(data.unit, bucket.start),
      costUsd: bucket.costUsd,
    };
    data.series.forEach((name, index) => {
      row[keys[index]] = bucket.tokens[name] ?? 0;
    });
    return row;
  });

  return (
    <div className="space-y-3">
      <ChartContainer config={config} className="aspect-auto h-80 w-full">
        <ComposedChart data={rows} margin={{ left: 8, right: 8 }}>
          <CartesianGrid vertical={false} />
          <XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={24} />
          <YAxis yAxisId="tokens" tickLine={false} axisLine={false} width={64} />
          {hasCost && <YAxis yAxisId="cost" orientation="right" tickLine={false} axisLine={false} width={64} />}
          <ChartTooltip content={<ChartTooltipContent />} />
          {keys.map((key) => (
            <Bar key={key} dataKey={key} yAxisId="tokens" stackId="tokens" fill={`var(--color-${key})`} isAnimationActive={false} />
          ))}
          {hasCost && (
            // 등장 애니메이션(stroke-dasharray)이 백그라운드 탭 등에서 중간에 멈춰 선이 일부만 보일 수 있어 끈다.
            <Line
              dataKey="costUsd"
              yAxisId="cost"
              type="monotone"
              stroke="var(--color-costUsd)"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
          )}
        </ComposedChart>
      </ChartContainer>
      <ul aria-label="계열" className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
        {data.series.map((name, index) => (
          <li key={name} className="flex items-center gap-1.5">
            <span className="inline-block size-3 rounded-sm" style={{ background: `var(--chart-${(index % COLOR_COUNT) + 1})` }} />
            {name}
          </li>
        ))}
        {hasCost && (
          <li className="flex items-center gap-1.5">
            <span className="inline-block h-0.5 w-3 bg-foreground" />
            비용 (USD)
          </li>
        )}
      </ul>
    </div>
  );
}
