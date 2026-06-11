// ════════════════════════════════════════════════════════════════════
// PassthroughClassifier.cs — NowMoment v4.1 (Core 분리)
//
// IAssetClassifier 최소 폴백. 스캔된 파일을 모두 '미분류'(Unknown /
// Low) 후보로만 만든다.
//
// ★ v4.1 Phase 2 마무리 안내:
//   기본 폴백은 이제 AssetClassifierFallback (확장자 기반, 종류까지
//   추정) 으로 일원화되었다. CoreProviderLoader 는 더 이상 이 클래스를
//   사용하지 않는다.
//   이 클래스는 "분류를 전면 비활성" 해야 하는 경우(예: Phase 3 의
//   세분화된 권한 등급에서 분류 자체를 막는 정책)를 위한 예비
//   구현으로 보존한다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services.Import;  // ScannedItem

namespace SPILab.NowMoment.Core.Provider
{
    [Obsolete("기본 폴백은 AssetClassifierFallback 로 대체됨. 분류 전면 비활성 정책용 예비 구현.")]
    public sealed class PassthroughClassifier : IAssetClassifier
    {
        public bool IsAvailable => false;

        public List<ImportCandidate> Classify(List<ScannedItem> items)
        {
            var list = new List<ImportCandidate>();
            if (items == null) return list;

            foreach (var it in items)
            {
                if (it.IsDirectory) continue; // 폴더 자체는 후보로 만들지 않음

                list.Add(new ImportCandidate
                {
                    Kind              = ImportAssetKind.Unknown,
                    Confidence        = ImportConfidence.Low,
                    ConfidencePercent = 30,
                    IsSelected        = false,    // 낮은 신뢰도 — 기본 체크 OFF
                    Name              = it.Name,
                    SourcePath        = it.FullPath,
                    Reason            = "자동 분류 비활성 (Core 미탑재) — 자산 종류를 직접 지정하세요.",
                });
            }
            return list;
        }
    }
}
