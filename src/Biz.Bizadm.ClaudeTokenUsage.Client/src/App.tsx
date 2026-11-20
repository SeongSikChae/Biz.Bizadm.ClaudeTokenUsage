import { useEffect, useRef } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { UsageChart } from "@/features/usage/components/UsageChart";
import { UsageFilters } from "@/features/usage/components/UsageFilters";
import { UsageHeader } from "@/features/usage/components/UsageHeader";
import { UsageSummary } from "@/features/usage/components/UsageSummary";
import { loadMeta, loadSeries, refresh } from "@/features/usage/usageSlice";

/** 실시간 자동 갱신 주기 */
const LIVE_INTERVAL_MS = 10_000;

function App() {
  const dispatch = useAppDispatch();
  const { filters, series, status, error, live, refreshSeq, colorIndex } = useAppSelector((state) => state.usage);
  const statusRef = useRef(status);

  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  // 처음 열 때와 단위를 바꿀 때 보관 하한·필터 목록을 새로 받는다(화면을 오래 열어 두어도 최신으로 유지).
  useEffect(() => {
    void dispatch(loadMeta());
  }, [dispatch, filters.unit]);

  // 필터가 바뀌거나 새로고침·자동 갱신(refreshSeq)이 있으면 한 번 조회한다.
  useEffect(() => {
    void dispatch(loadSeries(filters));
  }, [dispatch, filters, refreshSeq]);

  // 조회 중이면 그 주기를 건너뛴다. 요청이 겹치면 마지막 요청 결과만 반영하므로 앞선 요청의 시간 초과가 가려지기 때문이다.
  useEffect(() => {
    if (!live) return;
    const timer = window.setInterval(() => {
      if (statusRef.current !== "loading") dispatch(refresh());
    }, LIVE_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [dispatch, live]);

  return (
    <main className="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-6 sm:px-6 sm:py-8">
      <UsageHeader />
      <Card>
        <CardContent>
          <UsageFilters />
        </CardContent>
      </Card>
      {error && (
        <p role="alert" className="rounded-xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          조회에 실패했습니다: {error}
        </p>
      )}
      {series && (
        <>
          <UsageSummary data={series} tokenTypeFiltered={filters.tokenTypes.length > 0} colorIndex={colorIndex} />
          <Card>
            <CardHeader>
              <CardTitle>구간별 사용량</CardTitle>
            </CardHeader>
            <CardContent>
              {series.buckets.length === 0 ? (
                <p className="text-sm text-muted-foreground">고른 기간에 보관 중인 구간이 없습니다.</p>
              ) : (
                <UsageChart data={series} />
              )}
            </CardContent>
          </Card>
        </>
      )}
    </main>
  );
}

export default App;
