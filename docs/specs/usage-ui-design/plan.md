# 사용량 화면 디자인 개선 계획

## 요약

클라이언트(`src/Biz.Bizadm.ClaudeTokenUsage.Client`)만 바꾼다. 서버와 조회 API는 그대로 둔다.

- 차트: Recharts와 shadcn chart 래퍼를 빼고 ECharts 6을 쓴다. 한 ECharts 인스턴스에 그리드 두 개(위: 토큰 누적 막대, 아래: 비용 막대)를 두어, 확대(dataZoom)와 마우스 위치 표시(axisPointer)가 두 그리드에 함께 걸리게 한다. 옵션은 순수 함수(`buildChartOption`)로 만들어 jsdom에서도 단위 테스트한다.
- 범례: ECharts 범례는 숨기고(`legend.show: false`, 선택 상태만 씀) DOM 버튼 범례를 둔다. 숨김 상태는 Redux에 계열 이름으로 둔다.
- 색: 8색 팔레트를 CSS 변수로 라이트·다크 각각 정의하고, 계열 이름 → 색 번호 배정은 순수 함수(`assignColors`)로 Redux에서 응답마다 갱신한다.
- 실시간: Redux에 `live`·`refreshSeq`·`lastSuccessAt`을 두고, 10초 타이머와 새로고침 버튼은 `refresh` 액션 하나로 처리한다. 조회는 `filters`·`refreshSeq`가 바뀔 때 한 번만 보낸다.
- 화면: 헤더(제목, 마지막 조회 시각, 실시간·새로고침·다크 모드), 필터 카드, 요약(총합 2칸 + 계열별 목록), 차트 카드 순서. 페이지 배경을 카드와 다른 옅은 색으로 바꾸고 간격을 통일한다.

## 바꿀 파일·구조

경로는 저장소 기준이며, `Client/`는 `src/Biz.Bizadm.ClaudeTokenUsage.Client/`를 줄인 것이다.

- `Client/package.json`, `Client/package-lock.json`: `echarts` `6.1.0` 추가, `recharts` 제거
- `Client/index.html`: 첫 그리기 전에 저장된 테마(없으면 운영체제 설정)로 `<html>`에 `dark` 클래스를 붙이는 짧은 인라인 스크립트(깜빡임 방지)
- `Client/src/index.css`: `--chart-1`~`--chart-5`(회색) 대신 ECharts에 넘기는 색을 hex로 정의(D016): `--series-1`~`--series-8`(색상 팔레트), `--chart-cost`(비용 막대), `--chart-axis`(축 글자), `--chart-grid`(격자선), 라이트·다크 각각. 라이트 `--background`를 카드(흰색)와 다른 옅은 회색으로. 다크는 이미 배경(0.145)과 카드(0.205)가 다름
- `Client/src/components/ui/chart.tsx`: 삭제(Recharts 래퍼, 다른 곳에서 쓰지 않음)
- `Client/src/lib/echarts.ts`(새 파일): `echarts/core`에 필요한 모듈만 등록(`BarChart`, `GridComponent`, `TooltipComponent`, `AxisPointerComponent`, `DataZoomComponent`, `LegendComponent`, `CanvasRenderer`)하고 `init`을 내보냄. 테스트에서 이 모듈만 가짜로 바꾼다
- `Client/src/lib/theme.ts`(새 파일): React 밖의 작은 테마 저장소와 `useTheme()`(`useSyncExternalStore`) — `localStorage`(`theme` 키, `light`/`dark`) 읽기·쓰기(try/catch), 값이 없으면 `matchMedia('(prefers-color-scheme: dark)')`를 따르고 그 변경에도 반응. `setTheme`(토글)은 `localStorage`에 저장하고 `<html>`의 `dark` 클래스를 동기로 바꾼 뒤 구독자에게 알린다(D017). `matchMedia` 변경은 저장된 값이 없을 때만 클래스를 동기로 바꾼 뒤 알리며, 저장하지 않는다(C8: 고른 적이 없으면 운영체제 설정을 따름). 헤더와 차트는 같은 저장소를 본다
- `Client/src/features/usage/format.ts`: `formatCompact`(K·M·B, `Intl.NumberFormat('en', { notation: 'compact' })`), `formatTokens`(ko-KR 천 단위), `formatPercent`(총합 0이면 "—"), `formatKstTime`(마지막 조회 시각) 추가
- `Client/src/features/usage/periods.ts`: `shiftPeriodToNow(from, to, nowIso, earliest)` 추가 — 끝은 `defaultPeriod`와 같은 "지금이 속한 분의 다음 분 경계", 시작은 끝 − (옮기기 전 끝 − 시작), 보관 하한보다 이르면 하한(`clampFrom`)
- `Client/src/features/usage/seriesColors.ts`(새 파일): `assignColors(prev, series, reset)` — 아래 "인터페이스·데이터" 규칙
- `Client/src/features/usage/chartOption.ts`(새 파일): `buildChartOption({ data, colorIndex, hidden, palette, theme })` → ECharts 옵션. 툴팁 formatter(보이는 계열 합계, 비용), 축 formatter, 비용이 모두 null이면 그리드·축·시리즈를 하나만
- `Client/src/features/usage/usageSlice.ts`: 상태 `live`, `refreshSeq`, `lastSuccessAt`, `colorIndex`, `colorKey`, `hidden` 추가. 액션 `setLive`, `refresh`, `toggleHidden` 추가. `setPeriod`는 `live`를 끈다. `loadSeries.fulfilled`에서 `lastSuccessAt`·`colorIndex` 갱신과 `error` 지우기. `loadSeries.pending`은 `error`를 지우지 않는다(지금은 지움, D018)
- `Client/src/features/usage/api.ts`: `getJson`의 `fetch`에 `signal: AbortSignal.timeout(15000)`. 시간 초과(`TimeoutError`)는 "응답 시간이 초과되었습니다" 오류로 바꿔 던진다(D018)
- `Client/src/features/usage/components/UsageChart.tsx`: ECharts 인스턴스 관리(마운트 때 `init`, `ResizeObserver`로 `resize`, 언마운트 때 `dispose`), 데이터·숨김·테마가 바뀌면 옵션 갱신(비용 차트 유무가 이전과 같으면 `setOption(option, { replaceMerge: ['series'] })`, 다르면 `setOption(option, { notMerge: true })`, D015), DOM 범례(버튼, `aria-pressed`)
- `Client/src/features/usage/components/UsageSummary.tsx`: 총합 2칸 + 계열별 합계·비율 목록(색 표시)
- `Client/src/features/usage/components/UsageHeader.tsx`(새 파일): 제목, 마지막 성공 조회 시각, 실시간 토글, 새로고침 버튼(조회 중 아이콘 회전), 다크 모드 토글
- `Client/src/features/usage/components/UsageFilters.tsx`: 모양은 그대로 두고 375px에서 넘치지 않게 입력 폭만 조정(`w-full sm:w-auto`)
- `Client/src/App.tsx`: 레이아웃 재구성, 조회 effect를 `[filters, refreshSeq]`에 걸고, 실시간이면 10초 `setInterval`로 `refresh` 디스패치. 타이머는 조회 중(`status === 'loading'`)이면 그 주기를 건너뛴다(D019). 새로고침 버튼은 사용자가 누른 것이므로 조회 중에도 보낸다
- `Client/src/test/setup.ts`: `@/lib/echarts` 가짜 모듈(`init`이 `setOption`·`resize`·`dispose`·`dispatchAction`을 가진 객체를 돌려줌), `matchMedia` 빈 구현
- 테스트: `Client/src/features/usage/seriesColors.test.ts`, `chartOption.test.ts`(새 파일), `periods.test.ts`, `usageSlice.test.ts`, `components/UsageChart.test.tsx`, `components/UsageSummary.test.tsx`, `Client/src/App.test.tsx` 수정·추가
- `docs/features/token-usage.md`: R8을 이번 화면 요구로 바꾸고 R11(실시간 갱신)을 더하며 이력 한 줄 추가

## 결정

- 차트 라이브러리는 ECharts(D004). 버전은 `6.1.0`(2026-05-19 배포)으로 고정: 지금 최신이고 배포 뒤 반년이 지났다 / 검토한 대안: `echarts-for-react` 래퍼 — 의존성이 하나 늘고 인스턴스 수명·`replaceMerge`를 직접 다뤄야 하는 건 같아 쓰지 않음
- 두 차트를 한 인스턴스의 그리드 두 개로 그림: `dataZoom.xAxisIndex: [0, 1]`과 `axisPointer.link`로 C3 연동이 설정만으로 된다 / 검토한 대안: 인스턴스 두 개 + `echarts.connect` — 높이·여백을 따로 맞춰야 하고 확대 슬라이더가 둘이 됨
- 범례는 DOM 버튼: 색 견본·계열 이름·숨김 상태를 접근성 있게 보이고 jsdom에서 테스트할 수 있다. 숨김은 옵션의 `legend.selected`로 ECharts에 넘긴다 / 검토한 대안: ECharts 기본 범례 — canvas라 테스트할 수 없고 숨김 상태를 Redux와 맞추려면 이벤트를 따로 받아야 함
- 색 배정·숨김을 Redux에 둠: 조회 응답(`loadSeries.fulfilled`)과 요청 조건(`action.meta.arg`)을 한곳에서 보므로 "기준·필터가 바뀌면 다시 매김"(C1)을 리듀서에서 판단하고 테스트할 수 있다
- 조회 트리거를 `refreshSeq`로 통일: 실시간 갱신에서 기간을 옮겨도 같은 분 안이면 `filters`가 바뀌지 않아 effect가 돌지 않는다. `refresh`가 기간을 옮기고 `refreshSeq`를 올리면, effect(`[filters, refreshSeq]`)가 정확히 한 번 조회한다
- 확대 범위 유지(C11, D010): `dataZoom`의 `start`/`end`(%)를 옵션 갱신 때 넘기지 않아 ECharts가 이전 비율을 유지하게 한다. 처음 그릴 때만 0~100
- 옵션 갱신 방식(D015): ECharts 기본 병합은 배열 컴포넌트를 인덱스별로 합쳐, 그리드·축이 2개에서 1개로 줄어도 뒤의 것이 남는다. 그래서 비용 차트 유무(그리드 수)가 이전 옵션과 다르면 `notMerge: true`로 새로 그리고(확대 범위는 0~100으로 돌아감), 같으면 `replaceMerge: ['series']`로 계열만 바꾼다. 비용이 null이 되는 것은 토큰 종류 필터를 바꿀 때뿐이라(token-usage R7) 자동 갱신 중의 확대 유지(C11)와 부딪히지 않는다 / 검토한 대안: `replaceMerge`에 `grid`·`xAxis`·`yAxis`를 늘 넣기 — 교체된 축과 `dataZoom`의 연결이 ECharts 내부 동작에 기대 덜 확실함
- 색은 hex CSS 변수로 정의하고 그릴 때 `getComputedStyle`로 읽어 ECharts에 넘김(D016): 요약·범례(DOM)와 차트(canvas)가 같은 값을 쓰고, 테마가 바뀌면 다시 읽어 옵션을 갱신한다. 기존 토큰(`oklch(...)`)은 ECharts가 마우스를 올린 막대의 강조색을 계산하는 zrender 색 해석기가 읽지 못해(hex·rgb·hsl·이름 색만 해석) 넘기지 않는다 / 검토한 대안: 읽은 oklch를 sRGB로 바꾸는 변환 함수 — 변환 코드와 경계 처리(알파, 범위 밖 값)가 늘어남
- 다크 모드 저장은 `localStorage`: 사용자 한 명의 브라우저 설정이라 서버 저장이 필요 없다
- 테마는 클래스를 먼저 바꾸는 외부 저장소(D017): 차트는 테마 값이 바뀐 것을 계기로 CSS 변수를 다시 읽는데, 클래스를 훅의 `useEffect`에서 바꾸면 React가 자식(`UsageChart`)의 effect를 부모보다 먼저 실행해 이전 테마 값을 읽는다. 저장소가 클래스를 동기로 바꾼 뒤 알리면 React가 다시 그릴 때 이미 새 변수 값이 적용돼 있다 / 검토한 대안: 차트가 테마 변경 뒤 `requestAnimationFrame`에서 읽기 — 순서를 타이밍에 맡겨 덜 확정적이고 테스트하기 어려움

## 인터페이스·데이터

Redux `UsageState`에 더하는 필드:

```ts
live: boolean;               // 처음 true
refreshSeq: number;          // refresh마다 +1, 조회 effect의 의존값
lastSuccessAt: string | null; // 마지막 loadSeries 성공 시각(ISO). 실패하면 그대로
colorIndex: Record<string, number>; // 계열 이름 → 팔레트 번호(0~7)
colorKey: string | null;     // 색을 매긴 요청의 groupBy·users·models·tokenTypes를 JSON으로
hidden: string[];            // 범례로 숨긴 계열 이름
```

액션:

- `setLive(on)`: 켜면 `refresh`와 같이 기간을 옮기고 `refreshSeq`+1(D011). 끄면 `live`만 false
- `refresh()`: `live`이면 `shiftPeriodToNow`로 기간을 옮김(보관 하한 적용). `refreshSeq`+1. 새로고침 버튼과 10초 타이머가 같이 씀(C7·C11, D007)
- `setPeriod`: 지금 동작 + `live = false`
- `toggleHidden(name)`: `hidden`에 넣거나 뺌. 계열 이름에 묶이고 응답이 바뀌어도 지우지 않음(C4, C1 끝 문장)

`assignColors(prev: Record<string, number>, series: string[], reset: boolean): Record<string, number>`:

1. `reset`이면 `series` 순서대로 0, 1, 2…를 매기고 8 이상은 8로 나눈 나머지로 돌린다.
2. 아니면 결과는 `series`에 있는 이름만 담는다(빠진 계열은 색을 잃음, D014). 두 단계로 매긴다. ① `prev`에 있고 `series`에도 있는 이름의 번호를 먼저 모두 결과에 넣는다. ② 그다음 `series` 순서대로 새 이름에 "결과에 아직 없는 번호 가운데 가장 작은 것"을 매긴다. 8개를 모두 쓰고 있으면 0부터 다시(나머지). 서버가 계열 이름을 정렬해 돌려주므로(`DuckDbUsageStore`의 `SortedSet`) 새 이름이 기존 이름보다 앞에 오는 일이 흔해, 한 번에 훑으면 새 이름이 기존 번호를 먼저 가져가 같은 색이 생긴다.
3. `reset`은 `colorKey`가 요청의 키와 다를 때(나눠 볼 기준·사용자·모델·토큰 종류 필터가 바뀜) 참(D013).

`buildChartOption` 출력의 뼈대:

- 비용 있음: `grid` 2개(위 높이 약 62%, 아래 약 22%), `xAxis` 2개(`category`, 같은 라벨, 위 축 라벨 숨김), `yAxis` 2개(토큰: `formatCompact`, 비용: `$` + 값), `dataZoom` 내부(휠·드래그) + 슬라이더, 둘 다 `xAxisIndex: [0, 1]`, `axisPointer.link: [{ xAxisIndex: 'all' }]`, `tooltip.trigger: 'axis'`
- 비용 없음(`buckets`의 `costUsd`가 모두 null): 그리드·축 하나, 비용 시리즈 없음
- 토큰 시리즈: 계열마다 `type: 'bar'`, `stack: 'tokens'`, `name` = 계열 이름, `color` = `palette[colorIndex[name]]`
- 비용 시리즈: `type: 'bar'`, `xAxisIndex: 1`, `yAxisIndex: 1`, 색은 `--chart-cost`
- 축 글자·격자 색은 `--chart-axis`·`--chart-grid`. `buildChartOption`이 받는 `palette`는 이 hex 값들(`series[8]`, `cost`, `axis`, `grid`)이다
- `legend: { show: false, selected: { [name]: !hidden.includes(name) } }`
- 툴팁 formatter: 구간 라벨, 보이는 계열마다 색 점·이름·`formatTokens`, 보이는 계열 합계, 비용이 null이 아니면 `formatUsd`(소수 넷째 자리). 문자열은 HTML 이스케이프

## 오류·위험

- jsdom에 canvas가 없어 ECharts 실제 그리기는 단위 테스트로 볼 수 없다 → 옵션 생성을 순수 함수로 떼어 테스트하고, 실제 그림은 브라우저에서 확인한다(검증 전략).
- 번들 증가: ECharts 필요 모듈만 등록해도 gzip 기준 수백 KB 미만으로 늘 것으로 본다. 빌드 출력으로 크기를 적어 둔다(목표 아님, 기록).
- 10초마다 조회: 자동 갱신은 조회 중이면 그 주기를 건너뛰어(D019) 서로 겹치지 않는다. 요청이 겹치는 것은 사용자가 조회 중에 새로고침·필터·기간을 조작할 때뿐이고, 그때는 마지막 요청이 15초 안에 끝나 결과·오류가 가려지지 않는다. 서버 부하는 조회 하나라 무시할 만하다.
- 숨은 탭 타이머 지연: 브라우저 정책이라 막지 않는다(C11 단서, D006).
- 실패가 이어질 때: 오류는 요청을 보낼 때 지우지 않고 성공할 때만 지우므로, 실시간 갱신 중에도 깜빡이지 않고 계속 보인다. 이전 차트와 마지막 성공 시각은 남는다(C7). 서버가 응답 없이 멈추면 15초 시간 제한으로 실패가 된다(D018). 슬라이스는 마지막 요청 결과만 반영하므로(`seriesRequestId`) 앞선 요청의 실패는 무시된다. 그래서 10초 타이머가 조회 중에 다음 요청을 보내면 시간 초과가 늘 무시된다. 이를 막기 위해 타이머는 조회 중이면 그 주기를 건너뛴다(D019). 자동 갱신은 겹치지 않아, 서버가 멈추면 15초 뒤 오류가 보이고 그 뒤 첫 주기에 다시 조회한다. 사용자가 조회 중에 새로고침을 누르면 요청이 겹칠 수 있으나 이는 사용자 조작이고 마지막 요청 결과로 화면이 맞춰진다.
- 테마 전환 때 ECharts 색: hex 차트 변수(`--series-*`, `--chart-cost`, `--chart-axis`, `--chart-grid`)를 다시 읽어 옵션을 다시 넣는다(D016, D017). 첫 그리기 전 테마는 `index.html` 스크립트로 맞춘다.
- `localStorage`가 막힌 환경(사생활 보호 모드 등): try/catch로 감싸 운영체제 설정을 따른다.

## 검증 전략

- 자동: `npm test`(vitest), `npm run lint`, `npm run build`(Client 폴더). 빌드 출력의 번들 크기를 기록한다.
- 실제 화면: 서버(`Biz.Bizadm.ClaudeTokenUsage.Server.Spa`, Development)와 Vite 개발 서버를 띄우고, OTLP JSON 표본을 `POST /v1/metrics`로 넣은 뒤 브라우저 패널에서 본다.

| 조건 | 확인 방법 |
|---|---|
| C1 | `seriesColors.test.ts`: reset·유지·새 계열이 빈 색·빠진 계열 색 반환·8개 초과 순환, 새 이름이 기존 이름보다 앞에 오는 경우(`prev={input:0, output:1}`, `series=[cacheCreation, input, output]` → `cacheCreation=2`). `usageSlice.test.ts`: 기준/필터 변경 시 재배정, 기간 변경·refresh 때 유지. `chartOption.test.ts`: 시리즈 색 = 팔레트[번호]. `index.css`의 `--series-*`·`--chart-*` 값이 모두 hex인지 확인하는 테스트(파일을 읽어 정규식). 화면: 토큰 종류 4계열이 서로 다른 색, 라이트·다크, 막대에 마우스를 올려도 색이 유지됨 |
| C2 | `chartOption.test.ts`: 비용 있으면 그리드 2·비용 시리즈 1, 모두 null이면 그리드 1·비용 시리즈 0. `UsageChart.test.tsx`: 비용 있음 → 없음으로 다시 그릴 때 가짜 `setOption`의 두 번째 인자가 `{ notMerge: true }`, 같은 모양이면 `{ replaceMerge: ['series'] }`. 화면: 화면을 연 뒤 토큰 종류 필터를 켜면 비용 그리드·축까지 사라짐 |
| C3 | `chartOption.test.ts`: `dataZoom[].xAxisIndex = [0,1]`, `axisPointer.link`. 화면: 슬라이더·휠로 확대하면 두 그리드가 함께 바뀌고 마우스 위치가 두 그리드에 표시, 가리킨 막대 색이 유지됨 |
| C4 | `UsageChart.test.tsx`: 범례 버튼을 누르면 `aria-pressed`가 바뀌고 `setOption`에 넘긴 `legend.selected[name]`가 false. 화면에서 막대가 사라졌다 돌아옴 |
| C5 | `format`·`chartOption.test.ts`: 160000 → "160K", 비용 축 "$", 툴팁 formatter가 숨긴 계열을 합계에서 뺌·천 단위·`$0.2500`. 화면: 툴팁 확인 |
| C6 | `UsageSummary.test.tsx`: 계열별 합계·비율, 숨김과 무관, 총합 0이면 "—", 토큰 종류 필터 안내 |
| C7 | `usageSlice.test.ts`: 성공 때 `lastSuccessAt` 갱신·`error` 지움, 실패 때 `lastSuccessAt` 유지, 다음 요청 `pending`에서 `error` 유지. `api` 테스트: 시간 초과(가짜 `fetch`가 `TimeoutError`로 거부)가 "응답 시간이 초과되었습니다" 오류가 됨. `App.test.tsx`(가짜 타이머, 조회가 끝나지 않는 가짜 `fetch`): 조회 중에는 10초 주기에 조회 요청이 더 나가지 않고, 그 조회가 시간 초과로 거부되면 오류가 보임. `refresh`가 live에 따라 기간을 옮기거나 그대로 두고 `refreshSeq`+1. `App.test.tsx`: 새로고침 누르면 조회 요청이 한 번 더 감. 화면: 조회 중 아이콘 |
| C8 | `theme` 테스트(`App.test.tsx` 또는 별도): 토글하면 `<html>.dark`와 `localStorage` 값이 바뀌고, 값이 없으면 `matchMedia` 결과를 따름. 운영체제 변경(가짜 `matchMedia`의 change 이벤트) 뒤에도 `localStorage`는 비어 있고 클래스는 따라 바뀜, 저장값이 있으면 운영체제 변경을 무시함. 토글 직후 가짜 `setOption`에 마지막으로 넘긴 팔레트가 새 테마의 변수 값임(테스트에서 `.dark` 아래 변수를 다르게 정의). 화면: 토글 한 번에 차트 계열·축·격자 색까지 바뀜, 새로고침 후 유지 |
| C9 | 브라우저 패널을 375×812로 바꿔 `document.documentElement.scrollWidth <= 375` 확인과 화면 캡처 |
| C10 | 기존 테스트(`usageSlice`, `periods`, `App`, 필터 동작) 통과, lint·build 통과. 화면: 단위·기간·필터·나눠 볼 기준 조작 |
| C11 | `periods.test.ts`: `shiftPeriodToNow`(길이 유지, 분 경계, 하한). `usageSlice.test.ts`: 처음 `live`=true, `setPeriod`가 끔, `setLive(true)`가 옮기고 seq+1. `App.test.tsx`(가짜 타이머): live면 10초마다 조회, 끄면 멈춤. `chartOption.test.ts`: 옵션에 `dataZoom` start/end를 넣지 않음(갱신 시). 화면: 오른쪽 끝 확대 상태에서 갱신 뒤에도 최신 구간을 따라감 |
| C12 | 화면 캡처(라이트·다크): 배경과 카드가 다른 색, 영역 간격 동일(`gap-6` 한 곳에서 지정), 카드는 모두 같은 `Card` 컴포넌트 |

## 원칙 점검

- 적용 규칙: Bizadm MCP `workflow/spec-driven.md`(절차·문서), `workflow/repository-content.md`(저장소 파일에 개인 경로·비밀 정보 넣지 않음, 저장소 기준 상대 경로), 전역 `language-preference.md`(대화는 한국어, 코드·주석은 저장소 관례 — 이 저장소는 주석이 한국어), 전역 `portable-python.md`(이번 작업에 Python 없음), `github.md`(이번 작업은 GitHub 접근 없음)
- 저장소 관례: 클라이언트는 React 18 + Redux Toolkit + Tailwind 4 + shadcn(radix-nova) 구성, 기능별 폴더(`features/usage`), 테스트는 vitest + Testing Library, 주석은 한국어. 계획은 이 구성을 그대로 따른다.
- 충돌: 없음. 검증 전략의 OTLP 표본 데이터에는 실제 이메일 대신 `a@example.com` 같은 예시 값을 쓴다(저장소 파일에 남는 테스트 데이터도 같음).
