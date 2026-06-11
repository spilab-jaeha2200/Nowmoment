# NowMoment v4.1 — 배포본 빌드 안내 (Core 분리)

개선 개발계획서 6.2 / 6.4 기준. v4.1 부터 배포 산출물은 **3종**이며,
SPILab Core 페이로드(`kg_builder/`)의 포함 여부로 나뉜다.

## 배포본 3종

| 배포본 | 빌드 스크립트 | ISS 스크립트 | Core | 배포 방식 | 대상 |
|---|---|---|---|---|---|
| 내부 개발본 (FD) | `build-installer.bat`    | `NowMoment.iss`     | 포함 | Framework-Dependent | SPILab 내부 |
| 내부 개발본 (SC) | `build-installer-SC.bat` | `NowMoment-SC.iss`  | 포함 | Self-Contained | SPILab 내부 |
| **외부 배포본**   | `build-installer-EXT-SC.bat`| `NowMoment-EXT-SC.iss` | **제외** | **Self-Contained** | 외부 고객·협력사 |

> FD = Framework-Dependent (대상 PC 에 .NET 8 Desktop Runtime 필요)
> SC = Self-Contained (.NET 8 런타임 산출물 내장 — 사전 설치 불필요)

## 외부 배포본 빌드 (`build-installer-EXT-SC.bat`)

내부 SC 빌드와 동일한 Self-Contained 방식이되, 두 단계가 추가된다:

1. `dotnet publish` 산출물에서 `kg_builder\` 폴더를 **물리적으로 삭제**
2. **Core 페이로드 누출 게이트** — `build_kg_*.py` 가 산출물에 하나라도
   남아 있으면 빌드를 중단한다 (계획서 8장 — 실수 유출 차단).

`NowMoment-EXT-SC.iss` 에도 동일 게이트가 `[Code]` 의 `InitializeSetup` 에
들어 있어 ISCC 컴파일 시점에 한 번 더 검사한다 (2중 방어).

publish 명령:
```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false -p:DebugType=embedded -p:PublishReadyToRun=true
```

산출물: `installer\Output\NowMoment-v4.1.0-Setup.exe` (~80 MB+,
.NET 8 런타임 내장)

## 동작 차이 (외부 배포본)

`CoreProviderLoader` 가 `kg_builder/` 부재를 감지 → `NullKgBuilder` +
`AssetClassifierFallback` 등록. 따라서 외부 배포본에서는:

- KG 빌드 / 자동 분류(full 휴리스틱) → "기능 비활성 (Core 미탑재)" 안내
- 폴더 임포트 자체는 동작 — 확장자 기반 간이 분류(Low 신뢰도)로 폴백
- 자산 관리 · 백업 · TTL Studio 열람 · PDF/Excel 내보내기 → **정상 동작**

코드 분기는 없다. 폴더 제거만으로 안전하게 분리된다 (계획서 3.4).

## 검증 기준 (Phase 2 종료 조건)

- [ ] `build-installer-EXT-SC.bat` 실행 → 산출물에 `.py` 빌더 0개
- [ ] 산출물 파일명이 `NowMoment-v4.1.0-Setup.exe` 인지 확인
- [ ] EXT 인스톨러가 .NET 런타임 없는 PC 에서도 설치·실행되는지
- [ ] EXT 인스톨러 설치 후 KG 탭 = 비활성 안내
- [ ] EXT 설치본에서 자산관리·백업·내보내기 정상
- [ ] 산출물에 Core 가 섞이면 빌드가 `:core_leak` 으로 중단되는지 확인
      (테스트: publish 후 `kg_builder\dummy build_kg_x.py` 를 만들고 재빌드)

— SPILab Co., Ltd. / NowMoment Integration Track / 2026-05-26
