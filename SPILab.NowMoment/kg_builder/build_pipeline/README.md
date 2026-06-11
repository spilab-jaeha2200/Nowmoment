# NowMoment v4.1 — Phase 3 작업 1: Cython 빌드 파이프라인

개선 개발계획서 6.3 Phase 3 의 첫 작업. KG 빌더 5종을 보호된
네이티브 형태로 변환하는 빌드 파이프라인이다.

> 사전 검증(`../PHASE3_cython_verification_report.md`)에서 5개 도메인
> 전수 Cython 컴파일 통과를 확인한 뒤 본 파이프라인을 구성했다.


## 파이프라인 개요

```
  build_kg_*.py  (원본 — RULES 평문 포함)
        │
        │  ① extract_rules.py
        ├──────────────────────────────┐
        ▼                              ▼
  build_kg_*.stripped.py         rules/*.enc
  (RULES 제거 + 로더 스텁)        (AES-256-GCM 암호화 RULES)
        │
        │  ② build_cython.py
        ▼
  build_kg_*.pyd / .so
  (네이티브 컴파일 — 정적 열람 차단)
        │
        │  ③ build_spc.py  ← Phase 3 작업 2 (별도)
        ▼
  SPILab.Core.spc  (.pyd + rules/*.enc 봉인 번들)
```

런타임: 컴파일된 빌더가 `rules_loader.py` 를 통해 `*.enc` 를 복호화.
복호화 키는 Secure-Verify(작업 3)가 인증 후 환경변수로 주입한다.


## 구성 파일

빌드 파이프라인(번들 생성) 과 운영 도구(인증·키 배치) 로 나뉜다.

**빌드 파이프라인 — Core 번들 생성**

| 파일 | 역할 |
|---|---|
| `extract_rules.py` | 빌더에서 RULES 분리 → `stripped.py` + `*.enc` |
| `rules_loader.py`  | 런타임 RULES 복호화 로더 (.pyd 와 함께 번들됨) |
| `build_cython.py`  | `stripped.py` → `.pyd`/`.so` 컴파일 |
| `build_spc.py`     | `.pyd` + `*.enc` + 로더 → `SPILab.Core.spc` 번들 봉인/검증 |
| `watermark.py`     | KG 산출물에 세션 정보 워터마크 삽입 (계획서 5.4) |
| `regression_test.py` | 보호 변환 전후 KG 산출물 동일성 회귀 검증 |

**운영 도구 — Secure-Verify 인증·키 배치**

| 파일 | 역할 |
|---|---|
| `manage_access.py` | 권한 매트릭스(`core_access.json`) 생성·편집 (계획서 5.2) |
| `setup_core_keys.py` | 키·번들을 `%APPDATA%` 에 배치, DPAPI 보호 전환 (계획서 4.3) |
| `setup-secure-verify.bat` | 위 두 도구를 메뉴로 실행하는 대화형 도우미 (Windows) |
| `build-core-bundle.bat` | Core 번들·키 생성 4단계를 자동 실행하는 도우미 (Windows) |
| `core_access.sample.json` | 권한 매트릭스 예시 — 4역할 4명 (연습용) |
| `SECURE_VERIFY_SETUP_GUIDE.md` | 운영 셋업 전체 절차 가이드 |
| `SECURE_VERIFY_쉬운설명.md` | 위 가이드를 입문자용으로 풀어 쓴 설명서 |


## 사용법

> **실행 환경:** Python 이 Anaconda 로 설치된 PC 에서는 **Anaconda
> Prompt** 를 열어 실행한다. 일반 cmd·더블클릭은 Microsoft Store 의
> python 스텁이 잡혀 `Python` 만 출력되고 동작하지 않는다.
> `python --version` 이 버전 번호를 보이는 창에서 실행할 것.

```bash
# ① RULES 분리·암호화 (Core-Owner 권한 PC 에서)
python extract_rules.py --out-dir ../build_out --key-file core.key

# ② Cython 컴파일
python build_cython.py --src-dir ../build_out --out-dir ../build_out/native

# ③ .spc 번들 봉인
python build_spc.py pack \
    --native-dir ../build_out/native --rules-dir ../build_out/rules \
    --loader rules_loader.py --out ../build_out/SPILab.Core.spc \
    --bundle-key bundle.key --sign-key sign_ed25519.key

# 번들 무결성 검증
python build_spc.py verify --spc ../build_out/SPILab.Core.spc \
    --bundle-key bundle.key --verify-key sign_ed25519.pub

# 산출물:
#   ../build_out/build_kg_*.stripped.py   (중간물)
#   ../build_out/rules/*.enc              (번들 입력)
#   ../build_out/native/build_kg_*.pyd    (번들 입력)
#   ../build_out/SPILab.Core.spc          ← 최종 Core 번들
#   core.key / bundle.key / sign_*.key    (★ 키 저장소로 분리 보관)
```

요구 패키지: `pip install cython cryptography setuptools`
Windows `.pyd` 빌드: Visual Studio Build Tools (MSVC) 필요.


## 적용 범위

| 빌더 | RULES 분리 | Cython | 비고 |
|---|---|---|---|
| `build_kg_cs.py`       | ✅ 23룰  | ✅ | |
| `build_kg_cmp.py`      | ✅ 40룰  | ✅ | |
| `build_kg_etch.py`     | ✅ 50룰  | ✅ | |
| `build_kg_thinfilm.py` | ✅ 45룰  | ✅ | |
| `build_kg_photo.py`    | — | ✅ | RULES 리스트 없는 모듈형. Cython 만 적용 |

photo 빌더는 `extract_rules` 대상이 아니다. `build_cython.py` 로
컴파일하려면 `build_kg_photo.py` 를 `build_kg_photo.stripped.py` 로
복사해 `--src-dir` 에 두면 된다(RULES 분리 없이 그대로 컴파일).


## 검증 결과 (Linux/.so 기준)

- RULES 분리·암호화: 4빌더 158룰 — `.enc` 생성 OK
- 라운드트립: 복호화 RULES == 원본 RULES — 4도메인 일치
- 키 차단: 키 없음 / 잘못된 키 → 로드 거부 (GCM 인증 실패 탐지)
- stripped 빌더: 로더 경유 import — 4종 정상, `build_graph` 보존
- Cython 컴파일: `.stripped.py` → `.so` — 4종 OK
- 컴파일본 동작: `.so` import + RULES 복호화 — 4종 정상
- `.spc` 번들: pack/verify OK — 빌더 4 + 룰 4도메인 + 로더 봉인
- `.spc` 무결성: 잘못된 키·바이트 변조 → GCM 인증 실패로 차단
- `.spc` 평문: manifest/빌더명/ZIP 시그니처조차 미노출 (전체 암호문)
- end-to-end: `.spc` 복호화 → 전개 → 키 주입 → 빌더 실행 — 정상
- 보호 수준:
  - `.so` 바이너리 — 룰 수식·이름 평문 **미노출**
  - `.enc` 파일 — AES-256-GCM 암호문, 평문 **미노출**
  - `.spc` 번들 — AES-256-GCM, 키 없이는 전개 불가
  - 키 없이 `.so` 빌더 import — **차단됨**


## 보안 주의

- `core.key`(RULES 키), `bundle.key`(번들 키), `sign_ed25519.key`
  (서명 개인키)는 `.spc`·Git 저장소에 **절대 포함 금지**.
  Secure-Verify(작업 3)가 인증 후 키 저장소에서 발급한다.
- `sign_ed25519.pub`(서명 공개키)는 Shell 의 Provider 로더에 내장하여
  번들 무결성 검증에 사용한다 — 공개키이므로 노출되어도 무방.
- 복호화된 RULES 는 프로세스 메모리에만 존재. 디스크 평문 기록 없음.
- 본 파이프라인은 빌드 PC(Core-Owner)에서만 실행한다.


## 다음 단계

- Phase 3 작업 3: Secure-Verify Mode — 인증 대화상자 + 권한 매트릭스
  (역할 4종). 인증 통과 시 `bundle.key`/`core.key` 를 메모리 발급하고,
  `build_spc.py verify` 로 번들 무결성을 확인한 뒤 Core 를 활성화.
- Phase 3 작업 4: `audit_log` 확장 (Core 이벤트 4종 적재).
- Phase 3 작업 5: 산출물 워터마킹.
- Windows MSVC `.pyd` 빌드는 작업 3 착수 시 실제 확인.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
