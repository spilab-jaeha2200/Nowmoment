# NowMoment v4.1 — Phase 3 작업 5: 워터마킹 + 어댑터 (Phase 3 완료)

개선 개발계획서 5.4 "워터마킹" + 작업 3·4 에서 미뤄둔
`ICoreBundleVerifier` / `ICoreKeyVault` 구체 구현.


## 1. 산출물 워터마킹 (계획서 5.4)

`kg_builder/build_pipeline/watermark.py` — KG JSON/TTL 산출물에
빌드 출처(세션·사용자·역할·도메인·시각)를 비가시 인코딩한다.

**계획서 5.4 핵심 제약 준수**: 워터마크는 노드/엣지 데이터를
변경하지 않는다.
- JSON → `meta._provenance` 필드에 삽입 (KG 의미론에 무해한 출처 메타)
- TTL  → 맨 위 `# spilab-provenance:` 주석 (Turtle 파서가 무시)

워터마크는 HMAC-SHA256 으로 서명된다 — SPILab 만 서명 키를 보유하므로
워터마크 자체를 위조할 수 없고, 유출 산출물의 출처를 신뢰성 있게
추적할 수 있다.

```bash
# 빌드 후 워터마크 삽입
python watermark.py stamp --json kg.json --ttl kg.ttl \
    --session ab12 --actor "홍길동" --role CoreDeveloper \
    --domain cmp --wm-key wm.key

# 유출 산출물 추적
python watermark.py extract --json suspect.json --wm-key wm.key
```

KG 빌드 시 자동 적용: `PhysicsKgBuilder.BuildAsync` 가 빌드 성공
직후 `StampWatermark` 로 `watermark.py stamp` 를 호출한다. 워터마킹
실패는 빌드 자체를 실패시키지 않는다(경고만).


## 2. 어댑터 구현 — 작업 3·4 미완분 마무리

### SpcBundleVerifier (`ICoreBundleVerifier`)

계획서 5.1 단계 4 "무결성 검증". `build_spc.py verify` 를 외부
프로세스로 호출하고 종료 코드로 통과/실패를 판정한다. 검증 로직
(AES-256-GCM 복호화 + Ed25519 서명 + 해시 대조)을 C# 에 재구현하지
않고 단일 진실 출처(build_spc.py)를 재사용한다.

### DpapiCoreKeyVault (`ICoreKeyVault`)

계획서 5.1 단계 5·7 / 4.3 "키는 인증 후 메모리 로드, 종료 시 폐기".

- 키 파일은 Windows DPAPI(`ProtectedData`, CurrentUser 범위)로
  암호화 — 같은 Windows 계정에서만 복호화. 파일을 복사해 가도
  다른 PC·계정에서는 못 푼다.
- `IssueForSession()` — DPAPI 복호화 → 메모리 로드 → 환경변수
  `SPILAB_CORE_KEY` 주입 (rules_loader.py 가 읽음).
- `RevokeSession()` — 메모리 키를 `ZeroMemory` 로 소거 + 환경변수
  제거.
- `ProtectKeyFile()` — 평문 키(bundle.key/core.key)를 DPAPI 보호
  형태로 변환하는 운영 도구.


## 3. App 배선 완성

`App.BuildSecureVerifyBackend` 가 두 어댑터를 실제 주입한다:

```csharp
var bundleVerifier = new SpcBundleVerifier(
    "python", ".../build_pipeline/build_spc.py", ".../bundle.key.dpapi");
var keyVault = new DpapiCoreKeyVault(".../core.key.dpapi");

new LocalSecureVerifyBackend(options, prompt,
    sso: null, secondFactor: null,
    bundleVerifier: bundleVerifier, keyVault: keyVault);
```

도구·키 파일이 없으면 각 어댑터가 검증 단계에서 "실패"를 반환하므로
Core 활성화가 안전하게 거부된다. SSO·2FA 어댑터만 여전히 `null` —
조직이 사내 인증 확정 시 주입한다.


## 4. 신규/수정 파일

| 파일 | 구분 | 내용 |
|---|---|---|
| `kg_builder/build_pipeline/watermark.py` | 신규 | 산출물 워터마킹 도구 |
| `Core/Provider/SpcBundleVerifier.cs` | 신규 | `.spc` 무결성 검증 어댑터 |
| `Core/Provider/DpapiCoreKeyVault.cs` | 신규 | DPAPI 키 저장소 어댑터 |
| `Core/Provider/PhysicsKgBuilder.cs` | 수정 | 빌드 성공 시 워터마킹 호출 |
| `App.xaml.cs` | 수정 | 두 어댑터 주입 배선 |


## 5. 검증

- 워터마킹 stamp/extract 라운드트립 — JSON·TTL 양쪽 OK
- 노드·엣지 불변 (계획서 5.4) — stamp 전후 해시 동일 확인
- 위변조 탐지 — 워터마크 변조 시 HMAC 서명 무효 판정
- 멱등성 — stamp 재실행 시 워터마크 중복 안 됨
- 타입 정합성 — `ICoreBundleVerifier`/`ICoreKeyVault` 구현 일치,
  `RunnerResult.JsonPath/TtlPath` 사용 확인

Windows 빌드 검증(`dotnet build -c Release`)은 Phase 4 회귀 검증
단계에서 일괄 수행. DPAPI 는 Windows 전용 API 이므로 `DpapiCoreKeyVault`
는 net8.0-windows 대상에서만 컴파일된다 (WPF 앱과 동일 제약).


## Phase 3 — 완료

계획서 6.3 의 Phase 3 작업 5건이 모두 완료되었다:

- [x] 작업 1: Cython 빌드 파이프라인 + RULES 암호화 분리
- [x] 작업 2: `.spc` 번들 빌드·검증 도구
- [x] 작업 3: Secure-Verify Mode (인증·인가·권한 매트릭스)
- [x] 작업 4: `audit_log` 확장 + Core 이벤트 적재 + App 배선
- [x] 작업 5: 산출물 워터마킹 + 무결성·키 어댑터

→ 다음은 Phase 4 (통합 검증 및 배포): 5개 도메인 회귀 테스트,
  배포 3종 빌드, Inno Setup 분기, 튜토리얼 갱신.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
