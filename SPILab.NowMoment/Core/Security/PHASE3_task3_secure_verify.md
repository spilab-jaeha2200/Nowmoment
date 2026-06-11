# NowMoment v4.1 — Phase 3 작업 3: Secure-Verify Mode

개선 개발계획서 5장 "보안 확인 모드". 내부 개발자의 Core 접근에도
인증·인가·무결성 검증을 강제한다.

> 작업 1·2 가 Python Core 의 패키징·암호화(.spc)를 다뤘다면,
> 작업 3 은 그 .spc 를 **누가 열 수 있는가**를 C# Shell 쪽에서
> 통제한다.


## 신규 파일 (5개)

| 파일 | 역할 |
|---|---|
| `Core/Security/SecureVerifyContracts.cs` | 자격증명 모델 + SSO/2FA 확장 계약 |
| `Core/Security/CoreAccessMatrix.cs` | 권한 매트릭스 (역할 4종, PBKDF2 검증) |
| `Core/Security/LocalSecureVerifyBackend.cs` | `ISecureVerifyBackend` 실제 구현 |
| `Views/SecureVerifyDialog.xaml` | 인증 대화상자 (사번·비밀번호·2FA) |
| `Views/SecureVerifyDialog.xaml.cs` | 대화상자 코드비하인드 (`ICredentialPrompt`) |

기존 `SecureVerifyGate.cs` 는 변경하지 않는다 — 골격이 이미 `backend`
주입을 받도록 설계돼 있어, `LocalSecureVerifyBackend` 를 끼우기만
하면 된다.


## 인증 흐름 (계획서 5.1 의 2~6단계)

```
SecureVerifyGate.Unlock()
  └─ LocalSecureVerifyBackend.Verify()
      2. 자격증명 수집      ICredentialPrompt (SecureVerifyDialog)
      2. 인증              ISsoProvider 주입 시 SSO / 아니면 로컬 PBKDF2
      2. 2차 인증          RequireSecondFactor 시 ISecondFactorVerifier
      3. 인가              CoreAccessMatrix.RoleOf() — ShellOnly 거부
      4. 무결성 검증        ICoreBundleVerifier — .spc 서명·해시
      5. 키 발급           ICoreKeyVault.IssueForSession()
      6. 세션 수립          CoreSession (Granted, Role, SessionId)
```

각 단계 실패 시 `CoreSession.Denied(reason)` 으로 즉시 중단되고,
`SecureVerifyGate` 가 그 결과를 `core.denied` 감사 이벤트로 적재한다.


## 권한 매트릭스 (계획서 5.2)

역할 4종은 `core_access.json` 외부 파일로 관리한다 — 계획서 8장
권고대로 권한 부여는 조직이 결정하므로 코드에 하드코딩하지 않는다.

```json
{
  "schema": "1.0",
  "entries": [
    { "employeeId": "SPL-001", "displayName": "...", "role": "CoreOwner",
      "pbkdf2": { "salt": "<b64>", "hash": "<b64>", "iterations": 200000 } },
    { "employeeId": "SPL-042", "displayName": "...", "role": "CoreRunner",
      "pbkdf2": { ... } }
  ]
}
```

- 역할: `CoreOwner` / `CoreDeveloper` / `CoreRunner` / `ShellOnly`
- 비밀번호는 평문 저장 안 함 — PBKDF2-SHA256(200k 반복) + salt.
- `CoreAccessMatrix.CreateHash()` 가 신규 계정·비밀번호 변경용
  해시를 생성한다 (운영 도구).
- 파일이 없거나 손상되면 전원 `ShellOnly` 로 안전 폴백.


## SSO 확장점

`ISsoProvider` 는 계약만 제공한다. 조직이 사내 인증 시스템
(Azure AD / LDAP / OAuth 등) 어댑터를 작성해 `LocalSecureVerifyBackend`
생성자에 주입하면, **인증 단계만 SSO 로 교체**되고 인가·무결성·키
발급·감사 흐름은 그대로다 (계획서 8장 "백엔드만 교체").

미주입 시 로컬 PBKDF2 비밀번호 검증으로 동작 — SSO 확정 전에도
Core 인증이 완결된다.


## 배선 (App 시작 시 — 작업 4 에서 연결)

```csharp
var options = new SecureVerifyOptions {
    AccessMatrixPath = ".../core_access.json",
    SpcBundlePath    = ".../SPILab.Core.spc",
    VerifyKeyPath    = ".../sign_ed25519.pub",
    RequireSecondFactor = false,
};
var backend = new LocalSecureVerifyBackend(
    options,
    credentialPrompt: new SecureVerifyDialog(),
    sso: null,                  // 조직 SSO 어댑터 주입 지점
    bundleVerifier: ...,        // build_spc verify 호출 어댑터
    keyVault: ...);             // DPAPI 보호 키 저장소
var gate = new SecureVerifyGate(backend, auditSink);
```

`ICoreBundleVerifier` / `ICoreKeyVault` 의 구체 구현(외부 프로세스
`build_spc.py verify` 호출, DPAPI 키 저장소)은 작업 4 에서 audit
연동과 함께 배선한다.


## 검증 (정적 — Windows 빌드 미수행)

- 타입 정합성: `CoreRole` / `CoreSession` / `ISecureVerifyBackend`
  — 기존 `SecureVerifyGate.cs` 와 일치.
- XAML: `Theme.*` 키 8종 — App.xaml 정의와 일치. 공유 스타일
  `Dlg*` 4종 — `EditDialogStyles.xaml` 과 일치.
- 책임 분리: 대화상자는 UI 수집만, 인증·인가·키는 백엔드.

Windows 빌드 검증(`dotnet build -c Release`)은 작업 4 배선 완료
후 일괄 수행.


## 다음 단계

- Phase 3 작업 4: `audit_log` 확장 — `actor_role` / `core_session`
  컬럼 추가 + Core 이벤트 4종(`core.verify`/`unlock`/`build`/
  `denied`) 적재. 작업 3 의 백엔드를 App 에 실제 배선.
- Phase 3 작업 5: 산출물 워터마킹.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
