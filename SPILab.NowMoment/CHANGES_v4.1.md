# NowMoment v4.1 — Core 분리 변경 요약

본 패키지는 NowMoment v4.0 소스에 **개선 개발계획서의 Phase 1 + Phase 2**
(인터페이스 추출 + Provider 로더)를 적용한 것입니다.

핵심 효과: **SPILab Core(kg_builder)가 없어도 Shell이 정상 빌드·실행**되며,
외부 배포본에서 물리 규칙 IP의 평문 노출이 제거됩니다.


## 1. 신규 파일 (9개) — `Core/` 폴더

| 파일 | 역할 |
|---|---|
| `Core/Contracts/IKgBuilder.cs` | KG 빌더 계약 인터페이스 (IP 없음, 공개 가능) |
| `Core/Contracts/CoreContractDtos.cs` | `BuildRequest` / `BuildResult` DTO |
| `Core/Contracts/IAssetClassifier.cs` | 자산 자동분류 계약 인터페이스 |
| `Core/Security/SecureVerifyGate.cs` | 보안 확인 모드 게이트 (인증·인가·감사) |
| `Core/Provider/CoreProviderLoader.cs` | Core 페이로드 탐지 + Provider 구성 |
| `Core/Provider/CoreServices.cs` | Provider 전역 접근점 (경량 서비스 로케이터) |
| `Core/Provider/PhysicsKgBuilder.cs` | `IKgBuilder` 실제 구현 (KgBuilderRunner 래핑) |
| `Core/Provider/NullKgBuilder.cs` | Core 미탑재 시 폴백 (기능 비활성) |
| `Core/Provider/PassthroughClassifier.cs` | 분류기 폴백 (미분류 후보만 생성) |

신규 파일 1개 추가: `Services/AuditService.Core.cs` — Core 접근 감사 로그.


## 2. 수정된 기존 파일 (6개)

| 파일 | 변경 내용 |
|---|---|
| `App.xaml.cs` | 시작 시 `CoreServices.Initialize()` 배선 (감사 sink 연결) |
| `Services/AuditService.cs` | `class` → `partial class` (1줄, Core 메서드 분리용) |
| `ViewModels/KgViewModel.Builder.cs` | KG 빌드를 `IKgBuilder.BuildAsync` 경유로 교체. `_runner` 직접호출 3곳 제거 |
| `ViewModels/FolderImportViewModel.cs` | 분류기를 `IAssetClassifier`(CoreServices 경유)로 교체 |
| `SPILab.NowMoment.csproj` | 버전 4.0 → 4.1, kg_builder 패키징 정책 주석 |
| `Services/KgBuilderRunner.cs` 외 | **변경 없음** — static 경로 헬퍼는 그대로 사용 (IP 아님) |


## 3. 동작 흐름

```
App 시작
 └─ CoreServices.Initialize(auditSink)
     └─ CoreProviderLoader.Load()
         ├─ kg_builder/build_kg_*.py 존재?
         │   ├─ 있음 → SecureVerifyGate.Unlock()
         │   │         ├─ 인가 성공 → PhysicsKgBuilder (실제 빌드)
         │   │         └─ 인가 실패 → NullKgBuilder (비활성)
         │   └─ 없음 → NullKgBuilder + PassthroughClassifier (외부 배포본)
```

- **KG 빌드 버튼** → `CoreServices.KgBuilder.BuildAsync()` → Core 활성 시 빌드,
  비활성 시 "기능 비활성" 안내 후 종료 (앱은 정상 유지).
- **폴더 임포트** → `CoreServices.Classifier.Classify()` → Core 활성 시 자동분류,
  비활성 시 전부 '미분류' 후보 (사용자가 직접 지정).


## 4. Secure-Verify Mode (현재 동작)

`Core/Security/SecureVerifyGate.cs` — 기본 정책은 **"기본 잠금"**:

- 환경변수 `SPILAB_CORE_DEV=1` 인 개발 PC → `CoreDeveloper` 권한으로 통과
- 그 외 → 거부 (Core 비활성)

→ 외부 배포본·일반 사용자에서는 Core가 절대 열리지 않습니다.

★ 실제 사내 SSO·2차 인증·`.spc` 암호화 번들은 Phase 3 작업입니다.
  `ISecureVerifyBackend` 구현체를 `CoreProviderLoader.Load(backend:)` 에
  주입하면 게이트 흐름은 그대로 두고 백엔드만 교체됩니다.


## 5. 외부 배포본 만드는 법 (수동)

현 단계에서는 패키징 스크립트 분기를 아직 추가하지 않았습니다.
외부 배포본은 다음과 같이 만듭니다:

1. 정상 빌드: `dotnet build -c Release`
2. publish 후 **`kg_builder/` 폴더를 산출물에서 삭제**
3. 인스톨러 패키징

→ `CoreProviderLoader` 가 `kg_builder/` 부재를 감지해 자동으로 Core를
  비활성화하므로, 코드 분기 없이 폴더 제거만으로 안전합니다.

(권장: Phase 4 에서 `build-installer-EXT.bat` + Core 검출 게이트 추가 —
 외부본에 kg_builder 가 실수로 포함되면 빌드 실패 처리. 개선계획서 8장 권고)


## 6. 빌드 검증 필수 안내

본 수정은 Linux 환경에서 정적 검토만 거쳤습니다 (WPF `net8.0-windows`는
Linux 빌드 불가). **Windows에서 반드시 `dotnet build -c Release` 로
컴파일 확인** 후 사용하십시오.

검토 중 발견·수정한 실제 버그:
- `ScannedItem` 네임스페이스 오인 (`Models` → `Services.Import`)
- `ImportCandidate` 필드명 불일치 (`AssetType`/`FilePath` → `Kind`/`SourcePath`)


## 7. 다음 단계 (미적용)

- Phase 3: Cython 컴파일, `.spc` AES-256-GCM 번들, 코드서명, SSO 연동
- Phase 4: 패키징 스크립트 3종 분기 + Core 검출 게이트, 회귀 테스트
- `AssetClassifier` 휴리스틱 본체의 SPILab.Core 완전 이전
  (현재는 `AssetClassifierAdapter` 로 경계만 세워둔 상태)

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
