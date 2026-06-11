# Secure-Verify 셋업 — 쉬운 설명서

원본 `SECURE_VERIFY_SETUP_GUIDE.md` 의 내용을, 처음 보는 사람도
따라 할 수 있게 풀어 쓴 문서입니다. 정확한 명령어 레퍼런스는
원본 가이드를, 빠른 실행은 `setup-secure-verify.bat` 을 쓰세요.

---

## 한 줄 요약

> NowMoment 의 "핵심 기술(Core)"에 아무나 손대지 못하도록 **자물쇠**를
> 달았습니다. 자물쇠 장치는 이미 다 만들어졌고, 이제 **누가 열 수
> 있는지(열쇠 명단)** 와 **금고 안에 넣을 내용물(번들·키)** 만
> 준비하면 됩니다. 이 문서가 그 준비 방법입니다.

---

## 1. 큰 그림 — 금고 비유

NowMoment 의 진짜 IP(물리 규칙·KG 빌더)는 "금고" 안에 있다고
생각하세요.

```
   [ NowMoment 앱 ]
        │
        │  KG 빌드 버튼을 누르면
        ▼
   ┌─────────────────────┐
   │  🔒 Secure-Verify   │  ← 자물쇠 (코드로 이미 완성됨)
   │     자물쇠 장치       │
   └─────────────────────┘
        │
        │  통과해야
        ▼
   ┌─────────────────────┐
   │  📦 SPILab Core      │  ← 금고 안 내용물
   │  (KG 빌더·물리 규칙)  │
   └─────────────────────┘
```

자물쇠 장치(인증창·검증 로직)는 **개발이 끝났습니다**. 우리가 할
일은 두 가지뿐입니다:

1. **열쇠 명단 만들기** — 누가 이 금고를 열 수 있는지 적은 목록
2. **금고 내용물·열쇠 준비** — 금고 안에 넣을 Core 번들과 진짜 열쇠

이 두 가지가 없으면 자물쇠는 **그냥 잠긴 채로 둡니다.** 아무도 못
엽니다. (그래서 "데이터 미비 = 잠금" 이라고 합니다 — 안전한 기본값.)

---

## 2. 준비물 — 어디에, 무엇을

모든 파일은 결국 **이 한 폴더**에 모입니다:

```
C:\Users\(사용자)\AppData\Roaming\SPILab\NowMoment\
```

> 탐색기 주소창에 `%APPDATA%\SPILab\NowMoment` 라고 치면 바로 갑니다.

그 폴더에 들어가야 할 6가지:

| 파일 | 비유 | 누가 만드나 |
|---|---|---|
| `core_access.json` | **열쇠 명단** (누가 열 수 있나) | 조직 — `manage_access.py` |
| `SPILab.Core.spc` | **금고 내용물** (암호화된 Core) | Core-Owner — `build_spc.py` |
| `sign_ed25519.pub` | **위조 방지 도장 확인용** | Core-Owner — `build_spc.py` |
| `bundle.key.dpapi` | **금고 열쇠 1** (이 PC 전용으로 잠금) | `setup_core_keys.py` |
| `core.key.dpapi` | **금고 열쇠 2** (이 PC 전용으로 잠금) | `setup_core_keys.py` |
| `build_pipeline\` | **내용물이 진짜인지 검사하는 도구** | `setup_core_keys.py` |

---

## 3. 가장 쉬운 방법 — 도우미 .bat 쓰기

명령어를 외울 필요 없습니다. `kg_builder\build_pipeline\` 폴더의

```
setup-secure-verify.bat
```

을 실행하면 메뉴가 나옵니다. 번호만 누르면 됩니다.

> ### ⚠️ 중요 — 더블클릭하지 말고 "Anaconda Prompt" 에서 실행하세요
>
> 이 PC 는 Python 이 **Anaconda** 로 설치돼 있습니다. 그냥 더블클릭
> 하거나 일반 명령프롬프트(cmd)에서 실행하면, Windows 가 진짜
> Python 대신 **Microsoft Store 의 가짜 python** 을 잡아서 — 화면에
> `Python` 한 단어만 찍히고 아무 동작도 하지 않습니다. (사용자 추가도
> 안 되고 목록도 안 나옵니다.)
>
> **올바른 실행 방법:**
>
> 1. 시작 메뉴에서 **`Anaconda Prompt`** 를 검색해 연다.
> 2. 아래를 입력해 build_pipeline 폴더로 이동한다:
>    ```
>    cd /d "C:\Users\addmin\Desktop\work-260420\project\202604\SPILab_NowMoment\after_v3.0\src\SPILab.NowMoment\kg_builder\build_pipeline"
>    ```
> 3. .bat 을 실행한다:
>    ```
>    setup-secure-verify.bat
>    ```
>
> `build-core-bundle.bat` 도 똑같이 Anaconda Prompt 에서 실행합니다.
>
> 제대로 됐는지 미리 확인하려면, Anaconda Prompt 에서 `python --version`
> 을 쳐 보세요. `Python 3.x.x` 처럼 **버전 번호**가 나오면 정상입니다.
> `Python` 한 단어만 나오면 일반 cmd 를 쓴 것이니 Anaconda Prompt 를
> 다시 여세요.

메뉴 구성:

```
  [1] 권한 매트릭스 만들기      ← 빈 열쇠 명단 생성
  [2] 사용자 추가              ← 명단에 사람 한 명씩 등록
  [3] 사용자 목록 보기
  [4] 비밀번호 검증 테스트
  [5] Core 키/번들을 APPDATA 에 배치  ← build_out 에서 자동으로 찾음
  [6] DPAPI 보호 전환
  [7] 배치 상태 점검          ← 6개 다 됐는지 확인
  [8] core_access.json 을 APPDATA 로 복사
```

### 권장 순서

```
처음이면 →  [1] → [2] 를 사람 수만큼 반복 → [3] 으로 확인 → [8]
그 다음   →  build-core-bundle.bat 더블클릭 (번들·키 생성) → [5] → [7]
비Windows 에서 [5] 했으면 → Windows 에서 [6] → [7]
```

`[7]` 에서 6개가 모두 `✓ 있음` 이면 끝입니다.

---

## 4. 손으로 익히기 — 샘플 파일

이 폴더의 **`core_access.sample.json`** 은 미리 만들어 둔 예시
열쇠 명단입니다. 사용자 4명이 4개 역할을 하나씩 맡고 있습니다.

| 사번 | 이름 | 역할 | 예시 비밀번호 |
|---|---|---|---|
| SPL-001 | 홍길동 | CoreOwner | `OwnerPass!2026` |
| SPL-010 | 이코어 | CoreDeveloper | `DevPass!2026` |
| SPL-042 | 김개발 | CoreRunner | `RunnerPw!2026` |
| SPL-099 | 박외부 | ShellOnly | (없음 — Core 접근 불가) |

이 파일로 도구를 연습해 보세요. **샘플은 연습용입니다 — 실제
운영에는 쓰지 말고, 진짜 비밀번호로 새로 만드세요.**

```bash
# 샘플 명단 들여다보기
python manage_access.py list --file core_access.sample.json

# 비밀번호가 맞는지 테스트 (SPL-001 / OwnerPass!2026 입력해 보기)
python manage_access.py verify --file core_access.sample.json --id SPL-001
```

### 역할 4종 — 무엇을 할 수 있나

| 역할 | 쉽게 말하면 | 누구 |
|---|---|---|
| CoreOwner | 금고 주인 — 전부 가능 | CTO / Core 책임자 |
| CoreDeveloper | Core 코드를 보고 고침 | Core 개발자 |
| CoreRunner | Core 를 쓰기만 함 (KG 빌드) | Shell 개발자, QA |
| ShellOnly | 금고 접근 불가 | 외부 협력사, 일반 사용자 |

---

## 5. 실제 운영 명단 만들기 (3단계)

샘플로 연습했으면, 진짜 명단을 만듭니다. `.bat` 의 `[1] [2]` 와
같은 일입니다.

**1단계 — 빈 명단 만들기**
```bash
python manage_access.py init --out core_access.json
```

**2단계 — 사람 추가** (사람 수만큼 반복)
```bash
python manage_access.py add --file core_access.json \
    --id SPL-001 --name "홍길동" --role CoreOwner
```
→ 실행하면 비밀번호를 물어봅니다. **화면에 안 보이게** 입력됩니다.
   비밀번호는 파일에 평문으로 저장되지 않습니다(해시만 저장).

**3단계 — APPDATA 로 복사**
```bash
copy core_access.json "%APPDATA%\SPILab\NowMoment\"
```
(`.bat` 의 `[8]` 이 이걸 대신 해 줍니다.)

---

## 6. 다 됐는지 확인

```bash
python setup_core_keys.py check
```

이렇게 나오면 성공입니다:

```
  core_access.json      ✓ 있음
  SPILab.Core.spc       ✓ 있음
  sign_ed25519.pub      ✓ 있음
  bundle.key.dpapi      ✓ 있음
  core.key.dpapi        ✓ 있음
  build_pipeline/       ✓ 있음
  → 모든 파일이 배치되었습니다. Secure-Verify 정식 인증이 작동합니다.
```

이제 NowMoment 내부본을 실행하고 KG 빌드 버튼을 누르면, 진짜
**인증창이 뜹니다.** 명단에 등록한 사번·비밀번호를 넣으면 통과됩니다.

---

## 7. 자주 묻는 것

**Q. .bat 을 실행했더니 `Python` 한 단어만 찍히고 아무 일도 안 일어난다.**
일반 명령프롬프트(cmd)나 더블클릭으로 실행해서 Windows 가 가짜
Microsoft Store python 을 잡은 것입니다. **Anaconda Prompt** 를 열어
`cd /d "...\kg_builder\build_pipeline"` 로 이동한 뒤 `.bat` 을 실행하세요
(3장 참조). Anaconda Prompt 에서 `python --version` 이 `Python 3.x.x`
처럼 버전 번호를 보이면 올바른 환경입니다.

**Q. 아직 번들(.spc)·키가 없는데 인증 테스트만 해보고 싶다.**
우회 경로는 없습니다. Core 접근에는 예외 없이 정식 인증이
필요하므로, 인증을 테스트하려면 이 문서의 절차대로 `core_access.json`
과 키·번들을 모두 배치해야 합니다. 배치 전에는 KG 빌드 버튼을
눌러도 "Core 비활성"으로 거부됩니다 — 이것이 정상 동작입니다.

**Q. `.dpapi` 가 붙은 키는 뭔가?**
열쇠를 "이 Windows 계정에서만 열리도록" 한 번 더 잠근 것입니다.
파일을 복사해 가도 다른 PC·계정에서는 못 씁니다. `setup_core_keys.py`
가 자동으로 만들어 줍니다.

**Q. 비밀번호를 잊었다.**
복구할 수 없습니다(해시만 저장하므로). `manage_access.py passwd`
명령으로 새 비밀번호를 설정하세요.

**Q. 비Windows(Mac/Linux)에서 준비하면?**
명단(core_access.json)은 어디서 만들든 같습니다. 단, 키의 DPAPI
잠금은 Windows 전용이라, `.raw` 평문 키로 배치된 뒤 **배포할
Windows PC 에서** `[6] DPAPI 보호 전환` 을 한 번 실행해야 합니다.

---

## 정리 — 무엇을 하면 되나

1. `setup-secure-verify.bat` 더블클릭
2. `[1]` → `[2]`(사람 수만큼) → `[8]` 로 **열쇠 명단** 완성
3. `build-core-bundle.bat` 더블클릭 → **번들·키** 생성
   (SPILab Core 소스가 있는 내부 트리에서, Core-Owner 가 수행)
4. `setup-secure-verify.bat` 의 `[5]` 로 번들·키 배치, 비Windows 였으면 `[6]`
5. `[7]` 로 확인 — 6개 모두 `✓` 면 끝

자물쇠(코드)는 이미 완성돼 있으니, 우리는 **명단과 내용물만**
채우면 됩니다.

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-27
