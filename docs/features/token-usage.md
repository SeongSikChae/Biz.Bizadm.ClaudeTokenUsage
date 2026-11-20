# token-usage

파일: global.json, Directory.Build.props, Directory.Packages.props, Biz.Bizadm.ClaudeTokenUsage.slnx, .gitignore, README.md, deploy/config.example.yaml, deploy/systemd/biz-bizadm-claudetokenusage.service, src/Biz.Bizadm.ClaudeTokenUsage.Core/, src/Biz.Bizadm.ClaudeTokenUsage.Infrastructure/, src/Biz.Bizadm.ClaudeTokenUsage.Server.Core/, src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa/, src/Biz.Bizadm.ClaudeTokenUsage.Client/, tests/Biz.Bizadm.ClaudeTokenUsage.Core.Tests/, tests/Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests/, tests/Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests/, tests/Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests/

Claude Code의 OpenTelemetry 지표(`claude_code.token.usage`, `claude_code.cost.usage`)를 OTLP/HTTP로 받아 DuckDB에 분·시·일·주·월·년 롤업으로 저장하고, 단위별 보관 기간에 따라 지우며, React 화면에서 차트로 조회한다.

## 용어

- 단위: 분(minute), 시(hour), 일(day), 주(week), 월(month), 년(year)
- 구간: 한 단위의 한 칸. 시작을 포함하고 끝을 포함하지 않는다. 시각은 KST(UTC+9).
- 끝난 구간: 구간 끝 시각이 지난 구간
- 속성 찾기: 데이터 포인트 속성에서 먼저 찾고, 없으면 리소스 속성에서 찾는다.
- 사용자: `user.email` → `user.account_uuid` → `user.id` 순으로 처음 찾은 값, 모두 없으면 `unknown`
- 모델: `model`, 없으면 `unknown`
- 토큰 종류: `claude_code.token.usage`의 `type`(input, output, cacheRead, cacheCreation), 없으면 `unknown`

## 요구(완료 조건)

- R1. 서버가 OTLP/HTTP `POST /v1/metrics` 요청을 `application/x-protobuf`와 `application/json` 두 형식으로 받는다. 요청 안의 `claude_code.token.usage`와 `claude_code.cost.usage` 델타 데이터 포인트만 저장하고, 다른 지표와 누적 시간성 데이터 포인트는 저장하지 않는다. 성공하면 200과 요청과 같은 형식의 `ExportMetricsServiceResponse` 본문을, 본문을 해석할 수 없으면 400을, 지원하지 않는 Content-Type이면 415를 돌려준다.
- R2. 받은 데이터 포인트는 그 포인트의 끝 시각(OTLP `time_unix_nano`, KST)이 속한 분 단위 구간에 더해진다. 토큰은 (구간, 사용자, 모델, 토큰 종류)별, 비용은 (구간, 사용자, 모델)별 합계로 저장된다. 같은 구간·키로 여러 번 받으면 값이 합산된다. 늦게 온 포인트도 버리지 않는다: 늦게 온 값은 그 시각을 포함하는 구간이 아직 보관 기간 안인 모든 단위(분·시·일·주·월·년)의 합계에 정확히 한 번씩 반영된다. 이미 모은 상위 구간에는 그 값을 더하며, 남은 하위 행으로 다시 합산하지 않는다.
- R3. 시 단위는 분 단위를, 일 단위는 시 단위를, 주 단위와 월 단위는 일 단위를, 년 단위는 월 단위를 모아 만든다. 그 구간에 들어가는 하위 단위 행이 모두 보관 중인 끝난 상위 구간에서, 롤업을 처리한 뒤 상위 단위의 합계는 그 구간의 하위 행 합계와 같다(사용자·모델·토큰 종류별, 비용 포함).
- R4. 단위 경계는 KST로 자른다. 일은 00:00, 주는 월요일 00:00, 월은 1일 00:00, 년은 1월 1일 00:00에 시작한다.
- R5. 보관 기간이 지난 행은 지운다: 분 24시간, 시 7일, 일 1개월, 주 1년, 월 5년, 년 영구. 구간 시작이 "지금이 속한 그 단위 구간의 시작 − 보관 기간"보다 이른 행을 지운다. 분·시는 고정 길이(24시간, 7일)로, 일·주·월은 달력 기준(1개월, 1년, 5년)으로 뺀다. 뺀 달에 그 날짜가 없으면 그 달의 말일로 맞춘다(예: 03-31 − 1개월 = 02-28). 지우기 전에 그 행이 상위 단위로 모여 있어야 하며, 지워도 상위 단위의 합계는 바뀌지 않는다.
- R6. DuckDB 파일에 저장하므로 서버를 다시 시작해도 데이터가 남는다. 서버가 시작하면 곧바로 멈춰 있던 동안 밀린 롤업과 삭제를 처리하고, 그 뒤에는 1분마다 처리한다.
- R7. 조회 API는 단위, 기간(시작·끝), 사용자·모델·토큰 종류 필터, 나눠 볼 기준(토큰 종류·모델·사용자 가운데 하나)을 받아 구간별 토큰 합계와 비용 합계를 시계열로 돌려준다.
  - 기간은 KST로 해석하고 [시작, 끝) 반열림으로 본다. 시작은 그 단위 구간 시작으로 내린다. 끝이 구간 경계가 아니면 그 끝을 포함하는 구간의 끝으로 올리고, 경계이면 그대로 둔다.
  - 아직 끝나지 않은 현재 구간도 그때까지 받은 값으로 포함한다. 받은 값은 받은 뒤 2분 안에 모든 단위의 조회 결과(현재 구간 포함)에 반영된다.
  - 보관 기간 안에서 사용량이 없는 구간은 0으로 채워 넣고, 보관 기간이 지난 구간은 응답에 넣지 않는다. 현재 구간보다 뒤인 구간도 응답에 넣지 않는다(끝은 미래로 골라도 된다).
  - 단위나 나눠 볼 기준이 정해진 값이 아니거나, 시각을 해석할 수 없거나, 시작이 끝보다 늦거나 같으면 400을 돌려준다. 시각에 시간대 오프셋이 있으면 그 오프셋으로 해석해 KST로 바꾸고, 없으면 KST로 본다. 기간 전체가 보관 기간 밖이면 200과 빈 계열을 돌려준다.
  - 토큰 종류 필터를 걸면 비용을 돌려주지 않는다(null). 나눠 볼 기준이 토큰 종류이면 비용은 구간 합계 한 계열로 돌려준다.
- R8. React 화면에서 단위와 기간을 고르고, 사용자·모델·토큰 종류로 거르고, 나눠 볼 기준을 골라 토큰 사용량을 쌓은 막대 차트로 본다. 구간별 비용(USD)과 고른 기간의 토큰·비용 총합을 함께 표시한다. 토큰 종류로 거르면 비용을 볼 수 없다는 안내를 표시한다. 기간은 단위마다 보관 기간 안에서만 고를 수 있다. 화면은 같은 ASP.NET Core 서버가 제공한다.
- R9. README에 서버 실행 방법(콘솔 실행, Windows 서비스 등록, systemd 유닛 등록)과, Claude Code가 이 서버로 지표를 보내게 하는 환경 변수 설정(`CLAUDE_CODE_ENABLE_TELEMETRY`, `OTEL_METRICS_EXPORTER`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_ENDPOINT` 등)을 적는다.
- R10. 서버는 Windows에서는 Windows 서비스로, Linux에서는 systemd 서비스로 실행할 수 있다(`UseWindowsService`, `UseSystemd`와 같은 기능을 서비스 등록 형태 `AddWindowsService`, `AddSystemd`로 쓴다). 서비스로 실행할 때 서비스 관리자의 시작·중지 명령에 따라 시작하고 정상 종료하며, 콘솔에서 직접 실행해도 같은 프로그램이 그대로 동작한다.

## 이력
| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | token-usage-rollup | R1~R10 추가 | docs/specs/token-usage-rollup/ |
