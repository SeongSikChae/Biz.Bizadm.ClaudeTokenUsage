import { Moon, Radio, RefreshCw, Sun } from "lucide-react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { Button } from "@/components/ui/button";
import { setTheme, useTheme } from "@/lib/theme";
import { formatKstTime } from "../format";
import { refresh, setLive } from "../usageSlice";

/** 제목, 마지막 성공 조회 시각, 실시간·새로고침·다크 모드 조작. */
export function UsageHeader() {
  const dispatch = useAppDispatch();
  const live = useAppSelector((state) => state.usage.live);
  const loading = useAppSelector((state) => state.usage.status === "loading");
  const lastSuccessAt = useAppSelector((state) => state.usage.lastSuccessAt);
  const theme = useTheme();

  return (
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0 space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Claude Code 토큰 사용량</h1>
        <p className="flex flex-wrap items-center gap-x-2 text-sm text-muted-foreground">
          <span>마지막 조회 {lastSuccessAt ? `${formatKstTime(lastSuccessAt)} KST` : "—"}</span>
          {loading && <span role="status">조회 중…</span>}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant={live ? "default" : "outline"}
          aria-pressed={live}
          title="켜 두면 10초마다 기간을 지금 기준으로 옮겨 다시 조회합니다"
          onClick={() => dispatch(setLive(!live))}
        >
          <Radio className={live ? "animate-pulse" : undefined} />
          실시간
        </Button>
        <Button variant="outline" onClick={() => dispatch(refresh())}>
          <RefreshCw className={loading ? "animate-spin" : undefined} />
          새로고침
        </Button>
        <Button
          variant="outline"
          size="icon"
          aria-label={theme === "dark" ? "라이트 모드로 바꾸기" : "다크 모드로 바꾸기"}
          onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
        >
          {theme === "dark" ? <Sun /> : <Moon />}
        </Button>
      </div>
    </header>
  );
}
