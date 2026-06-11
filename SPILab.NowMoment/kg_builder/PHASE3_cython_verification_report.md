# NowMoment v4.1 — Phase 3 사전 검증 리포트: Cython 컴파일 전수 검증

개선 개발계획서 8장 권고: *"Cython 적용 가능성을 Phase 3 이전에 5개
도메인 전수 사전 검증할 것 — 가장 큰 기술 불확실성."*

본 리포트는 그 전수 검증 결과이다. **결론: 5개 도메인 전수 통과.
Cython 1순위 방식 채택 가능.**


## 1. 검증 환경

| 항목 | 값 |
|---|---|
| Python | 3.12 |
| Cython | 3.2.5 |
| 컴파일러 | gcc 13.3 (검증) / Windows 는 MSVC — Phase 3 본작업에서 .pyd 확인 |

> 본 검증은 Linux/`.so` 로 수행했다. Windows 배포 산출물은 `.pyd`
> 이며, MSVC 빌드 확인은 Phase 3 본작업 초기에 별도 수행한다.
> Cython 코드 생성 단계(.py→.c)는 OS 무관이므로 호환성 결론은 유효.


## 2. 전수 컴파일 결과 (.py → .c → .so → import)

| 빌더 | Cython | gcc | import | RULES 수 |
|---|---|---|---|---|
| `build_kg_cs.py`       | ✅ | ✅ | ✅ | 23 |
| `build_kg_photo.py`    | ✅ | ✅ | ✅ | (모듈형 — RULES 리스트 없음) |
| `build_kg_cmp.py`      | ✅ | ✅ | ✅ | 40 |
| `build_kg_etch.py`     | ✅ | ✅ | ✅ | 50 |
| `build_kg_thinfilm.py` | ✅ | ✅ | ✅ | 45 |

**5개 전부 실패·폴백 없이 통과.** 계획서 7.2 의 리스크
("Cython 컴파일이 일부 도메인에서 실패 → PyArmor 폴백")는
현재로서 발생하지 않았다.

통과 요인:
- 5개 빌더 모두 표준 라이브러리만 사용 (argparse/json/re/sys/
  dataclasses/pathlib/typing). 외부 C 확장 의존 없음.
- 정적 분석(정규식 파싱) 구조 — 런타임 메타프로그래밍 없음.
- `from __future__ import annotations` 사용 — Cython 3.x 호환.


## 3. 보호 수준 검증 (계획서 4장)

### 3.1 정적 열람 차단 — 확인됨

컴파일된 `.so` 에 대해 `strings` 추출 시도:

| 검사 대상 | `.py` 원본 | `.so` 컴파일본 |
|---|---|---|
| 룰 관련 문자열(severity/citation/BOWING/Weights) | 29~65건 | **0건** |
| 룰 텍스트(수식·이름·인용) 평문 | 노출 | **미노출** |

→ 빌드 로직과 룰 텍스트 모두 `strings` 등 단순 정적 분석으로는
  추출되지 않는다. 계획서 4.1 L2 의 1차 목표("평문 .py 소거,
  정적 열람 차단")는 달성된다.

### 3.2 한계 — 동적 분석 (계획서 4.1 L2 한계와 일치)

`.so` 를 `import` 하면 `RULES` 가 Python 객체로 메모리에 그대로
올라온다 — 즉 `import build_kg_cmp; build_kg_cmp.RULES` 한 줄로
40개 룰 dict 전량이 평문 노출된다.

이는 계획서 4.1 이 명시한 L2 한계("동적 분석으로 우회 가능")와
정확히 일치한다. Cython 단독으로는 "진짜 IP(룰 수식·인용)" 를
보호하지 못한다.


## 4. 권장안 — 계획서 4.2 "1순위" 타당성 확인

검증 결과는 계획서 4.2 의 권장안이 옳음을 뒷받침한다:

> **1순위 — Cython 컴파일 + 룰 메타데이터 암호화 분리**

구체적으로 Phase 3 본작업은 다음을 수행한다:

1. **빌드 로직** (`build_graph`, 파싱 함수 등) → Cython `.pyd` 컴파일.
   정적 열람 차단 (3.1 에서 확인).
2. **RULES 메타데이터** (수식·인용·severity·가중치) → `.py` 에서
   분리하여 별도 `*.enc` (AES-256-GCM) 로 암호화.
   `.pyd` 가 Secure-Verify 인증 후 발급된 키로 런타임 복호화.

→ 이렇게 하면 3.2 의 한계(메모리 평문 노출)도 "키 없이는 RULES
  자체를 로드 불가" 로 완화된다.


## 5. Phase 3 착수 판정

- [x] Cython 5개 도메인 전수 컴파일 — **통과**
- [x] PyArmor 폴백 필요성 — **현재 없음** (7.2 리스크 미발생)
- [x] 보호 한계 확인 — RULES 암호화 분리가 필수임을 검증
- [x] 권장안(4.2 1순위) 타당성 — **확인**

→ **Phase 3 본작업 착수 가능.** 다음 단계: Cython 빌드 파이프라인
  스크립트화 → RULES 분리·암호화 → `.spc` 번들 → Secure-Verify.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
