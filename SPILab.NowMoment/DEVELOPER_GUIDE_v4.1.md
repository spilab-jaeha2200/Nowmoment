# NowMoment v4.1 — 개발자 가이드 (Core 분리 아키텍처)

개선 개발계획서 6.4 작업 ④ "개발 튜토리얼 갱신 — Core 분리
아키텍처·Secure-Verify 절차 반영". 본 문서는 NowMoment 개발
튜토리얼 PPTX 의 v4.1 갱신 내용을 정리한 것이다.


## 1. v4.0 → v4.1 무엇이 바뀌었나

v4.1 의 핵심은 **SPILab Core IP 보호**다. 기능은 v4.0 과 100%
동일하되, KG 빌더·물리 규칙·분류 휴리스틱이 보호 경계 안으로
이동했다.

| 구분 | v4.0 | v4.1 |
|---|---|---|
| KG 빌더 | `kg_builder/*.py` 평문 | Cython `.pyd` + RULES 암호화 `.spc` |
| 물리 규칙(167룰) | `.py` 내 평문 dict | AES-256-GCM 암호화 `.enc` |
| 분류 휴리스틱 | `AssetClassifier.cs` 평문 | `kg_builder/classifier/` Core 페이로드 |
| Core 접근 | 무제한 | Secure-Verify 인증·인가·감사 |
| 배포본 | 단일 | 3종 (내부 FD/SC, 외부 EXT-SC) |


## 2. 3계층 아키텍처

```
NowMoment.Shell  ──참조──▶  Core.Contracts  ◀──구현──  SPILab.Core
(Views/VM/DB/                (인터페이스만,            (KG빌더·167룰·
 백업/내보내기)               IP 없음)                  분류휴리스틱)
                                                       = 보호 대상
```

- **Shell** 은 `IKgBuilder`/`IAssetClassifier` 인터페이스만 참조한다.
  Core 구현체를 컴파일 타임에 알지 못한다.
- **Core 페이로드**(`kg_builder/`)가 있으면 실제 빌더가, 없으면
  `NullKgBuilder`/`AssetClassifierFallback` 이 등록된다.
- 외부 배포본은 `kg_builder/` 를 물리적으로 제외 → KG 빌드·자동
  분류만 "비활성" 표시, 나머지는 정상.


## 3. Secure-Verify 절차 (개발자용)

내부 개발자가 Core 기능(KG 빌드)을 처음 호출하면:

1. **인증 대화상자** — 사번 + 비밀번호 입력 (정책상 2차 인증).
2. **인가** — `core_access.json` 의 권한 매트릭스로 역할 조회.
   Shell-Only 면 거부.
3. **무결성 검증** — `SPILab.Core.spc` 번들 서명·해시 확인.
4. **키 발급** — DPAPI 보호 키를 메모리에 로드.
5. **세션 수립** — 앱 실행당 1회. 이후 재인증 불필요.

권한 등급 4종:

| 역할 | Core 접근 |
|---|---|
| Core-Owner | 번들 생성·서명·룰 편집·키 관리 |
| Core-Developer | Core 코드 열람·수정·로컬 빌드 |
| Core-Runner | KG 빌드 실행만 (소스 열람 불가) |
| Shell-Only | Core 접근 불가 (외부·일반 사용자) |

개발 PC 빠른 진입: 환경변수 `SPILAB_CORE_DEV=1` → Core-Developer
권한으로 통과 (백엔드 미구성 시 폴백 정책).


## 4. Core 빌드 파이프라인 (Core-Owner 전용)

`kg_builder/build_pipeline/` — 빌더를 보호된 형태로 변환:

```bash
# ① RULES 분리·암호화
python extract_rules.py --out-dir build_out --key-file core.key

# ② Cython 컴파일 (stripped.py → .pyd/.so)
python build_cython.py --src-dir build_out --out-dir build_out/native

# ③ .spc 번들 봉인
python build_spc.py pack --native-dir build_out/native \
    --rules-dir build_out/rules --loader rules_loader.py \
    --out SPILab.Core.spc --bundle-key bundle.key \
    --sign-key sign_ed25519.key

# 회귀 검증 — v4.0 산출물과 동일성 확인
python regression_test.py --orig-dir .. \
    --protected-dir build_out --key-file core.key
```

키 파일(`core.key`/`bundle.key`/`sign_ed25519.key`)은 `.spc` 와
분리하여 키 저장소에 보관한다. 배포 PC 에는 DPAPI 로 보호해 둔다.


## 5. 배포 산출물 3종

| 배포본 | 빌드 스크립트 | Core | .NET | 대상 |
|---|---|---|---|---|
| 내부 FD | `build-installer.bat` | 포함 | 별도 설치 | SPILab 내부 |
| 내부 SC | `build-installer-SC.bat` | 포함 | 내장 | SPILab 내부 |
| 외부 EXT | `build-installer-EXT-SC.bat` | **제외** | 내장 | 외부 고객 |

외부 배포본은 빌드 시 `kg_builder/` 를 삭제하고 Core 누출 게이트로
재확인한다 — `build_kg_*.py` 가 산출물에 남으면 빌드 중단.
산출물: `NowMoment-v4.1.0-Setup.exe`

상세는 `installer/README-v4.1-EDITIONS.md` 참조.


## 6. 데이터·호환성

- `audit_log` 에 `actor_role`/`core_session` 컬럼 2개 추가
  (멱등 ALTER) — 기존 `nowmoment.db` 그대로 사용.
- KG JSON/TTL 포맷·`@vocab` 네임스페이스 불변 — 기존 KG 재빌드 불필요.
- Core 보유 내부본은 v4.0 과 기능 100% 동일.


## 7. 보호의 한계 (개발자가 알아야 할 것)

본 설계는 "우연한 노출 차단 + 추출 비용 상향 + 추적 가능성"이
목표다. 완전한 비가역 보호가 아니다 — 충분한 동기·기술을 가진
내부 인가자는 메모리 덤프 등으로 복호화된 Core 를 추출할 수 있다.

따라서 기술적 통제는 계약적 통제(NDA·취업규칙·접근 권한 최소화)와
병행해야 한다. 워터마킹·감사 로그는 잔존 리스크에 대한 사후 억제
수단이다.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
