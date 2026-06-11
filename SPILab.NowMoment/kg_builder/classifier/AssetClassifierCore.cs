// ════════════════════════════════════════════════════════════════════
// AssetClassifierCore.cs — NowMoment v4.1 (Core 분리 / Phase 2 마무리)
//
// ★ 이 파일은 "SPILab Core 페이로드" 이다 (개선 개발계획서 2.2 — C4).
//
//   계획서 2.2 C4: AssetClassifier 의 자산 분류 규칙·신뢰도 임계값
//   (conf >= 0.90 / 0.85 / 0.70 등). 이 점수 매트릭스가 SPILab 의
//   분류 노하우이며, 외부 배포본에 평문으로 노출되어서는 안 된다.
//
//   배치 위치 (kg_builder/classifier/) 의 의미:
//     kg_builder/ 는 v4.1 의 Core 페이로드 폴더이다. csproj 가
//     <Compile Include="kg_builder\classifier\*.cs"> 로 내부 빌드에만
//     포함하고, build-installer-EXT.bat 는 kg_builder/ 를 통째로
//     제거한다 → 외부 배포본에 C4 가 들어가지 않는다.
//
//   외부 배포본에서는 이 클래스가 존재하지 않으므로, Shell 의
//   AssetClassifierFallback (확장자 기반, 신뢰도 일률 Low) 이
//   대신 동작한다. CoreProviderLoader 가 자동 선택한다.
//
// 책임 분리:
//   - 본 클래스(Core)  : "점수를 어떻게 매기는가" — 휴리스틱 IP
//   - Shell 측 유틸     : 폴더 그룹핑, 파일 읽기 헬퍼 — IP 아님(공개)
//   Core 는 Shell 유틸을 IClassifierContext 로 주입받아 재사용한다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services.Import;

namespace SPILab.NowMoment.Core.ClassifierCore
{
    /// <summary>
    /// 계획서 2.2 C4 — 자산 분류 휴리스틱 본체.
    /// IAssetClassifier 의 실제(full) 구현. Core 페이로드에만 존재한다.
    /// </summary>
    public sealed class AssetClassifierCore : IAssetClassifier
    {
        private readonly ClassifierUtil _util;

        public AssetClassifierCore(ClassifierUtil util) => _util = util;

        /// <summary>Core 분류기는 항상 사용 가능 (페이로드가 있으면 로드됨).</summary>
        public bool IsAvailable => true;

        // ── 모델 / 문서 확장자 (확장자 자체는 IP 아님 — 자명한 매핑) ──
        private static readonly HashSet<string> ModelExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pt", ".pth", ".onnx", ".pkl", ".h5",
            ".safetensors", ".tflite", ".pb", ".bin", ".ckpt", ".joblib",
        };
        private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx",
        };

        // ── ★ C4 IP: 경로 키워드 → 문서 유형 ──
        private static readonly string[] PaperKeywords =
            { "paper", "papers", "publication", "publications", "manuscript", "논문" };
        private static readonly string[] ProposalKeywords =
            { "proposal", "proposals", "rfp", "grant", "제안서" };
        private static readonly string[] ReportKeywords =
            { "report", "reports", "보고서" };

        // ── ★ C4 IP: 분류 신뢰도 임계값 (계획서 2.2 — conf 0.90/0.85/0.70) ──
        private const int ThresholdHigh   = 85;   // ≥ 85% → High
        private const int ThresholdMedium = 70;   // ≥ 70% → Medium

        // ── 코드 마커 종류 ──
        public enum CodeMarkerKind
        {
            Git, CsProject, Solution, PyProjectToml, SetupPy, RequirementsTxt, Pipfile,
        }

        // ════════════════════════════════════════════════════════════
        // 진입점 — FolderScanner 결과를 자산 후보로 분류
        // ════════════════════════════════════════════════════════════
        public List<ImportCandidate> Classify(List<ScannedItem> items)
        {
            var candidates = new List<ImportCandidate>();
            if (items == null || items.Count == 0) return candidates;

            var byDir = items.GroupBy(it => Path.GetDirectoryName(it.FullPath) ?? "");
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in byDir)
            {
                var inDir = group.ToList();

                // 1) AssetCode — .git 디렉터리
                var gitMarker = inDir.FirstOrDefault(
                    x => x.IsDirectory && x.Name.Equals(".git", StringComparison.OrdinalIgnoreCase));
                if (gitMarker != null)
                {
                    string repoRoot = Path.GetDirectoryName(gitMarker.FullPath) ?? "";
                    candidates.Add(BuildCodeCandidate(repoRoot, inDir, CodeMarkerKind.Git));
                    foreach (var it in inDir.Where(x => !x.IsDirectory))
                        processedFiles.Add(it.FullPath);
                    continue;
                }

                // 1-b) C# / Python 프로젝트 마커
                var codeMarker = FindCodeMarker(inDir);
                if (codeMarker != null)
                {
                    candidates.Add(BuildCodeCandidate(group.Key, inDir, codeMarker.Value));
                    foreach (var it in inDir.Where(x => !x.IsDirectory))
                        processedFiles.Add(it.FullPath);
                    continue;
                }

                // 2) AssetExperiment — metrics.json + params.json 셋트
                var metrics = inDir.FirstOrDefault(
                    x => !x.IsDirectory && x.Name.Equals("metrics.json", StringComparison.OrdinalIgnoreCase));
                var prms = inDir.FirstOrDefault(
                    x => !x.IsDirectory && x.Name.Equals("params.json", StringComparison.OrdinalIgnoreCase));
                if (metrics != null && prms != null)
                {
                    candidates.Add(BuildExperimentCandidate(group.Key, metrics, prms));
                    processedFiles.Add(metrics.FullPath);
                    processedFiles.Add(prms.FullPath);
                }
            }

            // 3) 단독 파일 (Model / Document)
            foreach (var it in items)
            {
                if (it.IsDirectory) continue;
                if (processedFiles.Contains(it.FullPath)) continue;

                if (ModelExts.Contains(it.Extension))
                {
                    candidates.Add(BuildModelCandidate(it));
                    continue;
                }
                if (DocumentExts.Contains(it.Extension))
                {
                    candidates.Add(BuildDocumentCandidate(it));
                }
            }

            return candidates;
        }

        // ── 코드 마커 탐지 (우선순위 순) ──
        private static CodeMarkerKind? FindCodeMarker(List<ScannedItem> inDir)
        {
            var files = inDir.Where(x => !x.IsDirectory).ToList();
            if (files.Any(x => x.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.Solution;
            if (files.Any(x => x.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.CsProject;
            if (files.Any(x => x.Name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.PyProjectToml;
            if (files.Any(x => x.Name.Equals("setup.py", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.SetupPy;
            if (files.Any(x => x.Name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.Pipfile;
            if (files.Any(x => x.Name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)))
                return CodeMarkerKind.RequirementsTxt;
            return null;
        }

        // ════════════════════════════════════════════════════════════
        // AssetCode 후보 — ★ C4 IP: 마커별 신뢰도 매트릭스
        // ════════════════════════════════════════════════════════════
        private ImportCandidate BuildCodeCandidate(
            string repoRoot, List<ScannedItem> inDir, CodeMarkerKind marker)
        {
            var c = new ImportCandidate
            {
                Kind       = ImportAssetKind.Code,
                SourcePath = repoRoot,
                Name       = Path.GetFileName(repoRoot),
            };

            var hasReadme = inDir.Any(x => !x.IsDirectory
                && x.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));

            // ★ C4 IP — 마커 종류별 기본 신뢰도 + README 보너스
            int conf = marker switch
            {
                CodeMarkerKind.Git             => hasReadme ? 95 : 90,
                CodeMarkerKind.Solution        => hasReadme ? 92 : 88,
                CodeMarkerKind.CsProject       => hasReadme ? 90 : 85,
                CodeMarkerKind.PyProjectToml   => hasReadme ? 90 : 85,
                CodeMarkerKind.SetupPy         => hasReadme ? 88 : 82,
                CodeMarkerKind.Pipfile         => hasReadme ? 80 : 70,
                CodeMarkerKind.RequirementsTxt => hasReadme ? 75 : 60,
                _                              => 60,
            };
            ApplyConfidence(c, conf);

            c.RepoUrl  = (marker == CodeMarkerKind.Git)
                         ? (_util.TryReadGitOrigin(Path.Combine(repoRoot, ".git", "config")) ?? "")
                         : "";
            c.Language = GuessLanguageByMarker(marker) ?? _util.GuessLanguage(repoRoot);
            c.Version  = _util.GuessVersion(repoRoot);

            var readme = inDir.FirstOrDefault(x => !x.IsDirectory
                && x.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));
            if (readme != null)
                c.Description = _util.TryReadFirstLine(readme.FullPath, 200) ?? "";

            string markerLabel = marker switch
            {
                CodeMarkerKind.Git             => ".git 디렉터리",
                CodeMarkerKind.Solution        => ".sln (VS 솔루션)",
                CodeMarkerKind.CsProject       => ".csproj (C# 프로젝트)",
                CodeMarkerKind.PyProjectToml   => "pyproject.toml (Python)",
                CodeMarkerKind.SetupPy         => "setup.py (Python)",
                CodeMarkerKind.Pipfile         => "Pipfile (Python)",
                CodeMarkerKind.RequirementsTxt => "requirements.txt (Python)",
                _                              => "코드 마커",
            };
            var reasons = new List<string> { markerLabel };
            if (hasReadme) reasons.Add("README");
            if (!string.IsNullOrEmpty(c.RepoUrl)) reasons.Add("git origin URL");
            c.Reason = string.Join(" + ", reasons);

            return c;
        }

        private static string? GuessLanguageByMarker(CodeMarkerKind marker) => marker switch
        {
            CodeMarkerKind.Solution        => "C#",
            CodeMarkerKind.CsProject       => "C#",
            CodeMarkerKind.PyProjectToml   => "Python",
            CodeMarkerKind.SetupPy         => "Python",
            CodeMarkerKind.Pipfile         => "Python",
            CodeMarkerKind.RequirementsTxt => "Python",
            _                              => null,
        };

        // ════════════════════════════════════════════════════════════
        // AssetModel 후보 — ★ C4 IP: checkpoint 감점 규칙
        // ════════════════════════════════════════════════════════════
        private ImportCandidate BuildModelCandidate(ScannedItem it)
        {
            var c = new ImportCandidate
            {
                Kind          = ImportAssetKind.Model,
                SourcePath    = it.FullPath,
                Name          = Path.GetFileNameWithoutExtension(it.FullPath),
                FileSizeBytes = it.SizeBytes,
            };

            c.Framework = it.Extension switch
            {
                ".pt" or ".pth" or ".ckpt" => "PyTorch",
                ".onnx"                    => "ONNX",
                ".h5"                      => "TensorFlow",
                ".pb"                      => "TensorFlow",
                ".tflite"                  => "TFLite",
                ".pkl" or ".joblib"        => "scikit-learn",
                ".safetensors"             => "PyTorch",
                ".bin"                     => "기타",
                _                          => "기타",
            };

            // ★ C4 IP — 모델 가중치 확장자는 명확하므로 85%,
            //   단 checkpoint/epoch/step 단어는 중간 산출물이라 70 으로 감점
            int conf = 85;
            string nameLower = it.Name.ToLowerInvariant();
            if (nameLower.Contains("checkpoint") || nameLower.Contains("ckpt")
                || nameLower.Contains("epoch") || nameLower.Contains("step"))
                conf = 70;
            ApplyConfidence(c, conf);

            c.Reason = $"확장자 {it.Extension} → {c.Framework} 가중치 ({_util.FormatSize(it.SizeBytes)})";
            return c;
        }

        // ════════════════════════════════════════════════════════════
        // AssetDocument 후보 — ★ C4 IP: 경로 키워드 → doc_type + 신뢰도
        // ════════════════════════════════════════════════════════════
        private ImportCandidate BuildDocumentCandidate(ScannedItem it)
        {
            var c = new ImportCandidate
            {
                Kind       = ImportAssetKind.Document,
                SourcePath = it.FullPath,
                Name       = Path.GetFileNameWithoutExtension(it.FullPath),
            };

            string pathLower = it.FullPath.ToLowerInvariant();

            // ★ C4 IP — 경로 키워드 분류 + 신뢰도
            int conf;
            if (PaperKeywords.Any(k => pathLower.Contains(k)))      { c.DocType = "paper";    conf = 85; }
            else if (ProposalKeywords.Any(k => pathLower.Contains(k))) { c.DocType = "proposal"; conf = 85; }
            else if (ReportKeywords.Any(k => pathLower.Contains(k)))   { c.DocType = "report";   conf = 85; }
            else                                                       { c.DocType = "document"; conf = 60; }
            ApplyConfidence(c, conf);

            c.Reason = $"확장자 {it.Extension}, 경로 분류 → {c.DocType}";
            return c;
        }

        // ── AssetExperiment 후보 ──
        private ImportCandidate BuildExperimentCandidate(
            string dir, ScannedItem metrics, ScannedItem prms)
        {
            var c = new ImportCandidate
            {
                Kind       = ImportAssetKind.Experiment,
                SourcePath = dir,
                Name       = Path.GetFileName(dir),
            };
            ApplyConfidence(c, 75);   // ★ C4 IP — metrics+params 셋트 = 75%
            c.Reason      = "metrics.json + params.json 셋트";
            c.MetricsJson = _util.TryReadAll(metrics.FullPath, 8 * 1024) ?? "{}";
            c.ParamsJson  = _util.TryReadAll(prms.FullPath,    8 * 1024) ?? "{}";
            return c;
        }

        // ── ★ C4 IP — 신뢰도 점수 → 등급/선택 상태 (임계값 85/70) ──
        private static void ApplyConfidence(ImportCandidate c, int conf)
        {
            c.ConfidencePercent = conf;
            c.Confidence = conf >= ThresholdHigh   ? ImportConfidence.High
                         : conf >= ThresholdMedium ? ImportConfidence.Medium
                         : ImportConfidence.Low;
            c.IsSelected = c.Confidence != ImportConfidence.Low;
        }
    }
}
