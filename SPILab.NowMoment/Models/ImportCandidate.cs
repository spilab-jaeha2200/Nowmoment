// ════════════════════════════════════════════════════════════
// ImportCandidate.cs — v3.0 F-002 폴더 임포트 (Step 2.1)
//
// FolderScanner + AssetClassifier 가 발견한 자산 후보 1건을
// 표현하는 통합 DTO. 5종 자산 모델(AssetCode/Model/Document/Patent/Experiment)
// 을 단일 그리드에 일관되게 보여주기 위한 view-model 형태.
//
// Confirm() 호출 시 실제 DB 자산 모델로 변환된다 (Step 2.4 에서 사용).
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SPILab.NowMoment.Models
{
    /// <summary>임포트 후보의 자산 분류.</summary>
    public enum ImportAssetKind
    {
        Unknown,        // 자동 분류 실패 — 사용자가 수동 지정 필요
        Code,           // .git/ + README.md 등
        Model,          // *.pt / *.onnx / *.pkl / *.h5 등
        Document,       // *.pdf / *.docx (paper/proposal/report)
        Experiment,     // metrics.json + params.json 셋
    }

    /// <summary>자동 분류 신뢰도 (UI 색상 결정용).</summary>
    public enum ImportConfidence
    {
        High,           // ≥ 85% — 초록 (체크 기본 ON)
        Medium,         // 70~84% — 노랑 (체크 기본 ON, 검토 권장)
        Low,            // < 70% — 빨강 (체크 기본 OFF)
    }

    /// <summary>임포트 미리보기 그리드의 한 행을 표현하는 DTO.</summary>
    public class ImportCandidate : INotifyPropertyChanged
    {
        // ── 분류 결과 ────────────────────────────────────
        public ImportAssetKind  Kind       { get; set; } = ImportAssetKind.Unknown;
        public ImportConfidence Confidence { get; set; } = ImportConfidence.Low;
        public int              ConfidencePercent { get; set; } = 0;

        // ── 그리드 표시용 ────────────────────────────────
        /// <summary>UI 체크박스 상태 (사용자가 토글). 기본값은 신뢰도에 따라 결정.</summary>
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>자산명/제목 (사용자 편집 가능).</summary>
        public string Name       { get; set; } = "";

        /// <summary>발견된 절대 경로 (파일 또는 디렉터리).</summary>
        public string SourcePath { get; set; } = "";

        /// <summary>분류 근거 — UI 툴팁이나 상세 정보용.</summary>
        public string Reason     { get; set; } = "";

        /// <summary>이미 DB 에 등록되어 있는지 (Step 2.4 에서 채움).</summary>
        public bool   IsDuplicate { get; set; } = false;

        /// <summary>중복인 경우 어떻게 식별되었는지 ("repo_url 일치", "file_path 일치" 등).</summary>
        public string DuplicateReason { get; set; } = "";

        // ── 추출된 메타데이터 (자산 종류별로 다른 필드 사용) ──
        // 모든 필드를 한 클래스에 두는 것은 가독성이 떨어지지만,
        // 그리드 바인딩의 단순성을 위해 의도적으로 평탄화한 구조이다.
        public string Language    { get; set; } = "";   // Code
        public string RepoUrl     { get; set; } = "";   // Code
        public string Version     { get; set; } = "";   // Code, Document
        public string Tags        { get; set; } = "";   // Code
        public string Description { get; set; } = "";   // Code, Model

        public string Framework   { get; set; } = "";   // Model
        public long   FileSizeBytes { get; set; } = 0;  // Model

        public string DocType     { get; set; } = "";   // Document — paper/proposal/report 등
        public string Summary     { get; set; } = "";   // Document

        public string ParamsJson  { get; set; } = "{}"; // Experiment
        public string MetricsJson { get; set; } = "{}"; // Experiment

        // ── 그리드 표시 헬퍼 ────────────────────────────
        public string KindLabel => Kind switch
        {
            ImportAssetKind.Code       => "소스코드",
            ImportAssetKind.Model      => "AI 모델",
            ImportAssetKind.Document   => "문서",
            ImportAssetKind.Experiment => "실험",
            _                          => "미분류",
        };

        public string ConfidenceLabel => $"{ConfidencePercent}%";

        /// <summary>그리드의 신뢰도 셀 배경색 식별용 (XAML 컨버터/DataTrigger 에서 사용).</summary>
        public string ConfidenceTag => Confidence switch
        {
            ImportConfidence.High   => "high",
            ImportConfidence.Medium => "medium",
            _                       => "low",
        };

        /// <summary>경로의 마지막 1~2단계만 짧게 (UI 표시용).</summary>
        public string ShortPath
        {
            get
            {
                if (string.IsNullOrEmpty(SourcePath)) return "";
                try
                {
                    var parts = SourcePath.Replace('\\', '/').TrimEnd('/').Split('/');
                    if (parts.Length >= 2) return $".../{parts[^2]}/{parts[^1]}";
                    return parts[^1];
                }
                catch { return SourcePath; }
            }
        }

        // ── INotifyPropertyChanged — IsSelected 변경 시 그리드 체크박스 갱신 ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
