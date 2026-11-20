import { useEffect } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { UsageChart } from "@/features/usage/components/UsageChart";
import { UsageFilters } from "@/features/usage/components/UsageFilters";
import { UsageSummary } from "@/features/usage/components/UsageSummary";
import { loadMeta, loadSeries } from "@/features/usage/usageSlice";

function App() {
  const dispatch = useAppDispatch();
  const { filters, series, status, error } = useAppSelector((state) => state.usage);

  // 처음 열 때와 단위를 바꿀 때 보관 하한·필터 목록을 새로 받는다(화면을 오래 열어 두어도 최신으로 유지).
  useEffect(() => {
    void dispatch(loadMeta());
  }, [dispatch, filters.unit]);

  useEffect(() => {
    void dispatch(loadSeries(filters));
  }, [dispatch, filters]);

  return (
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">Claude Code 토큰 사용량</h1>
      <Card>
        <CardContent className="pt-6">
          <UsageFilters />
        </CardContent>
      </Card>
      {error && (
        <p role="alert" className="text-sm text-destructive">
          조회에 실패했습니다: {error}
        </p>
      )}
      {series && (
        <>
          <UsageSummary data={series} tokenTypeFiltered={filters.tokenTypes.length > 0} />
          <Card>
            <CardHeader>
              <CardTitle>구간별 사용량{status === "loading" ? " (불러오는 중)" : ""}</CardTitle>
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
