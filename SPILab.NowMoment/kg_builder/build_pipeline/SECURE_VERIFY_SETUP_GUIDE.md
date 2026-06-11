# NowMoment v4.1 — Secure-Verify 운영 셋업 가이드

개선 개발계획서 8장 "운영 착수 전 조직 확정 사항" 및 PHASE4 완료
보고서 6장의 후속 절차다. Secure-Verify 인증 시스템의 **코드는 이미
완료**되어 있고, 본 문서는 그 코드가 작동하기 위해 **조직이 만들어
배치해야 하는 파일**의 생성 절차를 정리한다.

---

## 0. 사전 이해 — 무엇이 끝났고 무엇이 남았나

| 구분 | 상태 |
|---|---|
| 인증 대화상자 / 백엔드 / 권한 매트릭스 / 무결성 검증 / 키 발급 코드 | ✅ 구현 완료 |
| 권한 매트릭스 **파일**(`core_access.json`) | ⬜ 조직이 작성 |
| Core 암호화 번들(`SPILab.Core.spc`)·키 파일 | ⬜ Core-Owner 가 생성 |
| 위 파일들의 `%APPDATA%` 배치 | ⬜ 본 가이드로 수행 |

이 파일들이 없으면 백엔드는 **안전하게 거부**한다(매트릭스 없음 →
전원 Shell-Only, 번들 없음 → 검증 실패). 설계상 "데이터 미비 = 잠금"
이므로, 배치를 마쳐야 정식 인증이 작동한다.

배치 경로는 모두 다음 한 폴더다 (App.xaml.cs 의 `BuildSecureVerifyBackend()`
가 읽는 경로):

```
%APPDATA%\SPILab\NowMoment\
```

---

## 1. 운영 도구 3종

`kg_builder/build_pipeline/` 에 있다. 모두 Core-Owner 권한 PC 에서 실행한다.

| 도구 | 역할 |
|---|---|
| `setup-secure-verify.bat` | 권한 매트릭스 생성·키 배치를 메뉴로 실행하는 도우미 |
| `build-core-bundle.bat` | Core 번들·키 생성 4단계를 자동 실행하는 도우미 |
| `manage_access.py` | 권한 매트릭스(`core_access.json`) 생성·편집 |
| `setup_core_keys.py` | 키·번들을 `%APPDATA%` 에 배치, DPAPI 보호 전환 |
| `build_spc.py` 외 | (기존) Core 번들·키 생성 파이프라인 — Phase 3 산출물 |

> **실행 환경 — Anaconda Prompt 에서 실행할 것**
>
> 이 도구들은 모두 Python 을 호출한다. 이 PC 는 Python 이 Anaconda 로
> 설치돼 있으므로, `.bat` 을 더블클릭하거나 일반 명령프롬프트(cmd)에서
> 실행하면 Windows 가 진짜 Python 대신 Microsoft Store 의 빈 python
> 스텁을 잡아 `Python` 한 단어만 출력하고 동작하지 않는다.
>
> 반드시 **Anaconda Prompt** 를 열고, `build_pipeline` 폴더로 이동한
> 뒤 실행한다:
> ```
> cd /d "...\src\SPILab.NowMoment\kg_builder\build_pipeline"
> setup-secure-verify.bat
> ```
> Anaconda Prompt 에서 `python --version` 이 `Python 3.x.x` 로 버전을
> 보이면 올바른 환경이다. (`Python` 한 단어만 나오면 일반 cmd 이므로
> Anaconda Prompt 를 다시 연다.)
>
> 대안: 일반 cmd 에서 쓰려면 ① 설정 → 앱 → 고급 앱 설정 → 앱 실행
> 별칭에서 `python.exe`·`python3.exe` 를 끄고 ② Anaconda 의
> `python.exe`·`Scripts`·`Library\bin` 경로를 PATH 에 등록한다.

---

## 2. 절차 — 처음부터 끝까지

### 2-A. 권한 매트릭스 작성 (`core_access.json`)

누구에게 어떤 Core 역할을 줄지는 **조직이 결정**한다. 역할 4종:

| 역할 | Core 접근 범위 | 대상 |
|---|---|---|
| `CoreOwner` | 번들 생성·서명·룰 편집·키 관리 전체 | CTO / Core 책임자 |
| `CoreDeveloper` | Core 코드 열람·수정·로컬 빌드 | Core 담당 개발자 |
| `CoreRunner` | Core 기능 실행만 (KG 빌드 호출) | Shell 개발자, QA |
| `ShellOnly` | Core 접근 불가 | 외부 협력사, 일반 사용자 |

```bash
cd kg_builder/build_pipeline

# 빈 매트릭스 생성
python manage_access.py init --out core_access.json

# 사용자 추가 — 비밀번호는 화면에 표시되지 않고 입력받는다
python manage_access.py add --file core_access.json \
    --id SPL-001 --name "홍길동" --role CoreOwner
python manage_access.py add --file core_access.json \
    --id SPL-042 --name "김개발" --role CoreRunner

# 목록 확인
python manage_access.py list --file core_access.json

# 비밀번호 검증 테스트 (운영 점검)
python manage_access.py verify --file core_access.json --id SPL-001
```

기타 명령: `set-role`(역할 변경), `passwd`(비밀번호 변경),
`remove`(삭제). SSO 전용 계정은 `add --no-password` 로 등록한다.

> 비밀번호는 PBKDF2-HMAC-SHA256(200,000회)로 해시되어 저장된다.
> 평문은 어디에도 기록되지 않으며, `CoreAccessMatrix.cs` 가 동일
> 알고리즘으로 검증한다.

완성된 `core_access.json` 을 `%APPDATA%\SPILab\NowMoment\` 에 복사한다.

### 2-B. Core 번들·키 생성 (`build-core-bundle.bat`)

Core-Owner 가 수행한다. SPILab Core 소스가 있는 내부 개발 트리에서만
가능하다(외부 배포본 트리에는 소스가 없다).

**가장 쉬운 방법 — 도우미 .bat 더블클릭:**

```
kg_builder\build_pipeline\build-core-bundle.bat
```

이 .bat 은 아래 4단계를 순서대로 자동 실행하고, 결과를
`build_pipeline\build_out\` 폴더에 모은다.

**수동으로 단계별 실행 (참고):**

```bash
# ① RULES 분리·암호화
python extract_rules.py --out-dir build_out --key-file build_out/core.key

# ② Cython 컴파일
python build_cython.py --src-dir build_out --out-dir build_out/native

# ③ .spc 번들 봉인 — bundle.key, sign_ed25519.key/.pub 가 함께 생성됨
python build_spc.py pack --native-dir build_out/native \
    --rules-dir build_out/rules --loader rules_loader.py \
    --out build_out/SPILab.Core.spc --bundle-key build_out/bundle.key \
    --sign-key build_out/sign_ed25519.key

# ④ 번들 무결성 자체 검증
python build_spc.py verify --spc build_out/SPILab.Core.spc \
    --bundle-key build_out/bundle.key --verify-key build_out/sign_ed25519.pub
```

> 필요 환경: Python 3 + `cryptography`, 그리고 ② 단계에 Cython 과
> C 컴파일러(Windows 는 Visual Studio Build Tools).

이 단계 산출물: `SPILab.Core.spc`, `bundle.key`, `core.key`,
`sign_ed25519.key`(개인키 — 비공개 보관), `sign_ed25519.pub`(공개키).

### 2-C. `%APPDATA%` 배치 (`setup_core_keys.py`)

**가장 쉬운 방법** — `setup-secure-verify.bat` 의 `[5]` 메뉴를 쓰면,
2-B 가 만든 `build_out\` 폴더에서 4개 파일(`SPILab.Core.spc`,
`bundle.key`, `core.key`, `sign_ed25519.pub`)을 자동으로 찾아 배치한다.
경로를 직접 입력할 필요가 없다.

**수동 실행 (참고)** — 직접 명령으로 배치하려면:

```bash
python setup_core_keys.py deploy \
    --spc        build_out/SPILab.Core.spc \
    --bundle-key build_out/bundle.key \
    --core-key   build_out/core.key \
    --sign-pub   build_out/sign_ed25519.pub \
    --pipeline-dir .
```

- **Windows 에서 실행하면** — `bundle.key`·`core.key` 가 DPAPI 로
  보호되어 `bundle.key.dpapi`·`core.key.dpapi` 로 배치된다. 바로 사용 가능.
- **비Windows 에서 실행하면** — 평문 키가 `.raw` 로 배치되고, 배포
  Windows PC 에서 다음을 한 번 더 실행해야 한다:

```bash
# 배포 대상 Windows PC 에서
python setup_core_keys.py dpapi-protect
```

이 명령이 `.raw` 평문 키를 DPAPI 보호로 전환하고 평문을 삭제한다.

### 2-D. 배치 상태 점검

```bash
python setup_core_keys.py check
```

6개 파일이 모두 `✓ 있음` 이면 정식 인증이 작동한다.

---

## 3. 최종 배치 결과

```
%APPDATA%\SPILab\NowMoment\
├─ core_access.json      권한 매트릭스 (2-A)
├─ SPILab.Core.spc       Core 암호화 번들 (2-B)
├─ sign_ed25519.pub      서명 검증 공개키 (2-B)
├─ bundle.key.dpapi      DPAPI 보호 bundle.key (2-C)
├─ core.key.dpapi        DPAPI 보호 core.key (2-C)
└─ build_pipeline\
   ├─ build_spc.py       무결성 검증 도구
   └─ rules_loader.py
```

---

## 4. 동작 확인 — 통합 스모크 테스트

배치 완료 후, 내부본을 실행해 PHASE4 보고서 5장 4번 항목을 확인한다.

1. NowMoment 내부본(Core 포함) 실행 → KG 탭에서 [빌드] 클릭.
2. **Secure-Verify 대화상자**가 뜬다 — 2-A 에서 등록한 사번·비밀번호 입력.
3. 인증·인가·`.spc` 무결성 검증 통과 → KG 빌드 실행.
4. 산출물(JSON/TTL)에 워터마크(세션 ID·사용자·시각)가 박혔는지 확인.
5. `audit_log` 에 `core.verify`·`core.build` 이벤트가 적재됐는지 확인.

외부본(EXT-SC)은 `kg_builder/` 자체가 없어 위 절차에 도달하지 않고
KG 탭이 "비활성" 으로 표시된다 — 이것이 정상이다.

> Core 접근에는 예외 없이 정식 인증이 필요하다 — 우회 경로는 없다.
> 따라서 인증을 검증하려면 본 가이드대로 `core_access.json` 과
> 키·번들을 모두 배치한 뒤 내부본을 실행해야 한다.

---

## 5. 키 관리 주의사항 (계획서 4.3 / 7.2)

- `sign_ed25519.key`(서명 개인키)와 `bundle.key`·`core.key`(평문)는
  **`.spc` 와 분리**하여 키 저장소에 보관한다. 배포 PC 에는 DPAPI 보호
  형태(`.dpapi`)만 둔다.
- DPAPI 보호 키는 **그 키를 만든 Windows 사용자 계정에서만** 복호화된다.
  파일을 복사해 가도 다른 PC·계정에서는 못 푼다.
- 키 유출이 의심되면 `build_spc.py` 로 번들을 새 키로 재봉인하고
  `setup_core_keys.py deploy` 를 다시 수행한다(키 회전).

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-27
