import { formatUsd } from "../format";
import type { UsageSeries } from "../types";

interface UsageSummaryProps {
  data: UsageSeries;
  tokenTypeFiltered: boolean;
}

/** 고른 기간의 토큰·비용 총합. 토큰 종류로 거르면 비용 대신 안내를 보인다. */
export function UsageSummary({ data, tokenTypeFiltered }: UsageSummaryProps) {
  return (
    <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div className="rounded-lg border p-4">
        <dt className="text-sm text-muted-foreground">토큰 총합</dt>
        <dd className="text-2xl font-semibold tabular-nums">{data.totals.tokens.toLocaleString("ko-KR")}</dd>
      </div>
      <div className="rounded-lg border p-4">
        <dt className="text-sm text-muted-foreground">비용 총합 (USD)</dt>
        <dd className="text-2xl font-semibold tabular-nums">
          {data.totals.costUsd === null ? "—" : formatUsd(data.totals.costUsd)}
        </dd>
        {tokenTypeFiltered && (
          <p role="note" className="mt-1 text-sm text-muted-foreground">
            토큰 종류로 거르면 비용을 볼 수 없습니다. 비용 지표에는 토큰 종류 구분이 없습니다.
          </p>
        )}
      </div>
    </dl>
  );
}
