# NowMoment v4.1 — Phase 3 작업 4: audit_log 확장 + App 배선

개선 개발계획서 5.3 "감사 로그 확장" + 작업 3 백엔드의 App 연결.


## 1. audit_log 스키마 확장 (계획서 5.3)

`audit_log` 테이블에 Core 접근 이벤트용 전용 컬럼 2개를 추가했다.

```sql
ALTER TABLE audit_log ADD COLUMN actor_role   TEXT NOT NULL DEFAULT '';
ALTER TABLE audit_log ADD COLUMN core_session TEXT NOT NULL DEFAULT '';
```

- `actor_role`   — CoreOwner / CoreDeveloper / CoreRunner / ShellOnly
- `core_session` — Secure-Verify 세션 ID

마이그레이션은 `DatabaseService` 의 v4 블록에 멱등 ALTER 로 추가했다
(`ColumnExists` 가드). 계획서 7.1 호환성 보장 그대로 — 기존
`nowmoment.db` 를 그대로 쓰며, 컬럼 2개만 추가된다. 자산 CRUD 행은
두 컬럼이 빈 문자열로 유지된다.


## 2. 수정 파일

| 파일 | 변경 |
|---|---|
| `Services/DatabaseService.cs` | audit_log ALTER 2컬럼, `WriteAudit` 에 `actorRole`/`coreSession` 선택 파라미터, `GetAuditLogs` SELECT 확장 |
| `Models/V4Models.cs` | `AuditLog` 에 `ActorRole`/`CoreSession` 속성 + `IsCoreEvent` |
| `Services/AuditService.Core.cs` | `LogCore` 가 role·session 을 payload JSON 이 아닌 전용 컬럼에 기록 |
| `App.xaml.cs` | `LocalSecureVerifyBackend` 구성·배선 (`BuildSecureVerifyBackend`) |

`WriteAudit` 의 새 파라미터는 선택(기본값 `""`)이므로 기존 자산
CRUD 호출 4곳(`LogCreate`/`LogDelete`/`LogUpdate`/`LogAction`)은
변경 없이 그대로 동작한다.


## 3. Core 이벤트 (계획서 5.3)

`audit_log.action` 에 적재되는 Core 이벤트:

| action | 시점 |
|---|---|
| `core.verify` | Secure-Verify 인증 성공 |
| `core.denied` | 인가 거부 (권한 없음 등) |
| `core.unlock` | 번들 복호화 성공 |
| `core.build`  | KG 빌더 실행 |
| `core.lock`   | 세션 종료 |

`SecureVerifyGate` 가 인증 시도마다 `auditSink` 콜백을 호출하고,
그 콜백은 `AuditService.CreateCoreAuditSink()` 가 만든 것으로
`LogCore` → `WriteAudit(actor_role, core_session)` 로 이어진다.


## 4. App 배선 (작업 3 백엔드 연결)

```
App.OnAppStartup
  ├─ AuditService(db)
  ├─ BuildSecureVerifyBackend()
  │    └─ LocalSecureVerifyBackend(
  │         options(core_access.json, .spc, .pub),
  │         prompt = SecureVerifyDialog,
  │         sso/secondFactor/bundleVerifier/keyVault = null)
  └─ CoreServices.Initialize(auditSink, backend)
       └─ CoreProviderLoader.Load → SecureVerifyGate(backend, auditSink)
```

`CoreServices.Initialize` 와 `CoreProviderLoader.Load` 는 이미
`backend` 파라미터를 받도록 작업 3 이전부터 설계돼 있어, App 에서
백엔드를 만들어 넘기기만 하면 배선이 완성된다.

구성 파일 위치: `%APPDATA%\SPILab\NowMoment\`
- `core_access.json`   권한 매트릭스 (없으면 전원 Shell-Only 거부)
- `SPILab.Core.spc`    Core 번들 (작업 2 산출물)
- `sign_ed25519.pub`   번들 서명 검증 공개키

SSO·2FA·번들검증·키저장소 어댑터는 `null` 로 두었다 — 로컬 PBKDF2
인증으로 즉시 동작하며, 조직이 확정하면 `BuildSecureVerifyBackend`
의 해당 인자에 주입한다.


## 5. 검증

- audit_log 마이그레이션: ALTER 2컬럼 + 멱등 재실행 — 중복 없음 OK
- 기존 행 보존: v4.0 행이 빈 문자열 컬럼으로 정상 유지
- Core 이벤트 적재: `core.verify`/`build`/`denied` — role·session
  전용 컬럼 기록 OK
- C# 정합성: `WriteAudit` 기존 호출 4곳 무영향(선택 파라미터),
  App 배선 타입 참조 일관

Windows 빌드 검증(`dotnet build -c Release`)은 작업 5 완료 후
일괄 수행.


## 다음 단계

- Phase 3 작업 5: 산출물 워터마킹 — KG JSON/TTL 에 세션 ID·사용자·
  시각을 비가시 인코딩 (계획서 5.4).
- `ICoreBundleVerifier`(build_spc verify 호출)·`ICoreKeyVault`
  (DPAPI 키 저장소) 구체 구현도 작업 5 에서 마무리.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
