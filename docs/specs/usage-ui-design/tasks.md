# 사용량 화면 디자인 개선 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로의 `Client/`는 `src/Biz.Bizadm.ClaudeTokenUsage.Client/`를 줄인 것이다. 명령은 `Client/` 폴더에서 실행한다.

## 단계 1: 기반

- [x] T001 `echarts` `6.1.0`을 의존성에 추가(`npm install echarts@6.1.0 --save-exact`). `recharts`는 T018에서 뺀다 (Client/package.json, Client/package-lock.json) — C2, C3
- [x] T002 [P] 회색 `--chart-1`~`--chart-5`를 지우고 hex 차트 변수 `--series-1`~`--series-8`, `--chart-cost`, `--chart-axis`, `--chart-grid`를 `:root`·`.dark`에 정의. 라이트 `--background`를 카드와 다른 옅은 회색으로 (Client/src/index.css) — C1, C8, C12
- [x] T003 [P] `echarts/core`에 `BarChart`, `GridComponent`, `TooltipComponent`, `AxisPointerComponent`, `DataZoomComponent`, `LegendComponent`, `CanvasRenderer`를 등록하고 `init`과 옵션 타입을 내보냄 (Client/src/lib/echarts.ts) — C2, C3
- [x] T004 [P] `@/lib/echarts` 가짜 모듈(`init`이 `setOption`·`resize`·`dispose`·`dispatchAction` 가짜 함수를 가진 객체를 돌려주고, 테스트가 마지막 인스턴스를 꺼낼 수 있게 함)과 `matchMedia` 빈 구현 추가 (Client/src/test/setup.ts) — C10

체크포인트: `npm test`가 기존 테스트 그대로 통과(아직 `UsageChart`는 Recharts 사용).

## 단계 2: 순수 로직과 상태

- [x] T005 [P] `formatCompact`(K·M·B), `formatTokens`(ko-KR 천 단위), `formatPercent`(총합 0이면 "—"), `formatKstTime` 추가와 테스트 (Client/src/features/usage/format.ts, Client/src/features/usage/components/UsageChart.test.tsx) — C5, C6, C7
- [x] T006 [P] `shiftPeriodToNow(from, to, nowIso, earliest)`: 끝은 지금이 속한 분의 다음 분 경계, 시작은 끝 − 옮기기 전 기간 길이, 보관 하한으로 자름. 테스트(길이 유지, 분 경계, 하한) (Client/src/features/usage/periods.ts, Client/src/features/usage/periods.test.ts) — C11
- [x] T007 [P] `assignColors(prev, series, reset)`: reset이면 응답 순서대로 0..7 순환, 아니면 ① 유지되는 이름의 번호를 먼저 넣고 ② 새 이름에 빈 번호 중 가장 작은 것(모두 쓰면 0부터 순환). 테스트(reset, 유지, 새 계열 빈 색, 빠진 계열 색 반환, 8개 초과 순환, `prev={input:0, output:1}`·`series=[cacheCreation, input, output]` → `cacheCreation=2`) (Client/src/features/usage/seriesColors.ts, Client/src/features/usage/seriesColors.test.ts) — C1
- [x] T008 [P] `getJson`의 `fetch`에 `signal: AbortSignal.timeout(15000)`, `TimeoutError`를 "응답 시간이 초과되었습니다" 오류로 바꿈 (Client/src/features/usage/api.ts) — C7
- [x] T009 [P] 테마 저장소와 `useTheme()`(`useSyncExternalStore`): `setTheme`은 `localStorage`(`theme`) 저장 → `<html>.dark` 동기 변경 → 알림. `matchMedia` 변경은 저장값이 없을 때만 클래스 변경 → 알림, 저장하지 않음. `localStorage` 접근은 try/catch (Client/src/lib/theme.ts) — C8
- [x] T010 `buildChartOption({ data, colorIndex, hidden, palette, theme })`: `theme`(light/dark)에 따라 툴팁 배경·테두리·글자색과 `dataZoom` 슬라이더 색을 hex로 넣음. 비용 있음이면 그리드·x축·y축 2개, `dataZoom`(내부+슬라이더) `xAxisIndex: [0, 1]`, `axisPointer.link`, 비용 막대(`--chart-cost`), 모두 null이면 1개씩·비용 없음. 토큰 막대 `stack`·`color = palette.series[colorIndex[name]]`, `legend: { show: false, selected }`, 축 formatter(`formatCompact`, `$`), 툴팁 formatter(보이는 계열·합계·비용, HTML 이스케이프), 갱신 옵션에 `dataZoom` `start`/`end` 없음. 비용 유무를 돌려주는 값 포함. 테스트(그리드 수, 비용 시리즈, `xAxisIndex`, `axisPointer.link`, 색, `legend.selected`, 축 160000 → "160K"·`$`, 툴팁이 숨긴 계열을 합계에서 뺌·`$0.2500`, `dataZoom` `start`/`end` 없음, `index.css`의 `--series-*`·`--chart-*` 값이 모두 hex, light/dark에 따라 툴팁 배경이 다름) (Client/src/features/usage/chartOption.ts, Client/src/features/usage/chartOption.test.ts) — C1, C2, C3, C4, C5, C8, C11
- [x] T011 슬라이스: 상태 `live`(true)·`refreshSeq`·`lastSuccessAt`·`colorIndex`·`colorKey`·`hidden`, 액션 `setLive`(켜면 기간 이동·seq+1)·`refresh`(live면 `shiftPeriodToNow`·seq+1)·`toggleHidden`, `setPeriod`가 `live`를 끔, `pending`은 `error`를 지우지 않음, `fulfilled`에서 `error` 지움·`lastSuccessAt`·`assignColors`(요청의 groupBy·필터 키가 `colorKey`와 다르면 reset). 테스트(위 동작 전부, 실패 때 `lastSuccessAt` 유지, 다음 `pending`에서 `error` 유지, 기준/필터 변경 시 재배정·기간 변경·refresh 때 유지, `TimeoutError`로 거부된 조회 → `error`가 "응답 시간이 초과되었습니다") (Client/src/features/usage/usageSlice.ts, Client/src/features/usage/usageSlice.test.ts) — C1, C4, C7, C10, C11

체크포인트: `npm test`로 `periods`·`seriesColors`·`chartOption`·`usageSlice`·`UsageChart`(format 부분) 테스트 통과.

## 단계 3: 화면

- [x] T012 `UsageChart`를 ECharts로 다시 작성: 마운트 때 `init`, `ResizeObserver`로 `resize`, 언마운트 때 `dispose`. 데이터·숨김·테마가 바뀌면 hex 변수를 `getComputedStyle`로 읽고 테마 저장소 값과 함께 `buildChartOption`에 넘김 → 비용 유무가 이전과 같으면 `setOption(option, { replaceMerge: ['series'] })`, 다르면 `{ notMerge: true }`. DOM 범례(버튼, 색 견본, `aria-pressed`, 누르면 `toggleHidden`). 테스트 수정(범례 목록·비용 범례, 범례 누르면 `aria-pressed`와 `legend.selected` 바뀜, 비용 있음 → 없음 때 `notMerge`, 같은 모양이면 `replaceMerge`) (Client/src/features/usage/components/UsageChart.tsx, Client/src/features/usage/components/UsageChart.test.tsx) — C1, C2, C3, C4, C5, C11
- [x] T013 [P] 요약: 토큰·비용 총합 2칸 + 계열별 합계·비율(색 견본, 숨김과 무관, 총합 0이면 "—"), 토큰 종류 필터 안내 유지. 총합 칸과 계열별 목록은 `Card` 컴포넌트로 감쌈(지금의 `div rounded-lg border` 대신). 테스트 수정·추가(요약 칸이 `data-slot="card"`) (Client/src/features/usage/components/UsageSummary.tsx, Client/src/features/usage/components/UsageSummary.test.tsx) — C1, C6, C10, C12
- [x] T014 [P] 헤더: 제목, 마지막 성공 조회 시각(KST), 실시간 토글(`aria-pressed`), 새로고침 버튼(조회 중 아이콘 회전·조회 중 표시), 다크 모드 토글. 375px에서 줄바꿈 (Client/src/features/usage/components/UsageHeader.tsx) — C7, C8, C9, C11
- [x] T015 [P] 필터 입력 폭을 `w-full sm:w-auto`로 바꿔 375px에서 넘치지 않게 함(모양은 그대로) (Client/src/features/usage/components/UsageFilters.tsx) — C9, C10
- [x] T016 `App` 재구성: 헤더·필터 카드·오류·요약·차트 카드를 `gap-6` 한 곳에서 배치, 조회 effect를 `[filters, refreshSeq]`로, 실시간이면 10초 `setInterval`로 `refresh`(조회 중이면 그 주기 건너뜀), 새로고침은 조회 중에도 `refresh`. 테스트 수정·추가(화면 렌더, 새로고침 누르면 조회 한 번 더, 가짜 타이머로 live면 10초마다 조회·끄면 멈춤, 조회가 끝나지 않으면 10초 주기에 더 나가지 않고 시간 초과 거부 뒤 오류가 보임, 테마 토글 → `<html>.dark`·`localStorage`, 값이 없으면 `matchMedia`, 운영체제 변경 뒤 `localStorage` 비어 있음·저장값 있으면 무시, 토글 직후 마지막 `setOption` 팔레트가 새 테마 변수 값) (Client/src/App.tsx, Client/src/App.test.tsx) — C7, C8, C9, C10, C11, C12
- [x] T017 [P] 첫 그리기 전에 저장된 테마(없으면 운영체제 설정)로 `<html>`에 `dark` 클래스를 붙이는 인라인 스크립트 (Client/index.html) — C8
- [x] T018 `recharts` 의존성 제거(`npm uninstall recharts`)와 Recharts 래퍼 삭제, `setup.ts`의 Recharts 관련 주석 정리 (Client/package.json, Client/package-lock.json, Client/src/components/ui/chart.tsx, Client/src/test/setup.ts) — C10

체크포인트: `npm test`, `npm run lint`, `npm run build` 통과. `grep -r recharts Client/src`가 비어 있음.

## 단계 4: 기준 spec 반영

- [x] T019 기준 spec 반영: R8을 이번 화면 요구(ECharts 누적 막대·비용 차트 분리·연동 확대·범례 숨김·숫자 축약·요약 계열별 비율·헤더 조회 시각·새로고침·다크 모드·375px·배경 구분)로 바꾸고, R11(실시간 토글과 10초 자동 갱신, 기간 이동 규칙, 확대·숨김 유지, 오류 표시)을 더하고, 이력에 `usage-ui-design` 한 줄(R8 바꿈, R11 추가) 추가. `파일:` 줄은 Client 폴더 전체를 이미 담고 있어 그대로 (docs/features/token-usage.md) — C1~C12

체크포인트: 기준 spec의 R8·R11이 spec.md C1~C12와 어긋나지 않음(읽어 대조).

## 의존 관계

- 단계 1 → 단계 2 → 단계 3 → 단계 4 순서로 한다.
- T004(가짜 echarts 모듈)는 T003의 모듈 경로를 가짜로 바꾸므로 T003 뒤 또는 같이 한다.
- T010은 T005(format)와 T002(변수 이름)에 의존한다.
- T011은 T006(`shiftPeriodToNow`), T007(`assignColors`), T008(시간 초과 오류 문구)에 의존한다.
- T012는 T003·T004·T009·T010·T011에 의존한다.
- T013은 T005·T011(`colorIndex`)에, T014는 T005·T009·T011에 의존한다.
- T016은 T012·T013·T014·T015에 의존한다.
- T018은 T012(새 `UsageChart`가 Recharts를 쓰지 않음) 뒤에 한다. 그 전에 빼면 빌드가 깨진다.
- T019는 구현이 끝난 뒤 마지막 구현 단계로 한다(구현 후 검토 diff에 들어가게).
