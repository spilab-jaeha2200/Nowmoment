# kg_builder

NowMoment 프로젝트 루트 **하위 폴더**로 배치되는 KG 빌더 모듈입니다.
**5개 도메인 (CS / Photo / CMP / Etch / ThinFilm)** 을 지원합니다.

## 배치 위치 (필수)

이 폴더 전체를 NowMoment의 **`.csproj` 가 위치한 폴더 (= 프로젝트 루트)** 에 그대로 복사해 주세요.

```
SPILab.NowMoment/                         ← 프로젝트 루트 (.csproj 위치)
├── SPILab.NowMoment.csproj
├── kg_builder/                           ← 이 폴더
│   ├── build_kg_cs.py                    ← SimCS — GaN/SiC
│   ├── build_kg_photo.py                 ← SimPhoto — 포토리소그래피
│   ├── build_kg_cmp.py                   ← SimCMP — CMP 공정 (신규)
│   ├── build_kg_etch.py                  ← SimEtch — 식각 공정 (신규)
│   ├── build_kg_thinfilm.py              ← SimThinFilm — 박막증착 (신규)
│   ├── dump_photo_to_csharp.py           ← Photo 전용 1단계 변환기
│   └── README.md   (이 파일)
└── ...
```

## 동작 원리

각 `build_kg_*.py` 는 NowMoment WPF 앱이 호출 시 다음 인자를 받습니다:

```bash
python build_kg_*.py --src <엔진.cs 경로> \
    --out-json <APPDATA>\SPILab\NowMoment\kg_builder\kg_raypann_*.json \
    --out-ttl  <APPDATA>\SPILab\NowMoment\kg_builder\kg_raypann_*.ttl
```

출력은 `%APPDATA%\SPILab\NowMoment\kg_builder\` 데이터 폴더로 강제됩니다
(설치 폴더 Program Files 는 권한 없어 쓰기 불가).

## 도메인별 입력 파일

| 도메인 | 입력 (--src) | 출력 JSON |
|---|---|---|
| **cs** | CSPhysicsEngine.cs | kg_raypann_cs.json |
| **photo** | python_engine/ 폴더 (2단계) | kg_raypann_photo.json |
| **cmp** | CmpPhysicsEngine.cs | kg_raypann_cmp.json |
| **etch** | EtchPhysicsEngine.cs | kg_raypann_etch.json |
| **thinfilm** | ThinFilmPhysicsEngine.cs | kg_raypann_thinfilm.json |

## 사용 예

NowMoment WPF 앱의 [KG 빌드] 버튼이 모든 호출을 자동 처리하므로 직접 실행할 필요는
없습니다. 수동 실행이 필요한 경우:

```bash
cd <SPILab.NowMoment 폴더>
python kg_builder\build_kg_cmp.py --src "C:\...\RaypannSimCMP\Core\Physics\CmpPhysicsEngine.cs"
```

각 빌더의 도메인 정보:

| 빌더 | 룰 개수 | Workspaces | 가중치 변수명 |
|---|---|---|---|
| build_kg_cs.py       | 23 (R1~R23)  | 5 (W1~W5) | BOWING |
| build_kg_photo.py    | 9 modules    | 1 (Photo) | (없음) |
| build_kg_cmp.py      | 40 (CM1~CM40)| 5 (W1~W5) | CpiWeights |
| build_kg_etch.py     | 50 (ET1~ET50)| 5 (W1~W5) | EpiWeights |
| build_kg_thinfilm.py | 45 (TF1~TF45)| 5 (W1~W5) | TpiWeights |

## NowMoment 도메인별 데이터 분리

각 도메인의 노드/엣지는 SQLite의 `kg_node.domain` / `kg_edge.domain` 컬럼으로
분리 저장됩니다. KG 탭의 [도메인:] 콤보박스로 도메인 전환 시 해당 도메인의
노드/엣지/통계만 표시됩니다.

JSON-LD `@vocab`:
- raypann-cs#       (CS Edition)
- raypann-photo#    (Photo)
- raypann-cmp#      (CMP)
- raypann-etch#     (Etch)
- raypann-thinfilm# (ThinFilm)

5개 도메인이 namespace 충돌 없이 공존합니다.
