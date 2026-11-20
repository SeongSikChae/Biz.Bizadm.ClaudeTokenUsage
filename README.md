# Biz.Bizadm.ClaudeTokenUsage

Claude Code가 OpenTelemetry로 내보내는 토큰 사용량(`claude_code.token.usage`)과 추정 비용(`claude_code.cost.usage`)을 받아 DuckDB에 롤업으로 저장하고, 웹 화면에서 차트로 보는 서버다.

- OTLP/HTTP `POST /v1/metrics`(protobuf·JSON, gzip)를 직접 받는다. 별도 Collector가 필요 없다.
- 분·시·일·주·월·년 단위로 롤업한다. 시는 분을, 일은 시를, 주·월은 일을, 년은 월을 모은다. 경계는 KST(UTC+9), 주는 월요일 시작이다.
- 보관 기간: 분 24시간, 시 7일, 일 1개월, 주 1년, 월 5년, 년 영구. 보관 기간이 지난 행은 상위 단위로 모인 뒤 지운다.
- 사용자·모델·토큰 종류(input, output, cacheRead, cacheCreation)별로 집계하고, 비용은 사용자·모델별로 집계한다.
- 같은 서버가 React 화면을 제공한다. Windows 서비스·systemd 서비스로 상시 실행할 수 있다.

## 구성

| 경로 | 내용 |
|---|---|
| `src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa` | 서버(OTLP 받기, 조회 API, 화면 제공, 유지 작업) |
| `src/Biz.Bizadm.ClaudeTokenUsage.Server.Core` | 기동 공통 구성 요소(KMS, Kestrel, 로그, 종료 코드) |
| `src/Biz.Bizadm.ClaudeTokenUsage.Core` | 단위·구간·보관 기간 계산 |
| `src/Biz.Bizadm.ClaudeTokenUsage.Infrastructure` | OTLP 해석, DuckDB 저장·롤업·조회 |
| `src/Biz.Bizadm.ClaudeTokenUsage.Client` | React 화면(Vite) |
| `deploy/` | 설정 예시, systemd 유닛 예시 |

## 빌드·publish

필요: .NET SDK 11(`global.json`, 대상 프레임워크는 `net10.0`), ASP.NET Core 런타임 10, Node.js 22.

```powershell
dotnet build Biz.Bizadm.ClaudeTokenUsage.slnx
dotnet test Biz.Bizadm.ClaudeTokenUsage.slnx
```

publish하면 프론트엔드를 빌드해 출력 폴더의 `ClientApp`에 넣는다(`npm ci`, `npm run build`). 프론트엔드를 따로 빌드했으면 `-p:SkipClientAppBuild=true`를 붙인다.

```powershell
# Windows
dotnet publish src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa -c Release -r win-x64 --self-contained false -o <배포 폴더>
# Linux
dotnet publish src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa -c Release -r linux-x64 --self-contained false -o <배포 폴더>
```

## 설정

`deploy/config.example.yaml`을 복사해 고친다.

| 키 | 설명 |
|---|---|
| `ContentRootPath` | 배포 폴더 절대 경로. 아래 상대 경로의 기준 |
| `Port` | 받기·조회·화면을 함께 여는 포트(예: OTLP/HTTP 관례 포트 4318) |
| `WebRootPath` | 화면 빌드 결과 폴더(기본 `ClientApp`) |
| `DatabaseFilePath` | DuckDB 파일(기본 `data/token-usage.duckdb`). 폴더가 없으면 만든다 |
| `OsKms` | 보안값 암호화용 OS 자격 증명 저장소 설정. `CredentialService`는 절대 URI여야 한다 |
| `Tls` | `UseTls: true`면 `ServerCertificate`(PKCS#12)·`ServerCertificatePassword` 필요 |

- 최초 기동 시 `OsKms.Salt`를 설정 파일에 기록하므로, 서버를 실행하는 계정이 설정 파일에 쓸 수 있어야 한다. KEK는 그 계정의 OS 자격 증명 저장소에 만들어진다(Windows 서비스면 서비스 계정).
- DuckDB 파일은 한 프로세스만 열 수 있다. 같은 `DatabaseFilePath`로 서버를 두 번 띄우면 두 번째는 기동에 실패한다(종료 코드 3).
- 수신·조회 엔드포인트에 인증이 없으므로 내부망에서만 연다.

## 실행

### 콘솔

```powershell
<배포 폴더>\Biz.Bizadm.ClaudeTokenUsage.Server.Spa.exe --config <설정 파일> --log <로그 폴더>
```

종료 코드: 0 정상, 1 인자 오류, 2 설정·보안값 처리 오류, 3 호스트 생성·실행 오류. Ctrl+C로 정상 종료한다.

### Windows 서비스

관리자 PowerShell에서 등록한다. 서비스의 작업 폴더는 `System32`이므로 경로는 모두 절대 경로로 쓴다.

```powershell
sc.exe create BizBizadmClaudeTokenUsage binPath= "\"<배포 폴더>\Biz.Bizadm.ClaudeTokenUsage.Server.Spa.exe\" --config \"<설정 파일 절대 경로>\" --log \"<로그 폴더 절대 경로>\"" start= auto
sc.exe start BizBizadmClaudeTokenUsage
sc.exe query BizBizadmClaudeTokenUsage
sc.exe stop BizBizadmClaudeTokenUsage
sc.exe delete BizBizadmClaudeTokenUsage
```

서버는 `AddWindowsService()`로 서비스 관리자의 시작·중지와 연동한다. 콘솔로 실행하면 이 연동은 아무것도 하지 않는다.

### systemd

`deploy/systemd/biz-bizadm-claudetokenusage.service`의 경로·계정을 고쳐 `/etc/systemd/system/`에 둔다.

Linux의 OsKms KEK 기본 저장소는 데스크톱 키링(Secret Service)인데, systemd 서비스에는 키링이 없어 그대로면 기동에 실패한다(종료 코드 2). 유닛 예시처럼 `GCM_CREDENTIAL_STORE=gpg`와 `GPG_TTY=/dev/null`을 두고, 등록 전에 서비스 계정에 암호 없는 gpg 키와 저장소를 한 번 만든다(`gnupg2` 필요, `pass`는 없어도 된다).

```bash
# 서비스 계정(예: claudetokenusage)과 홈 폴더
sudo useradd --system --create-home --home-dir /var/lib/claudetokenusage --shell /sbin/nologin claudetokenusage
# 암호 없는 gpg 키와 pass 형식 저장소(.gpg-id)
sudo -u claudetokenusage -H gpg --batch --passphrase '' --quick-gen-key claudetokenusage@localhost default default never
fpr=$(sudo -u claudetokenusage -H gpg --batch --with-colons --list-secret-keys claudetokenusage@localhost | awk -F: '/^fpr/{print $10; exit}')
sudo -u claudetokenusage -H mkdir -p /var/lib/claudetokenusage/.password-store
echo "$fpr" | sudo -u claudetokenusage -H tee /var/lib/claudetokenusage/.password-store/.gpg-id
# 배포 폴더(설정 파일에 Salt를 기록하므로 쓰기 권한 필요)·로그 폴더
sudo chown -R claudetokenusage:claudetokenusage /opt/biz-bizadm-claudetokenusage /var/log/biz-bizadm-claudetokenusage
sudo restorecon -R /opt/biz-bizadm-claudetokenusage /var/log/biz-bizadm-claudetokenusage   # SELinux를 쓰는 배포판
```

KEK는 서비스 계정의 `~/.password-store/Biz.Bizadm.KMS/...` 아래에 gpg로 암호화되어 저장된다. 이 홈 폴더(gpg 키 포함)를 지우면 보안값을 복호화할 수 없다.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now biz-bizadm-claudetokenusage
systemctl status biz-bizadm-claudetokenusage
sudo systemctl stop biz-bizadm-claudetokenusage
```

서버는 `AddSystemd()`로 `Type=notify` 시작 알림과 journald 로그 형식을 쓴다.

## Claude Code 설정

Claude Code가 이 서버로 지표를 보내게 한다. 환경 변수로 주거나 `~/.claude/settings.json`(또는 관리 설정)의 `env`에 넣는다.

```json
{
  "env": {
    "CLAUDE_CODE_ENABLE_TELEMETRY": "1",
    "OTEL_METRICS_EXPORTER": "otlp",
    "OTEL_EXPORTER_OTLP_PROTOCOL": "http/protobuf",
    "OTEL_EXPORTER_OTLP_ENDPOINT": "http://<서버 주소>:4318"
  }
}
```

- `OTEL_EXPORTER_OTLP_PROTOCOL`은 `http/protobuf` 또는 `http/json`이다. gRPC는 받지 않는다.
- `OTEL_EXPORTER_OTLP_ENDPOINT`에는 경로 없이 서버 주소만 쓴다(내보내는 쪽이 `/v1/metrics`를 붙인다). 지표만 따로 지정하려면 `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://<서버 주소>:4318/v1/metrics`를 쓴다.
- 내보내기 주기는 `OTEL_METRIC_EXPORT_INTERVAL`(밀리초, 기본 60000)로 바꿀 수 있다.
- `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE`는 바꾸지 않는다(기본 `delta`). `cumulative`로 바꾸면 서버가 그 포인트를 저장하지 않는다(경고 로그만 남김).
- 사용자는 `user.email` → `user.account_uuid` → `user.id` 순으로 찾은 값으로 구분한다.

## 조회 API

| 요청 | 설명 |
|---|---|
| `GET /api/usage/series?unit=&from=&to=&groupBy=[&user=][&model=][&tokenType=]` | 구간별 토큰 합계(계열별)와 비용 합계 |
| `GET /api/usage/meta` | 단위별 보관 하한(`earliest`), 사용자·모델·토큰 종류 목록 |
| `GET /health` | 상태 확인 |

- `unit`: `minute`·`hour`·`day`·`week`·`month`·`year`. `groupBy`: `tokenType`·`model`·`user`. `user`·`model`·`tokenType`은 여러 번 줄 수 있다.
- `from`·`to`는 ISO 8601이다. 오프셋이 있으면 그것으로, 없으면 KST로 읽는다. 기간은 [시작, 끝)이며 시작은 구간 시작으로 내리고 끝은 구간 경계로 올린다(경계면 그대로).
- 보관 기간 안의 빈 구간은 0으로 채우고, 보관 기간이 지난 구간과 현재 구간보다 뒤인 구간은 넣지 않는다. 받은 값은 1분마다 도는 유지 작업으로 모든 단위에 반영된다(2분 안).
- `tokenType` 필터가 있으면 `costUsd`는 null이다(비용 지표에는 토큰 종류가 없다).
- 잘못된 입력(정해지지 않은 `unit`·`groupBy`, 해석할 수 없는 시각, 시작 ≥ 끝)은 400이다.

## 개발

```powershell
dotnet run --project src/Biz.Bizadm.ClaudeTokenUsage.Server.Spa --launch-profile http -- --config <설정 파일> --log <로그 폴더>
```

개발 모드에서는 SpaProxy가 `npm run dev`로 Vite(5173)를 띄운다. 화면은 `http://localhost:5173`으로 열고, Vite가 `/api`를 서버(`SERVER_URL`, 기본 `http://localhost:5000`)로 넘긴다. 서버 포트가 다르면 `SERVER_URL`을 맞춘다.

프론트엔드 검사(`src/Biz.Bizadm.ClaudeTokenUsage.Client`):

```powershell
npx tsc -b
npm run lint
npm test
npm run build
```

## 서드파티

`src/Biz.Bizadm.ClaudeTokenUsage.Infrastructure/Protos/opentelemetry/` 의 `.proto` 파일은 [opentelemetry-proto](https://github.com/open-telemetry/opentelemetry-proto) v1.11.1에서 가져왔으며 Apache License 2.0을 따른다.
