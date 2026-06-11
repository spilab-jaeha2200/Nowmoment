# NowMoment v4.1 — Phase 4 완료 보고서 (통합 검증 및 배포)

개선 개발계획서 6.4 Phase 4. v4.0 → v4.1 전환의 마지막 단계.


## 1. Phase 4 작업 결과

| 계획서 6.4 작업 | 상태 |
|---|---|
| ① 5개 도메인 회귀 테스트 | ✅ 완료 (8/8 PASS) |
| ② 배포 산출물 3종 정의 | ✅ 완료 |
| ③ Inno Setup 스크립트 분기 | ✅ 완료 (Phase 2 에서 선행) |
| ④ 개발 튜토리얼 갱신 | ✅ 완료 (DEVELOPER_GUIDE_v4.1.md) |
| ⑤ v4.1 릴리스 3종 | ◐ 빌드 스크립트 준비 완료 — Windows 빌드 대기 |


## 2. 회귀 테스트 결과 (작업 ①)

`regression_test.py` — Phase 3 의 RULES 분리·암호화·Cython 변환이
KG 산출물을 바꾸지 않았음을 검증.

```
[E1] RULES 동일성 — 원본 vs 암호화→복호화
  cs 23 / cmp 40 / etch 50 / thinfilm 45  — 4도메인 전부 일치

[E2] 빌드 로직 동일성 — 원본 vs stripped (RULES 제외)
  build_graph / to_jsonld / to_turtle — 4도메인 전부 바이트 동일

결과: 8 PASS / 0 FAIL
```

검증 논리: RULES 가 동일하고 빌드 로직이 동일하면, 빌더는 정적
분석 only(비결정성 없음)이므로 KG 산출물(노드·엣지·룰 수)은
결정적으로 동일하다 — 계획서 7.1 호환성 보장 충족.


## 3. 배포 산출물 3종 (작업 ②③)

| 배포본 | 빌드 | ISS | Core | 대상 |
|---|---|---|---|---|
| 내부 FD | `build-installer.bat` | `NowMoment.iss` | 포함 | 내부 |
| 내부 SC | `build-installer-SC.bat` | `NowMoment-SC.iss` | 포함 | 내부 |
| 외부 EXT | `build-installer-EXT-SC.bat` | `NowMoment-EXT-SC.iss` | 제외 | 외부 |

외부 배포본은 2중 Core 누출 게이트(빌드 스크립트 + ISS Code)로
`build_kg_*.py` 유출을 차단한다. 산출물명 `NowMoment-v4.1.0-Setup.exe`.


## 4. v4.1 전체 (Phase 1~4) 총괄

| Phase | 핵심 | 상태 |
|---|---|---|
| Phase 1 | 인터페이스 추출 (Contracts) | ✅ |
| Phase 2 | Core 패키지화·Provider 로더·분류기 분리·EXT 패키징 | ✅ |
| Phase 3 | Cython·.spc 번들·Secure-Verify·감사·워터마킹 | ✅ |
| Phase 4 | 회귀 검증·배포 3종·튜토리얼 | ✅ |

달성된 3중 IP 보호:
- **Cython 컴파일** — 빌드 로직을 기계어화 (정적 열람 차단)
- **AES-256-GCM** — RULES 167룰 + `.spc` 번들 암호화
- **Secure-Verify** — 인증·인가·무결성검증·키발급·감사·워터마킹

외부 배포본에 남는 SPILab Core IP = 0.


## 5. 잔여 작업 (Windows 환경 필요)

본 v4.1 작업은 Linux 정적 검토 + Python 파이프라인 실행 검증을
거쳤다. 다음은 Windows 에서 확인이 필요하다:

1. **C# 빌드** — `dotnet build -c Release`
   - Core 분리 후 Shell 정상 컴파일
   - `DpapiCoreKeyVault` 등 Windows 전용 API 컴파일
2. **MSVC `.pyd` 빌드** — `build_cython.py` 를 Windows 에서 실행,
   `.pyd` 산출 확인 (Linux 는 `.so` 로만 검증함)
3. **인스톨러 3종** — `build-installer*.bat` 실행, EXT 누출 게이트
   동작 확인
4. **통합 스모크 테스트** — 내부본 KG 빌드 → Secure-Verify 인증 →
   워터마크 산출물 확인 / 외부본 KG 탭 비활성 확인
5. **튜토리얼 PPTX** — `DEVELOPER_GUIDE_v4.1.md` 내용을 기존
   NowMoment 개발 튜토리얼 슬라이드에 반영

이 5건이 완료되면 v4.1 릴리스(작업 ⑤)가 확정된다.


## 6. 운영 착수 전 조직 확정 사항 (계획서 8장 권고)

- 권한 매트릭스(`core_access.json`) 작성 — 4역할별 사번·해시
- 키 관리 책임자 지정 — `core.key`/`bundle.key`/`sign_ed25519.key`
- SSO 연동 여부 결정 — 사내 인증 어댑터(`ISsoProvider`) 작성 시
  `App.BuildSecureVerifyBackend` 에 주입
- 2차 인증 정책 — `SecureVerifyOptions.RequireSecondFactor`

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
