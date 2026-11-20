# Claude Code 토큰 사용량 수집·롤업·차트 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로의 `{S}`는 `Biz.Bizadm.ClaudeTokenUsage`다.

## 단계 1: 솔루션 기반(템플릿)

- [x] T001 SDK·런타임 확인(`dotnet --list-sdks`에 11.0.1xx, `dotnet --list-runtimes`에 Microsoft.AspNetCore.App 10.0.x), `global.json` 작성(sdk 11.0.100, `rollForward: latestFeature`). 없으면 멈추고 사용자에게 알린다 (`global.json`) — C10
- [x] T002 `Directory.Build.props`(net10.0, Nullable, ImplicitUsings), `Directory.Packages.props`(plan 패키지 목록), `.gitignore` 추가 항목 (`Directory.Build.props`, `Directory.Packages.props`, `.gitignore`) — C10
- [x] T003 `{S}.Server.Core` 템플릿 파일 작성 (`src/{S}.Server.Core/{S}.Server.Core.csproj`, `ExitCode.cs`, `OsKmsConfiguration.cs`, `TlsConfiguration.cs`, `KmsSecretProtector.cs`, `ServerLoggingExtensions.cs`, `ServerKestrelExtensions.cs`) — C10
- [x] T004 `{S}.Server.Core.Tests` 템플릿 테스트 작성 (`tests/{S}.Server.Core.Tests/`: csproj, `OsKmsConfigurationTests.cs`, `TlsConfigurationTests.cs`, `ServerKestrelExtensionsTests.cs`, `KestrelTlsTests.cs`) — C10
- [x] T005 `{S}.Server.Spa` 뼈대: 템플릿 `Program.cs`(SPA 차이 포함), `AppConfiguration.cs`(`WebRootPath`, `DatabaseFilePath` 추가), `appsettings*.json`, `Properties/launchSettings.json`, csproj(SpaProxy 속성, publish Target `PublishClientApp`) (`src/{S}.Server.Spa/`) — C8, C10
- [x] T006 `{S}.Server.Spa.Tests` 템플릿 테스트: `PipelineTests`(정적 파일·HTML fallback·비HTML 404·`/health`), `AppConfigurationTests`(OsKms 없음 실패, `WebRootPath`·`DatabaseFilePath` 기본값) (`tests/{S}.Server.Spa.Tests/`) — C8, C10
- [x] T007 `{S}.Client` 생성: `npm create vite@7` → 폴더 이름 변경, React 18.3·TS ~5.9·Vite ~7.3·Tailwind ~4.1·Redux Toolkit 2·react-redux 9·Vitest ~5.1 고정, shadcn `init --base radix --preset nova`, `@/` alias, `vite.config.ts`(5173 strictPort, `/api` 프록시), `src/app/store.ts`·`hooks.ts`, `src/test/setup.ts`, 최소 `App.tsx` 한 화면과 렌더링 테스트 `App.test.tsx`(템플릿), 데모 파일 삭제, `<title>` 변경 (`src/{S}.Client/`) — C8
- [x] T008 솔루션 등록(`dotnet sln add`)과 패키지 확인: NuGet 취약·Deprecated(`dotnet package list --vulnerable --include-transitive`, `--deprecated`), `npm ls`(React 19·Vite 8 중첩 없음), `npm audit`·`npm audit --omit=dev` 기록 (`{S}.slnx`) — C10

체크포인트: `dotnet build {S}.slnx --no-incremental` 경고 0, `dotnet test {S}.slnx` 통과. 프론트엔드 `npx tsc -b`·`npm run lint`·`npm test`·`npm run build`가 모두 0이다.

## 단계 2: Core 도메인

- [x] T009 [P] 단위·KST·구간 계산: `UsageUnit`(하위 단위, 직접 상위 단위 목록), `KstClock`(UTC+9 고정), `BucketCalculator`(`Floor`·`Ceil`·`Next`, 주는 월요일), 프로젝트를 솔루션에 등록 (`src/{S}.Core/{S}.Core.csproj`, `UsageUnit.cs`, `KstClock.cs`, `BucketCalculator.cs`, `{S}.slnx`) — C4
- [x] T010 보관 기준: `RetentionPolicy`(`Cutoff`: 분·시 고정 길이, 일·주·월 `AddMonths`/`AddYears` 말일 맞춤, 년 null; `RetainedFrom` = `Ceil(unit, Cutoff)`) (`src/{S}.Core/RetentionPolicy.cs`) — C5
- [x] T011 [P] 속성 찾기와 포인트 모델: `UsageAttributes`(포인트 → 리소스, `user.email` → `user.account_uuid` → `user.id` → `unknown`, `model`·`type` 없으면 `unknown`), `TokenUsagePoint`·`CostUsagePoint` (`src/{S}.Core/UsageAttributes.cs`, `UsagePoint.cs`) — C2
- [x] T012 Core 테스트: `BucketCalculatorTests`(UTC 14:59/15:00 KST 자정, 일→월요일, 월말·연말·윤년, `Ceil` 경계), `RetentionPolicyTests`(11-20 일 컷오프 10-20, 03-31 − 1개월 = 02-28, 윤년 02-29 − 1년, 분·시 고정 길이, 주 `RetainedFrom`이 월요일), `UsageAttributesTests`(찾기 순서), 테스트 프로젝트를 솔루션에 등록 (`tests/{S}.Core.Tests/`, `{S}.slnx`) — C2, C4, C5

체크포인트: `dotnet test tests/{S}.Core.Tests` 통과.

## 단계 3: Infrastructure(OTLP 해석, DuckDB)

- [x] T013 Infrastructure 프로젝트와 OTLP proto: csproj(`Google.Protobuf`, `Grpc.Tools` PrivateAssets all, `<Protobuf GrpcServices="None">`, `DuckDB.NET.Data.Full`), opentelemetry-proto 파일 4개를 Apache-2.0 머리말 그대로 넣음, 프로젝트를 솔루션에 등록 (`src/{S}.Infrastructure/{S}.Infrastructure.csproj`, `src/{S}.Infrastructure/Protos/opentelemetry/proto/...`, `{S}.slnx`) — C1
- [x] T014 `OtlpMetricsParser`(protobuf·JSON, JSON은 `exemplars` 제거 후 `JsonParser` 알 수 없는 필드 무시, 대상 지표 두 개의 delta `Sum` 포인트만, 누적 포인트 수 반환, `as_int`/`as_double` 처리)와 `OtlpPayloadException` (`src/{S}.Infrastructure/Otlp/OtlpMetricsParser.cs`, `Otlp/OtlpPayloadException.cs`) — C1, C2
- [x] T015 `OtlpMetricsParserTests`(protobuf·JSON 같은 결과, 다른 지표 제외, 누적 제외, 리소스 속성 대체, `as_double` 반올림, exemplar 포함 JSON, 깨진 본문 예외), 테스트 프로젝트를 솔루션에 등록 (`tests/{S}.Infrastructure.Tests/{S}.Infrastructure.Tests.csproj`, `OtlpMetricsParserTests.cs`, `{S}.slnx`) — C1, C2
- [x] T016 `DuckDbUsageStore` 스키마 생성(`token_usage`, `cost_usage`, `rollup_state`, DB 폴더 생성)과 `IngestAsync`(보관 밖 건너뜀, 분은 더함, 상위는 확정 구간만 증분, 잠금·트랜잭션) (`src/{S}.Infrastructure/Storage/DuckDbUsageStore.cs`) — C2, C6
- [x] T017 `DuckDbUsageStore.RunMaintenanceAsync`: 시→일→주→월→년 재계산(확정 경계 뒤 DELETE + INSERT…SELECT `date_trunc`), `finalized_until` 갱신, 보관 삭제(Cutoff 앞이고 직접 상위 확정) (`src/{S}.Infrastructure/Storage/DuckDbUsageStore.cs`) — C3, C5, C6
- [x] T018 조회: `UsageQuery`·`UsageSeries` 모델, `QuerySeriesAsync`(범위 `[max(Floor(from), RetainedFrom), min(to, 현재 구간 끝))`, 0 채우기, 필터, `groupBy`, 비용 null·합계 규칙), `QueryMetaAsync`(earliest, 사용자·모델·토큰 종류 목록) (`src/{S}.Infrastructure/Storage/UsageQuery.cs`, `Storage/UsageSeries.cs`, `Storage/DuckDbUsageStore.cs`) — C7
- [x] T019 저장소 테스트(FakeTimeProvider, 임시 DB 파일): `DuckDbUsageStoreIngestTests`(같은 분·키 합산, 끝 시각 분, 속성 찾기 결과(리소스 속성에만 있는 `user.email` 등)로 행이 저장됨, 확정 시 구간 늦은 값, 분 보관 지난 늦은 값, 일이 지워진 시각의 주·월·년 반영), `DuckDbUsageStoreMaintenanceTests`(상위 합계 = 하위 합계, 보관 삭제와 삭제 뒤 상위 합계 유지, 닫고 다시 열기, 사흘 공백 뒤 첫 유지 작업), `DuckDbUsageStoreQueryTests`(0 채우기, 미래 제외, 보관 밖 제외, 주 하한이 월요일, 비용 null·합계) (`tests/{S}.Infrastructure.Tests/`) — C2, C3, C5, C6, C7

체크포인트: `dotnet test tests/{S}.Infrastructure.Tests` 통과.

## 단계 4: 서버 API와 서비스

- [x] T020 `Server.Spa` csproj에 Core·Infrastructure `ProjectReference` 추가. `OtlpMetricsController`: `POST /v1/metrics`, Content-Type 판별(415), gzip 풀기, 파서 호출(400), `IngestAsync`, 같은 형식 응답 본문, 누적 포인트 경고 로그 (`src/{S}.Server.Spa/{S}.Server.Spa.csproj`, `src/{S}.Server.Spa/Controllers/OtlpMetricsController.cs`) — C1
- [x] T021 `UsageController`: `GET /api/usage/series`(인자 검증 400, 오프셋 있으면 그대로·없으면 KST, `from` 내림·`to` 올림, 시작≥끝 400), `GET /api/usage/meta` (`src/{S}.Server.Spa/Controllers/UsageController.cs`) — C7
- [x] T022 `UsageMaintenanceService`(`StartAsync`에서 유지 작업 한 번, 이후 `PeriodicTimer` 1분, 예외 로그 후 계속)와 `Program.cs` 등록(`TimeProvider.System`, `DuckDbUsageStore` 싱글턴, hosted service, `AddWindowsService()`, `AddSystemd()`), csproj에 `Microsoft.Extensions.Hosting.WindowsServices`·`Microsoft.Extensions.Hosting.Systemd` 패키지 참조 추가 (`src/{S}.Server.Spa/Services/UsageMaintenanceService.cs`, `src/{S}.Server.Spa/Program.cs`, `src/{S}.Server.Spa/{S}.Server.Spa.csproj`) — C6, C10
- [x] T023 서버 테스트: `OtlpMetricsEndpointTests`(protobuf 200·본문, JSON 200 `{}`, gzip 200, 깨진 본문 400, `text/plain` 415, 대상 외·누적 미저장), `UsageApiTests`(경계 내림·올림, 경계에 맞는 끝은 올리지 않음, 0 채우기, 미래·보관 밖 제외, 전체 보관 밖 200 빈 계열, 오프셋, 400 다섯 경우, 비용 null·합계, 유지 작업 한 번 뒤 모든 단위 반영), `UsageMaintenanceServiceTests`(시작 직후 실행, 1분 주기) (`tests/{S}.Server.Spa.Tests/`) — C1, C6, C7

체크포인트: `dotnet build` 경고 0, `dotnet test {S}.slnx` 통과.

## 단계 5: 화면

- [x] T024 조회 상태: `types.ts`(API 응답 타입), `api.ts`(`/api/usage/series`·`meta` 호출), `usageSlice.ts`(필터 상태, `createAsyncThunk`), store 등록 (`src/{S}.Client/src/features/usage/types.ts`, `api.ts`, `usageSlice.ts`, `src/app/store.ts`) — C8
- [x] T025 [P] 기간 계산: 단위별 기본 기간(분 60분, 시 24시간, 일 14일, 주 12주, 월 12개월, 년 5년), 보관 하한(`earliest`) 제한, KST 입력값을 `+09:00` ISO로 바꾸기 (`src/{S}.Client/src/features/usage/periods.ts`) — C8
- [x] T026 화면 구성: shadcn 컴포넌트 추가(`select`, `input`, `label`, `card`, `chart`, `button`, `toggle-group` 등, React 18 forwardRef 보정), `UsageFilters`·`UsageChart`(쌓은 막대 + 비용 보조 축 선, null이면 선 없음)·`UsageSummary`(총합, 토큰 종류 필터 안내), `App.tsx` (`src/{S}.Client/src/components/ui/`, `src/features/usage/components/UsageFilters.tsx`, `UsageChart.tsx`, `UsageSummary.tsx`, `src/App.tsx`) — C8
- [x] T027 프론트엔드 테스트: `App.test.tsx` 고침(사용량 화면 렌더링), `periods.test.ts`(기본 기간·하한), `usageSlice.test.ts`(필터 → 요청 인자), `UsageSummary.test.tsx`(비용 안내), `UsageChart.test.tsx`(계열 렌더링) (`src/{S}.Client/src/App.test.tsx`, `src/features/usage/*.test.ts(x)`) — C8

체크포인트: 프론트엔드 `npx tsc -b`·`npm run lint`·`npm test`·`npm run build`가 모두 0이다. `npm ls`로 버전 중첩이 없는지 확인한다.

## 단계 6: 배포 파일·문서·기준 spec

- [x] T028 [P] 배포 예시: `deploy/config.example.yaml`(placeholder만), `deploy/systemd/biz-bizadm-claudetokenusage.service`(`Type=notify`, `ExecStart`에 `--config`/`--log`) (`deploy/config.example.yaml`, `deploy/systemd/biz-bizadm-claudetokenusage.service`) — C9, C10
- [x] T029 README: 빌드·publish, 콘솔 실행, Windows 서비스 등록(`sc.exe`, 절대 경로), systemd 등록, Claude Code 환경 변수와 `settings.json` 예시, temporality 안내, DB 파일 단일 프로세스, 조회 API 요약, opentelemetry-proto 출처·라이선스 (`README.md`) — C9
- [x] T030 기준 spec 작성: 요구 R1~R10(spec.md C1~C10을 옮김), `파일:` 줄(이 기능의 src·tests·deploy·README 경로), 이력 한 줄(2026-11-20, token-usage-rollup, R1~R10 추가) (`docs/features/token-usage.md`) — C1, C2, C3, C4, C5, C6, C7, C8, C9, C10
- [x] T031 Linux systemd KEK 저장소 안내(D015): 유닛 예시에 `GCM_CREDENTIAL_STORE=gpg`·`GPG_TTY=/dev/null`, README "systemd"에 서비스 계정·gpg 키·`.password-store/.gpg-id` 준비 절차 (`deploy/systemd/biz-bizadm-claudetokenusage.service`, `README.md`) — C9, C10

체크포인트: README에 C9 항목이 모두 있다. 기준 spec의 R1~R10이 spec.md C1~C10과 같고, `파일:` 줄의 경로가 실제로 있다. `dotnet build`·`dotnet test`·프론트엔드 검사를 다시 통과한다.

## 의존 관계

- 단계 1 → 나머지 전부: SDK·솔루션·템플릿이 있어야 빌드된다. T001이 실패하면(SDK 11 또는 런타임 10 없음) 아무것도 진행하지 않는다.
- 단계 1 안: T002 → T003 → T004. T002 → T005 → T006(T003 필요). T007은 T005의 `SpaRoot`와 맞추지만 파일이 겹치지 않아 T003~T006과 동시에 할 수 있다. T008은 T003~T007 뒤에 한다.
- 단계 2 → 단계 3: 저장소와 파서가 `BucketCalculator`·`RetentionPolicy`·`UsageAttributes`·`UsagePoint`를 쓴다. T009 → T010(`Ceil` 사용). T011은 T009·T010과 동시에 할 수 있다.
- 단계 3 안: T013 → T014 → T015. T013 → T016 → T017 → T018(T016은 T013의 csproj·DuckDB 참조가 필요하고, T016~T018은 같은 파일 `DuckDbUsageStore.cs`라 순서대로). T015·T018 → T019(T015의 테스트 프로젝트·솔루션 등록이 필요).
- 단계 3 → 단계 4: 컨트롤러·서비스가 파서와 저장소를 쓴다. T020이 csproj에 Core·Infrastructure 참조를 더하므로 T020 → T021 → T022 순서로 한다(T020·T022가 같은 csproj를 고침). T020·T021·T022는 모두 `Program.cs` 등록에 기대므로 T022 뒤에 T023을 한다.
- 단계 4 → 단계 5: 화면이 조회 API 응답 형식(T018·T021)을 따른다. T024 → T026 → T027. T025는 T024와 동시에 할 수 있다.
- 단계 6: T028은 단계 1 뒤 언제든 할 수 있다. T029는 단계 4의 API와 T028의 예시 파일 이름이 정해진 뒤에 한다. T030은 마지막 구현 작업이며 다른 모든 작업 뒤에 한다(파일 목록을 확정하기 위해).
