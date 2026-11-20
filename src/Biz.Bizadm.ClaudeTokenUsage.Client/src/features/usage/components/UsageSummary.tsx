import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { formatPercent, formatTokens, formatUsd } from "../format";
import { PALETTE_SIZE } from "../seriesColors";
import type { UsageSeries } from "../types";

interface UsageSummaryProps {
  data: UsageSeries;
  tokenTypeFiltered: boolean;
  /** 계열 이름 → 팔레트 번호(차트·범례와 같은 색) */
  colorIndex: Record<string, number>;
}

/**
 * 고른 기간의 토큰·비용 총합과 계열별 토큰 합계·비율. 범례 숨김과 상관없이 기간 전체 값이다.
 * 토큰 종류로 거르면 비용 대신 안내를 보인다.
 */
export function UsageSummary({ data, tokenTypeFiltered, colorIndex }: UsageSummaryProps) {
  const total = data.totals.tokens;
  const perSeries = data.series.map((name) => ({
    name,
    tokens: data.buckets.reduce((sum, bucket) => sum + (bucket.tokens[name] ?? 0), 0),
  }));

  return (
    <>
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2">
        <Card>
          <CardContent>
            <dl>
              <dt className="text-sm text-muted-foreground">토큰 총합</dt>
              <dd className="text-3xl font-semibold tracking-tight tabular-nums">{formatTokens(total)}</dd>
            </dl>
          </CardContent>
        </Card>
        <Card>
          <CardContent>
            <dl>
              <dt className="text-sm text-muted-foreground">비용 총합 (USD)</dt>
              <dd className="text-3xl font-semibold tracking-tight tabular-nums">
                {data.totals.costUsd === null ? "—" : formatUsd(data.totals.costUsd)}
              </dd>
            </dl>
            {tokenTypeFiltered && (
              <p role="note" className="mt-1 text-sm text-muted-foreground">
                토큰 종류로 거르면 비용을 볼 수 없습니다. 비용 지표에는 토큰 종류 구분이 없습니다.
              </p>
            )}
          </CardContent>
        </Card>
      </div>
      {perSeries.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>계열별 토큰</CardTitle>
          </CardHeader>
          <CardContent>
            <ul aria-label="계열별 합계" className="grid grid-cols-1 gap-x-8 gap-y-3 sm:grid-cols-2">
              {perSeries.map(({ name, tokens }) => {
                const percent = formatPercent(tokens, total);
                const color = `var(--series-${((colorIndex[name] ?? 0) % PALETTE_SIZE) + 1})`;
                return (
                  <li key={name} className="min-w-0 space-y-1">
                    <div className="flex items-center justify-between gap-3 text-sm">
                      <span className="flex min-w-0 items-center gap-1.5">
                        <span className="inline-block size-3 shrink-0 rounded-sm" style={{ background: color }} />
                        <span className="break-all">{name}</span>
                      </span>
                      <span className="shrink-0 tabular-nums">
                        {formatTokens(tokens)} <span className="text-muted-foreground">{percent}</span>
                      </span>
                    </div>
                    <div className="h-1.5 overflow-hidden rounded-full bg-muted">
                      <div
                        className="h-full rounded-full"
                        style={{ width: total === 0 ? "0%" : `${Math.min(100, (tokens / total) * 100)}%`, background: color }}
                      />
                    </div>
                  </li>
                );
              })}
            </ul>
          </CardContent>
        </Card>
      )}
    </>
  );
}
