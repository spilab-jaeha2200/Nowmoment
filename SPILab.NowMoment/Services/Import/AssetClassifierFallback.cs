// ════════════════════════════════════════════════════════════════════
// AssetClassifierFallback.cs — NowMoment v4.1 (Core 분리 / Phase 2 마무리)
//
// IAssetClassifier 의 "폴백" 구현. Shell 어셈블리에 평문으로 남는다.
//
//   v4.0 의 AssetClassifier 는 마커별 신뢰도 매트릭스(C4 IP)를 담고
//   있었다. v4.1 에서 그 휴리스틱 본체는 Core 페이로드
//   (kg_builder/classifier/AssetClassifierCore.cs) 로 이전했다.
//
//   이 폴백은 Core 페이로드가 없는 외부 배포본에서 동작한다.
//   동작 원칙:
//     • 확장자만 보고 자산 종류를 추정한다 (.pt → Model 처럼 자명한 것).
//       확장자 매핑 자체는 도메인 노하우가 아니므로 IP 가 아니다.
//     • 신뢰도는 일률적으로 Low 로 둔다 — 즉 "후보는 보여주되 자동
//       선택은 하지 않고, 사용자가 직접 확인" 하게 한다.
//     • C4 의 핵심인 "마커별 점수표·임계값·경로 키워드 분류" 는
//       전혀 포함하지 않는다.
//
//   PassthroughClassifier 와의 차이:
//     PassthroughClassifier 는 전부 Unknown 으로만 만든다(최소 폴백).
//     이 폴백은 확장자로 종류까지는 추정해 사용자 편의를 약간 높인다.
//   CoreProviderLoader 가 Core 페이로드 유무에 따라
//     있음 → AssetClassifierCore / 없음 → AssetClassifierFallback
//   을 선택한다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services.Import
{
    /// <summary>Core 미탑재 시의 확장자 기반 폴백 분류기 (C4 휴리스틱 없음).</summary>
    public sealed class AssetClassifierFallback : IAssetClassifier
    {
        private readonly ClassifierUtil _util;

        public AssetClassifierFallback(ClassifierUtil? util = null)
            => _util = util ?? new ClassifierUtil();

        /// <summary>폴백 분류기임을 명시 — UI 에서 "간이 분류" 안내에 사용.</summary>
        public bool IsAvailable => false;

        // 확장자 매핑 (자명 — IP 아님)
        private static readonly HashSet<string> ModelExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pt", ".pth", ".onnx", ".pkl", ".h5",
            ".safetensors", ".tflite", ".pb", ".bin", ".ckpt", ".joblib",
        };
        private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx",
        };
        private static readonly HashSet<string> CodeExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".py", ".cs", ".cpp", ".c", ".js", ".ts", ".java", ".rs", ".go", ".ipynb",
        };

        public List<ImportCandidate> Classify(List<ScannedItem> items)
        {
            var candidates = new List<ImportCandidate>();
            if (items == null || items.Count == 0) return candidates;

            // 폴더 그룹핑·마커 인식 없이 단독 파일만 확장자로 추정한다.
            // 종류 추정에 실패하면 Unknown. 모든 후보는 Low 신뢰도.
            foreach (var it in items)
            {
                if (it.IsDirectory) continue;

                ImportAssetKind kind;
                string framework = "";
                if (ModelExts.Contains(it.Extension))
                {
                    kind = ImportAssetKind.Model;
                    framework = it.Extension switch
                    {
                        ".pt" or ".pth" or ".ckpt" or ".safetensors" => "PyTorch",
                        ".onnx"                                      => "ONNX",
                        ".h5" or ".pb"                               => "TensorFlow",
                        ".tflite"                                    => "TFLite",
                        ".pkl" or ".joblib"                          => "scikit-learn",
                        _                                            => "기타",
                    };
                }
                else if (DocumentExts.Contains(it.Extension))
                {
                    kind = ImportAssetKind.Document;
                }
                else if (CodeExts.Contains(it.Extension))
                {
                    kind = ImportAssetKind.Code;
                }
                else
                {
                    continue;   // 알 수 없는 확장자는 후보로 만들지 않음
                }

                var c = new ImportCandidate
                {
                    Kind              = kind,
                    SourcePath        = it.FullPath,
                    Name              = Path.GetFileNameWithoutExtension(it.FullPath),
                    FileSizeBytes     = it.SizeBytes,
                    Framework         = framework,
                    // ── 폴백: 신뢰도는 항상 Low. 자동 선택하지 않는다. ──
                    ConfidencePercent = 50,
                    Confidence        = ImportConfidence.Low,
                    IsSelected        = false,
                    Reason            = $"간이 분류 (확장자 {it.Extension}) — Core 미탑재, 직접 확인 필요",
                };
                if (kind == ImportAssetKind.Document) c.DocType = "document";
                candidates.Add(c);
            }

            return candidates;
        }
    }
}
