# NowMoment — 기술자산관리 시스템
WPF / C# (.NET 8) + SQLite 완전 오프라인 데스크탑 앱

## 파일 구조
```
SPILab.NowMoment/
├── App.xaml / App.xaml.cs          ← 앱 진입점
├── SPILab.NowMoment.csproj               ← 프로젝트 파일 (NuGet 포함)
├── Models/
│   └── AssetModels.cs              ← 6개 엔티티 클래스
├── Services/
│   └── DatabaseService.cs          ← SQLite CRUD 전체
├── ViewModels/
│   ├── MainViewModel.cs            ← 메인 상태 + Command
│   └── EditViewModels.cs           ← 5개 다이얼로그 VM
└── Views/
    ├── MainWindow.xaml/.cs         ← 메인 창
    ├── CodeEditDialog.xaml/.cs     ← 소스코드 등록
    ├── ModelEditDialog.xaml/.cs    ← AI 모델 등록
    ├── DocumentEditDialog.xaml/.cs ← 문서/논문 등록
    ├── PatentEditDialog.xaml/.cs   ← 특허/IP 등록
    └── ExperimentEditDialog.xaml/.cs ← 실험결과 등록
```

## 빌드 방법

### 방법 1 — Visual Studio 2022
1. `SPILab.NowMoment.csproj` 파일을 Visual Studio로 열기
2. NuGet 패키지 자동 복원 대기
3. F5 (디버그 실행) 또는 Ctrl+F5 (릴리스 실행)

### 방법 2 — CLI
```bash
cd SPILab.NowMoment
dotnet restore
dotnet build
dotnet run
```

### 배포 빌드 (단일 실행파일)
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# 결과물: bin/Release/net8.0-windows/win-x64/publish/SPILab.NowMoment.exe
```

## 첫 실행 시 자동 처리
- DB 위치: `%APPDATA%\SPILab\NowMoment\nowmoment.db` 자동 생성
- 스피랩 프로젝트 10개 시드 데이터 자동 등록
  (WCP, KANC, NEO2, 금강방재 등)

## 필수 환경
- Windows 10 / 11 (64bit)
- .NET 8 SDK (빌드용) — https://dotnet.microsoft.com/download/dotnet/8.0
- Visual Studio 2022 Community 이상 (선택)

## 주요 기능
| 탭 | 기능 |
|---|---|
| 대시보드 | 자산 5종 통계 카드 + 글로벌 전체 검색 |
| 소스코드 | GitHub 레포 등록·수정·삭제·검색 |
| AI 모델 | 모델 파일(.pt/.pkl) 경로 + 정확도 관리 |
| 문서·논문 | PDF/DOCX 경로 + 유형(paper/proposal/report) |
| 특허·IP | 출원번호·상태·출원일·발명자 추적 |
| 실험결과 | JSON 파라미터·지표 저장 + 결과 파일 경로 |

## DB 파일 백업
```
%APPDATA%\SPILab\NowMoment\nowmoment.db  →  원하는 위치에 복사
```
