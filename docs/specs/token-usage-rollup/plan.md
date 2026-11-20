# Claude Code 토큰 사용량 수집·롤업·차트 계획

## 요약

표준 SPA 서버 템플릿(`dotnet/server-spa-template.md`, 공통 기반은 `dotnet/server-api-template.md`)으로 솔루션을 만든다. 서버 하나(`Biz.Bizadm.ClaudeTokenUsage.Server.Spa`)가 다음을 모두 맡는다.

- OTLP/HTTP 지표 받기(`POST /v1/metrics`)
- 조회 API(`/api/usage/...`)
- React 화면 제공

도메인 계산(구간·보관 기준)은 `Core`, OTLP 해석과 DuckDB 저장·롤업·조회는 `Infrastructure`에 둔다.

롤업 방식:

- 저장은 단위별 행을 한 테이블에 둔다(`unit` 열).
- 단위마다 "확정 경계"(`finalized_until`)를 둔다. 경계 앞의 끝난 구간은 확정되어 다시 계산하지 않는다. 경계 뒤의 구간(현재 구간과 아직 확정하지 않은 구간)은 1분마다 하위 단위에서 다시 계산한다.
- 늦게 온 값을 받는 방식:
  - 분 단위가 보관 중이면 분 행에 더한다.
  - 상위 단위 구간이 이미 확정된 경우에만 그 상위 행에 직접 더한다(증분).
  - 확정 전인 상위 구간은 다음 재계산이 하위 행에서 가져간다.
  - 그래서 모든 보관 단위에 값이 정확히 한 번씩 들어간다(C2, D006, D008).

## 바꿀 파일·구조

루트

- `global.json`: 새 파일. SDK `11.0.100`, `rollForward: latestFeature` (D014). TFM은 `net10.0` 그대로
- `Biz.Bizadm.ClaudeTokenUsage.slnx`: 아래 프로젝트 등록
- `Directory.Build.props`: 새 파일. `net10.0`, `Nullable`, `ImplicitUsings`
- `Directory.Packages.props`: 새 파일. 중앙 패키지 버전(인터페이스·데이터 절)
- `.gitignore`: `node_modules/`, `dist/`, `*.duckdb`, `*.duckdb.wal`, `TestResults/` 추가
- `README.md`: 새 파일 (C9)
- `deploy/config.example.yaml`: 새 파일. 설정 예시(placeholder만)
- `deploy/systemd/biz-bizadm-claudetokenusage.service`: 새 파일. systemd 유닛 예시 (C10)
- `docs/features/token-usage.md`: 새 파일. 기준 spec(마지막 구현 단계에서 작성)

`src/Biz.Bizadm.ClaudeTokenUsage.Server.Core/` (템플릿 그대로)

- `Biz.Bizadm.ClaudeTokenUsage.Server.Core.csproj`, `ExitCode.cs`, `OsKmsConfiguration.cs`, `TlsConfiguration.cs`, `KmsSecretProtector.cs`, `ServerLoggingExtensions.cs`, `ServerKestrelExtensions.cs`

`src/Biz.Bizadm.ClaudeTokenUsage.Core/` (도메인, 기술 의존 없음)

- `Biz.Bizadm.ClaudeTokenUsage.Core.csproj`
- `UsageUnit.cs`: 단위 열거(Minute, Hour, Day, Week, Month, Year), 단위별 하위 단위(Hour→Minute, Day→Hour, Week→Day, Month→Day, Year→Month)와 직접 상위 단위 목록(Minute→Hour, Hour→Day, Day→Week·Month 둘, Month→Year, Week·Year→없음)
- `KstClock.cs`: KST(UTC+9 고정, 일광 절약 없음) 변환. `DateTimeOffset` → KST 벽시계 `DateTime`
- `BucketCalculator.cs`: `Floor(unit, kst)`(구간 시작), `Ceil(unit, kst)`(그 시각 이상인 첫 구간 시작), `Next(unit, start)`(다음 구간 시작). 주는 월요일 00:00 (C4)
- `RetentionPolicy.cs`: 단위별 보관 기간과 `Cutoff(unit, nowKst)` = 현재 구간 시작 − 보관 기간. 분·시는 고정 길이, 일·주·월은 `AddMonths`/`AddYears`(그 달에 없는 날짜는 말일로 맞춤). 년은 `null`(영구) (C5). `RetainedFrom(unit, nowKst)` = `Ceil(unit, Cutoff)`(보관 하한, 남아 있는 첫 구간 시작). 주의 Cutoff는 대개 월요일이 아니므로 올려서 쓴다
- `UsageAttributes.cs`: 속성 이름 상수와 찾기 규칙. 포인트 속성 → 리소스 속성, 사용자 `user.email` → `user.account_uuid` → `user.id` → `unknown`, `model`·`type`이 없으면 `unknown` (용어)
- `UsagePoint.cs`: `TokenUsagePoint(DateTimeOffset Time, string User, string Model, string TokenType, long Tokens)`, `CostUsagePoint(DateTimeOffset Time, string User, string Model, double CostUsd)`

`src/Biz.Bizadm.ClaudeTokenUsage.Infrastructure/`

- `Biz.Bizadm.ClaudeTokenUsage.Infrastructure.csproj`: `Google.Protobuf`, `Grpc.Tools`(PrivateAssets all, `<Protobuf GrpcServices="None">`), `DuckDB.NET.Data.Full`
- `Protos/opentelemetry/proto/{collector/metrics/v1/metrics_service.proto, metrics/v1/metrics.proto, common/v1/common.proto, resource/v1/resource.proto}`: opentelemetry-proto에서 그대로 가져온 파일(Apache-2.0 머리말 유지)
- `Otlp/OtlpMetricsParser.cs`: protobuf·JSON 본문 → `ExportMetricsServiceRequest` → `TokenUsagePoint`/`CostUsagePoint` 목록. 대상 지표 두 개의 `Sum`이면서 `AGGREGATION_TEMPORALITY_DELTA`인 포인트만 꺼낸다(C1). 값은 `as_int`, 또는 `as_double`을 반올림(토큰)하거나 그대로(비용) 쓴다
- `Otlp/OtlpPayloadException.cs`: 본문 해석 실패
- `Storage/DuckDbUsageStore.cs`: 스키마 생성, `IngestAsync`, `RunMaintenanceAsync`(재계산·확정·보관 삭제), `QuerySeriesAsync`, `QueryMetaAsync`. 연결 하나를 `SemaphoreSlim`으로 직렬화한다
- `Storage/UsageQuery.cs`, `Storage/UsageSeries.cs`: 조회 입력·결과 모델

`src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa/` (SPA 템플릿 + 이 기능)

- `Biz.Bizadm.ClaudeTokenUsage.Server.Spa.csproj`: 템플릿 + `Microsoft.Extensions.Hosting.WindowsServices`, `Microsoft.Extensions.Hosting.Systemd`, Core·Infrastructure 참조, publish Target
- `Program.cs`: 템플릿 + `AddWindowsService()`·`AddSystemd()`(D013), `DuckDbUsageStore` 싱글턴, `UsageMaintenanceService` 등록, `TimeProvider.System` 등록
- `AppConfiguration.cs`: 템플릿 + `WebRootPath`(기본 `ClientApp`), `DatabaseFilePath`(기본 `data/token-usage.duckdb`, `ContentRootPath` 기준)
- `Controllers/OtlpMetricsController.cs`: `POST /v1/metrics` (C1)
- `Controllers/UsageController.cs`: `GET /api/usage/series`, `GET /api/usage/meta` (C7)
- `Services/UsageMaintenanceService.cs`: `BackgroundService`. `StartAsync`에서 밀린 처리를 한 번 끝낸 뒤 1분 `PeriodicTimer`로 `RunMaintenanceAsync` (C6)
- `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`: 템플릿

`src/Biz.Bizadm.ClaudeTokenUsage.Client/` (React, `frontend/react-project-creation.md`)

- 템플릿 생성물(`package.json`, `vite.config.ts`, `tsconfig*.json`, `index.html`, `eslint.config.js`, `components.json`, `src/main.tsx`, `src/App.tsx`, `src/index.css`, `src/app/store.ts`, `src/app/hooks.ts`, `src/lib/utils.ts`, `src/test/setup.ts`)
- `vite.config.ts`: 프록시 `/api` → `SERVER_URL ?? http://localhost:5000`
- `src/features/usage/types.ts`, `api.ts`, `usageSlice.ts`: 조회 상태·thunk
- `src/features/usage/components/UsageFilters.tsx`: 단위·기간·사용자·모델·토큰 종류·나눠 볼 기준 선택
- `src/features/usage/components/UsageChart.tsx`: 쌓은 막대(토큰) + 보조 축 선(비용 USD)
- `src/features/usage/components/UsageSummary.tsx`: 기간 토큰·비용 총합, 토큰 종류로 거르면 비용 안내
- `src/features/usage/periods.ts`: 단위별 기본 기간, 보관 하한(`meta.units[].earliest`)으로 제한
- `src/components/ui/*`: shadcn 생성물(`button`, `select`, `input`, `label`, `card`, `chart`, `toggle-group` 등 필요한 것)
- 테스트: `src/App.test.tsx`, `src/features/usage/*.test.ts(x)`

`tests/`

- `Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests/`: 템플릿 테스트
- `Biz.Bizadm.ClaudeTokenUsage.Core.Tests/`: `BucketCalculatorTests`, `RetentionPolicyTests`, `UsageAttributesTests`
- `Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests/`: `OtlpMetricsParserTests`, `DuckDbUsageStoreIngestTests`, `DuckDbUsageStoreMaintenanceTests`, `DuckDbUsageStoreQueryTests`
- `Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests/`: 템플릿 `PipelineTests`(정적 파일·fallback·404·health) + `OtlpMetricsEndpointTests`, `UsageApiTests`, `UsageMaintenanceServiceTests`, `AppConfigurationTests`

## 결정

- 이미 설치된 .NET 11 SDK(11.0.100)로 TFM `net10.0`을 빌드하고 `global.json`으로 SDK를 고정한다: SDK 11은 net10.0 대상을 빌드하고, 실행 런타임 10.0.15가 설치되어 있다. TFM과 Microsoft.* 패키지(10.0.x)는 템플릿 그대로다 / 검토한 대안: .NET 10 SDK 설치(D012, 사용자가 D014로 대체), net11.0 대상(Biz.Bizadm.* 패키지가 net11에서 검증되지 않았고 바뀌는 범위가 큼) (D014, D012 대체)
- 서비스 연동은 `builder.Services.AddWindowsService()`·`AddSystemd()`로 한다. spec C10의 `UseWindowsService`·`UseSystemd`와 같은 기능(서비스 수명 연동, 이벤트 로그·journald 형식)의 서비스 등록 형태다: YAML `ContentRootPath`와 실행 파일 폴더가 달라도 시작된다 / 검토한 대안: `Host.UseWindowsService()`·`UseSystemd()`(서비스 실행 시 콘텐츠 루트를 바꾸려 해 `WebApplicationBuilder`가 예외를 냄) (D013)
- 서버 프로젝트는 `Server.Spa` 하나로 하고, 별도 API 서버(`{Solution}.Server`)는 만들지 않는다: 수신·조회 API와 화면을 한 프로세스가 제공하면 된다(C8). 스킬은 API 서버를 함께 둘지를 요청에 따른다고 정한다 / 검토한 대안: API 서버를 따로 두기(DuckDB 파일을 두 프로세스가 열 수 없어 오히려 문제)
- 도메인은 `Core`, 기술 구현은 `Infrastructure`로 나눈다: 구간·보관 계산을 DB 없이 테스트한다(`dotnet/project-creation.md` 8번) / 검토한 대안: `Server.Spa`에 모두 두기(`Server.Core`는 템플릿 구성 요소만 두게 되어 있음)
- OTLP 메시지는 opentelemetry-proto의 .proto를 저장소에 두고 `Grpc.Tools`로 생성한다. JSON은 `Google.Protobuf.JsonParser`(알 수 없는 필드 무시)로 읽는다: OTLP JSON은 proto3 JSON 매핑(lowerCamelCase, int64 문자열)이라 같은 메시지 타입으로 두 형식을 처리한다 / 검토한 대안: OpenTelemetry Collector 경유(D004에서 제외), 손으로 만든 DTO(필드 누락 위험)
- 저장은 테이블 두 개에 단위 열을 둔다(`token_usage`, `cost_usage`): 단위별 SQL이 같은 모양이고 조회·재계산이 단위만 바꿔 재사용된다 / 검토한 대안: 단위별 테이블 12개(같은 SQL 반복)
- 상위 단위는 "확정 경계 뒤 구간 전체를 하위에서 다시 계산"하고, 확정된 구간에는 늦은 값을 증분으로 더한다: 1분마다 재계산하므로 현재 구간이 2분 안에 모든 단위에 보인다(D010). 확정된 구간은 다시 합산하지 않아 하위 행 삭제 뒤에도 합계가 유지된다(D008) / 검토한 대안: 조회할 때 미확정 구간을 하위 단위에서 재귀로 합산(조회 SQL이 복잡해짐), 받을 때 모든 단위에 즉시 더하기(롤업 형태가 아니고 재계산과 이중 반영 위험)
- 모든 DB 접근(받기·유지 작업·조회)을 연결 하나와 `SemaphoreSlim` 하나로 직렬화한다: 받기와 재계산이 겹쳐 이중 반영되는 것을 막는다. 양이 작아(사용자 몇 명, 분당 수십 행) 성능 문제가 없다 / 검토한 대안: 연결 여러 개 + 트랜잭션 격리(DuckDB 낙관적 동시성 충돌 재시도가 필요해 복잡함)
- 시간은 `TimeProvider`로 주입한다: 테스트에서 `FakeTimeProvider`로 경계·보관·재시작을 재현한다
- 차트는 shadcn `chart`(recharts)를 쓴다: 규칙의 shadcn 우선 원칙을 따르고, 쌓은 막대 + 보조 축 선을 한 차트로 그린다 / 검토한 대안: 다른 차트 라이브러리(규칙의 shadcn 우선과 어긋남)

## 인터페이스·데이터

### 설정 YAML (`deploy/config.example.yaml`)

```yaml
ContentRootPath: <서버 배포 폴더 절대 경로>
Port: 4318
WebRootPath: ClientApp
DatabaseFilePath: data/token-usage.duckdb
OsKms:
  CredentialService: https://biz-bizadm-claudetokenusage.local
  CredentialAccount: claudetokenusage
  Iterations: <PBKDF2 반복 횟수>
  SecretsDekFilePath: secrets.dek
Tls:
  UseTls: false
```

포트 기본 예시는 OTLP/HTTP 관례 포트 4318이다. `DatabaseFilePath`의 폴더가 없으면 기동 시 만든다.

### DuckDB 스키마

```sql
CREATE TABLE IF NOT EXISTS token_usage (
  unit VARCHAR NOT NULL,            -- minute|hour|day|week|month|year
  bucket_start TIMESTAMP NOT NULL,  -- KST 벽시계, 구간 시작
  user_id VARCHAR NOT NULL,
  model VARCHAR NOT NULL,
  token_type VARCHAR NOT NULL,
  tokens BIGINT NOT NULL,
  PRIMARY KEY (unit, bucket_start, user_id, model, token_type)
);
CREATE TABLE IF NOT EXISTS cost_usage (
  unit VARCHAR NOT NULL,
  bucket_start TIMESTAMP NOT NULL,
  user_id VARCHAR NOT NULL,
  model VARCHAR NOT NULL,
  cost_usd DOUBLE NOT NULL,
  PRIMARY KEY (unit, bucket_start, user_id, model)
);
CREATE TABLE IF NOT EXISTS rollup_state (
  unit VARCHAR PRIMARY KEY,         -- hour|day|week|month|year
  finalized_until TIMESTAMP NOT NULL -- 이 시각 앞의 구간은 확정
);
```

더하기는 `INSERT ... ON CONFLICT (...) DO UPDATE SET tokens = tokens + excluded.tokens`(비용도 같음)이다.

### 받기 (`IngestAsync`, 잠금 안, 한 트랜잭션)

포인트마다 KST 시각 `t`에 대해 단위 `u`를 분→년 순으로 본다.

1. `b = Floor(u, t)`. `b < Cutoff(u, now)`이면(보관 기간 밖) 건너뛴다.
2. `u = minute`이면 더한다.
3. 상위 단위는 `b < finalized_until[u]`(확정)일 때만 더한다. 확정 전이면 건너뛴다(재계산이 하위에서 가져감).

확정 전인 상위 구간의 하위 행은 지워지지 않는다(아래 삭제 조건). 그래서 확정 전 구간의 값은 언제나 하위 행에 있고, 재계산으로 정확히 한 번 들어간다.

### 유지 작업 (`RunMaintenanceAsync`, 기동 직후 한 번 + 1분마다, 잠금 안, 한 트랜잭션)

1. 재계산: `u` = hour, day, week, month, year 순(하위가 먼저 최신이 됨)으로 다음을 한다.
   - `from = finalized_until[u]`(없으면 하위 단위의 가장 이른 구간)
   - `DELETE FROM token_usage WHERE unit=u AND bucket_start >= from`
   - `INSERT ... SELECT u, date_trunc(<u>, bucket_start), user_id, model, token_type, SUM(tokens) FROM token_usage WHERE unit=child(u) AND bucket_start >= from GROUP BY ...`
   - 비용도 같다. `date_trunc('week', ...)`는 ISO 월요일이다.
   - `finalized_until[u] = Floor(u, now)`(현재 구간 시작)
2. 보관 삭제: `u` = minute, hour, day, week, month에 대해 다음을 지운다.
   - 조건: `bucket_start < Cutoff(u, now)`이고, `u`를 직접 모으는 상위 단위 `p`(분→시, 시→일, 일→주·월, 월→년. 주는 없음) 모두에 대해 `bucket_start < finalized_until[p]`
   - 재계산은 바로 위 단위만 하위 행을 읽으므로 직접 상위의 확정만 보면 된다. 각 Cutoff는 언제나 직접 상위의 확정 경계보다 앞이라 C5 기간대로 지워진다.
   - 상위 구간이 확정된 행만 지운다(C5 "지우기 전에 상위로 모여 있어야").

서버가 멈춰 있던 기간이 있어도, 확정 경계 뒤의 하위 행은 남아 있다. 그래서 기동 직후 첫 실행이 밀린 구간을 모두 계산하고 지운다(C6).

### OTLP 받기 (`POST /v1/metrics`)

| 요청 | 응답 |
|---|---|
| `Content-Type: application/x-protobuf` | 200, `application/x-protobuf`, 빈 `ExportMetricsServiceResponse` 직렬화 |
| `Content-Type: application/json` | 200, `application/json`, `{}` |
| 위 둘이지만 본문 해석 실패 | 400 ProblemDetails |
| 그 밖의 Content-Type | 415 ProblemDetails |

- `Content-Encoding: gzip`이면 풀어서 읽는다(Claude Code의 `OTEL_EXPORTER_OTLP_COMPRESSION` 대비).
- 대상이 아닌 지표와 누적 포인트는 저장하지 않고 200을 돌려준다. 이때 누적 포인트를 받았다는 경고 로그를 남긴다.

### 조회 API

`GET /api/usage/series`

| 인자 | 값 |
|---|---|
| `unit` | `minute`·`hour`·`day`·`week`·`month`·`year` (필수) |
| `from`, `to` | ISO 8601. 오프셋이 있으면 그것으로, 없으면 KST로 해석 (필수) |
| `user`, `model`, `tokenType` | 여러 번 줄 수 있는 필터(없으면 전체) |
| `groupBy` | `tokenType`·`model`·`user` (필수) |

처리:

- `from` 내림, `to` 올림(경계면 그대로)
- 응답 구간은 `[max(Floor(from), RetainedFrom(unit)), min(to, 현재 구간 끝))`
- 이 범위의 모든 구간을 0으로 채운 뒤 합계를 넣는다
- 잘못된 입력은 400 ProblemDetails (C7)

```json
{
  "unit": "hour",
  "from": "2026-11-20T00:00:00+09:00",
  "to": "2026-11-20T15:00:00+09:00",
  "groupBy": "tokenType",
  "series": ["cacheCreation", "cacheRead", "input", "output"],
  "buckets": [
    { "start": "2026-11-20T00:00:00+09:00", "tokens": { "input": 120, "output": 40 }, "totalTokens": 160, "costUsd": 0.012 }
  ],
  "totals": { "tokens": 160, "costUsd": 0.012 }
}
```

- `tokenType` 필터가 있으면 `costUsd`는 버킷과 합계 모두 `null`이다.
- `groupBy=tokenType`이면 `costUsd`는 구간 합계 한 값이다(D007). `model`·`user`로 나눠도 비용은 구간 합계 한 값이다.
- `tokens`는 값이 0인 계열을 생략할 수 있다. 계열 목록은 `series`에 있다.

`GET /api/usage/meta`

`units[].earliest`는 `RetainedFrom(unit, now)`이다(년은 `null`).

```json
{
  "now": "2026-11-20T14:37:12+09:00",
  "units": [ { "unit": "minute", "earliest": "2026-11-19T14:37:00+09:00" }, { "unit": "year", "earliest": null } ],
  "users": ["a@example.com"], "models": ["claude-opus-5-5"], "tokenTypes": ["input", "output", "cacheRead", "cacheCreation"]
}
```

### 화면

- 단위를 바꾸면 그 단위의 기본 기간을 쓴다: 분 최근 60분, 시 최근 24시간, 일 최근 14일, 주 최근 12주, 월 최근 12개월, 년 최근 5년.
- 기간 입력은 KST 날짜·시각이며 `meta.units[].earliest`보다 이르게 고를 수 없다(C8).
- 차트: x축은 구간 시작(KST)이고, 막대는 `groupBy` 계열별로 쌓는다. 보조 축 선은 `costUsd`이고 null이면 그리지 않는다.
- 요약: 기간 토큰 총합과 비용 총합(USD)을 보인다. 토큰 종류 필터가 있으면 "토큰 종류로 거르면 비용을 볼 수 없습니다" 안내를 띄운다.

### 패키지 (`Directory.Packages.props`, 구현 때 취약·Deprecated 재확인)

- 템플릿: `Biz.Bizadm.KMS` 1.0.6, `Biz.Bizadm.KMS.Credentials` 1.0.2, `Biz.Bizadm.Serilog.Enrichers.Caller` 1.0.0, `Biz.Bizadm.Serilog.Sinks.File.Extensions` 1.0.0, `Biz.Bizadm.System.Configuration.Extensions` 1.0.1, `CommandLineParser` 2.9.1, `Microsoft.AspNetCore.OpenApi` 10.0.15, `Microsoft.AspNetCore.TestHost` 10.0.15, `Microsoft.AspNetCore.SpaProxy` 10.0.15, `MSTest` 4.5.2, `Serilog.Extensions.Hosting` 10.0.0, `Serilog.Sinks.Console` 6.1.1, `YamlDotNet` 18.1.0
- 추가: `DuckDB.NET.Data.Full` 1.5.6.1, `Google.Protobuf` 3.36.2, `Grpc.Tools` 2.85.0, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.15, `Microsoft.Extensions.Hosting.Systemd` 10.0.15, `Microsoft.Extensions.TimeProvider.Testing` 10.11.1(테스트)

### README (C9)

README에 적을 내용:

- 빌드·publish
- 콘솔 실행: `--config`, `--log`
- Windows 서비스 등록: `sc.exe create ... binPath= "<exe> --config <절대 경로> --log <절대 경로>"`, 시작·중지·삭제. 서비스는 작업 폴더가 `System32`이므로 경로는 절대 경로로 쓴다.
- systemd 유닛 등록: `deploy/systemd` 예시, `Type=notify`, `systemctl enable --now`, 서비스 계정의 KEK 저장소 준비(`GCM_CREDENTIAL_STORE=gpg`, `GPG_TTY`, 암호 없는 gpg 키와 `.password-store/.gpg-id`) (D015)
- Claude Code 설정: `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_METRICS_EXPORTER=otlp`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`(또는 `http/json`), `OTEL_EXPORTER_OTLP_ENDPOINT=http://<서버>:<포트>`, 선택 `OTEL_METRIC_EXPORT_INTERVAL`. `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE`는 바꾸지 말 것(cumulative면 저장되지 않음). `settings.json`의 `env`에 넣는 예시
- 조회 API 요약
- opentelemetry-proto 파일의 출처·라이선스(Apache-2.0)

## 오류·위험

- DuckDB 파일은 한 프로세스만 쓰기로 열 수 있다. 같은 `DatabaseFilePath`로 두 번째 서버를 띄우면 기동 단계에서 실패한다(종료 코드 3). README에 적는다.
- 유지 작업 중 예외가 나면 로그만 남기고 다음 주기에 다시 한다. 트랜잭션이라 중간 상태가 남지 않는다. 받기 중 DB 예외는 500 ProblemDetails를 돌려주고, 내보내는 쪽이 다시 보낸다.
- OTLP JSON의 bytes 필드(exemplar의 trace/span id)는 OTLP 규약상 hex인데, proto3 JSON 매핑은 base64를 기대한다. exemplar가 들어 있으면 JSON 해석이 실패할 수 있으므로, 해석 전에 `exemplars`를 지운 뒤 읽는다(exemplar는 쓰지 않음).
- Claude Code가 `user.email` 등을 리소스 속성으로만 보내도 찾기 규칙이 처리한다. 실제 전송 형태는 C1 수동 확인에서 본다.
- 토큰 값이 `as_double`로 오면 반올림해 BIGINT로 저장한다. 소수점 토큰은 없으므로 손실이 없다.
- KST는 고정 +9로 계산한다(한국은 일광 절약 시간이 없음). OS 시간대 데이터에 의존하지 않는다.
- 서버 시계가 크게 틀리면 보관·확정 경계가 어긋난다. 이 경우는 운영 전제(NTP)로 두고 따로 처리하지 않는다.
- systemd 실제 검증은 Linux 환경이 있어야 한다(검증 전략 C10).
- Linux 서비스에는 데스크톱 키링(Secret Service)이 없어 OsKms 기본 저장소로는 기동에 실패한다(종료 코드 2). 유닛에 `GCM_CREDENTIAL_STORE=gpg`·`GPG_TTY`를 두고 서비스 계정에 gpg 키와 저장소를 준비하도록 README에 적는다(D015).

## 검증 전략

- C1:
  - `OtlpMetricsEndpointTests`(TestServer)로 다음을 확인한다. protobuf 요청은 200과 protobuf 본문, JSON 요청은 200과 `{}`, gzip 본문은 200, 깨진 본문은 400, `text/plain`은 415. 대상이 아닌 지표·누적 포인트는 저장되지 않는다.
  - 수동: 실제 Claude Code에 README 환경 변수를 설정해 서버로 보내고, `/api/usage/series`에 값이 나오는지 확인한다.
- C2: `DuckDbUsageStoreIngestTests`(FakeTimeProvider, 임시 DB 파일)
  - 같은 분·키 두 번 → 합산
  - 끝 시각 기준 분 구간
  - 속성 찾기 순서
  - 확정된 시 구간으로 온 늦은 값 → 분·시에 한 번씩
  - 분 보관이 지난 늦은 값 → 시 이상에만 한 번씩
  - 일이 지워지고 주·월만 남은 시각 → 주·월·년에 한 번씩
- C3: `DuckDbUsageStoreMaintenanceTests`. 여러 단위에 걸친 데이터를 넣고 유지 작업을 한 뒤, 하위 행이 모두 남은 끝난 상위 구간마다 (사용자·모델·토큰 종류) 합계와 비용이 하위 합계와 같은지 확인한다.
- C4: `BucketCalculatorTests`. UTC 14:59/15:00(KST 자정 경계), 일요일→월요일, 월말·연말, 윤년.
- C5:
  - `RetentionPolicyTests`: 11-20 기준 일 컷오프 10-20, 03-31 − 1개월 = 02-28, 윤년 02-29 − 1년, 분·시 고정 길이.
  - `DuckDbUsageStoreMaintenanceTests`: 시간을 앞으로 돌려 행이 지워지는지, 지운 뒤에도 상위 합계가 그대로인지 확인한다.
- C6:
  - `DuckDbUsageStoreMaintenanceTests`: 저장소를 닫고 같은 파일로 다시 열면 데이터가 남는지, 시간을 사흘 뒤로 옮기고 첫 유지 작업 한 번에 밀린 재계산·삭제가 끝나는지 확인한다.
  - `UsageMaintenanceServiceTests`: 시작 직후 한 번 실행되고 이후 1분 주기로 도는지 확인한다(FakeTimeProvider).
- C7: `UsageApiTests`(TestServer)
  - 경계 내림·올림
  - 경계에 맞는 끝은 그대로
  - 0 채우기
  - 미래 구간 제외
  - 보관 밖 구간 제외, 전체가 보관 밖이면 200과 빈 계열
  - 오프셋 해석
  - 400 다섯 경우
  - `tokenType` 필터 시 비용 null
  - `groupBy=tokenType` 비용 합계
  - 받은 뒤 유지 작업 한 번이면 모든 단위에 반영되는지(1분 주기이므로 2분 안)
- C8:
  - Vitest: 필터 변경 시 요청 인자, 비용 안내 표시, 기간 하한 제한, 차트 렌더링.
  - 수동: publish 결과를 실행해 내장 브라우저로 화면을 열고 단위·필터·나눠 보기를 바꿔 차트·총합·안내를 확인한다.
- C9: README에 C9 항목(실행, Windows 서비스, systemd, Claude Code 환경 변수, temporality 안내)이 모두 있는지 읽어 확인한다.
- C10:
  - Windows: publish한 exe를 `sc.exe create`로 등록하고 `sc start`/`sc stop`을 한다. 로그에 정상 시작·`Application is shutting down`이 남고, 서비스 상태가 STOPPED인지 확인한다. 관리자 권한이 필요하므로 사용자 승인 후 하고, 끝나면 서비스를 지운다.
  - 같은 exe를 콘솔로 실행하고 Ctrl+C로 종료 코드 0을 확인한다.
  - Linux systemd: 이 PC에 WSL(systemd 사용)이 있으면 그곳에서 `systemctl start/stop`으로 확인하고, 없으면 사용자가 지정한 Linux 서버에서 승인 후 확인한다. 끝나면 서비스·계정·파일을 지운다.
- 템플릿 공통(규칙 `dotnet/server-spa-template.md` 9번): `dotnet build` 경고 0, `dotnet test`, 프론트엔드 `tsc -b`·lint·test·build, publish 결과 `ClientApp/index.html`, 개발 모드 SpaProxy 리다이렉트.

## 원칙 점검

- 적용 규칙
  - `workflow/spec-driven.md`, `git/github-issue.md`(이슈 #1), `workflow/repository-content.md`(예시 YAML은 placeholder만, 개인 경로·이메일 없음)
  - `dotnet/project-creation.md`(net10.0, MSTest, slnx, src/tests, CPM, `{Solution}.{역할}`, Core/Infrastructure 분리)
  - `dotnet/server-api-template.md`·`dotnet/server-spa-template.md`와 스킬(Server.Core 구성 요소, `--config`/`--log`, YAML 검증, OsKms, Kestrel, Serilog, ProblemDetails, 종료 코드, SpaProxy, publish Target, 테스트 방식)
  - `dotnet/kms-config-secrets.md`(TLS 패스워드만 보안값)
  - `dotnet/nuget-package-selection.md`
  - `frontend/react-project-creation.md`(React 18.3, Vite 7.3, TS 5.9, Tailwind 4.1, shadcn Radix, Redux Toolkit, Vitest)
  - `git/commit-message.md`(커밋 때)
  - `dotnet/rid-specific-runtime.md`: Windows 서비스·systemd 패키지는 다른 OS에서 아무것도 하지 않는 OS 공통 패키지라 RID 분리가 필요 없다.
- 충돌
  - SDK: 규칙 `dotnet/project-creation.md` 2번(.NET 10 계열 SDK)을 따르지 않고 SDK 11.0.100으로 빌드한다. 사용자가 정했다(D014). 이유는 SDK 11이 net10.0 대상을 빌드하고 이 PC에 .NET 10 SDK가 없기 때문이다. TFM `net10.0`(1번)은 따른다. SDK 11의 분석기 기본값 차이로 경고가 생기면 경고 0 기준(템플릿 검증)에 따라 코드를 고친다.
  - spec C10이 `UseWindowsService`·`UseSystemd`를 적고 있으나, 같은 기능의 `AddWindowsService`·`AddSystemd`로 구현한다. 이유는 템플릿의 YAML `ContentRootPath`와 충돌하지 않기 위해서다(D013). C10의 요구(서비스 시작·정상 종료, 콘솔 실행 그대로)는 바뀌지 않는다.
  - API 서버(`{Solution}.Server`)와 그 테스트 프로젝트를 만들지 않는다. 스킬이 "API 서버를 함께 둘지는 요청에 따른다"고 정하며, DuckDB를 한 프로세스가 써야 하므로 따로 둘 이유가 없다.
  - 그 밖의 충돌 없음.
