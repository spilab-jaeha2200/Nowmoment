============================================================
NowMoment v4.1 — 전체 패치 적용 안내 (Phase 1~4 완료본)
============================================================

본 zip 은 v4.0 소스 위에 덮어쓰는 구조다. 폴더 트리가 원본과
동일하므로 SPILab.NowMoment\ 를 기존 프로젝트에 병합한다.

[삭제 필요 — 적용 시 반드시]
  Services/Import/AssetClassifier.cs
    → ClassifierUtil/AssetClassifierFallback/AssetClassifierCore 3종으로
      분할됨. 남기면 타입 중복 빌드 실패.

[빌드 검증 — Windows 필수]
  1. dotnet build -c Release        (Core 분리 후 Shell 컴파일)
  2. kg_builder/build_pipeline/ 파이프라인 (Core-Owner)
       extract_rules → build_cython → build_spc → regression_test
  3. installer/build-installer-EXT-SC.bat  (외부 배포본)

[운영 착수 전 조직 확정]
  - core_access.json (권한 매트릭스, 4역할)
  - 키 관리 책임자 (core.key / bundle.key / sign_ed25519.key)
  - SSO 연동 여부 (ISsoProvider 어댑터)

상세: PHASE4_completion_report.md / DEVELOPER_GUIDE_v4.1.md
파이프라인: kg_builder/build_pipeline/README.md

— SPILab Co., Ltd. / 2026-05-26
