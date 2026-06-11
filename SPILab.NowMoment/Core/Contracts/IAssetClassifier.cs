// ════════════════════════════════════════════════════════════════════
// IAssetClassifier.cs — NowMoment v4.1 (Core 분리 / Phase 1)
//
// 자산 자동 분류 휴리스틱(분류 규칙·신뢰도 임계값)을 추상화하는 계약.
// 개선 개발계획서 2.2 의 C4 (분류 휴리스틱) 분리 대상.
//
// Shell 의 FolderImportViewModel 은 이 인터페이스만 참조하며,
// 실제 임계값(conf >= 0.90 / 0.85 / 0.70 등)을 담은 구현체는
// SPILab Core 가 보유한다. Core 미탑재 시 PassthroughClassifier 가
// 모든 항목을 낮은 신뢰도의 '미분류' 후보로만 만들어 폴백한다.
// ════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services.Import;  // ScannedItem

namespace SPILab.NowMoment.Core.Contracts
{
    public interface IAssetClassifier
    {
        /// <summary>Core 분류기가 활성화되어 있는지. false 면 폴백 분류기.</summary>
        bool IsAvailable { get; }

        /// <summary>FolderScanner 가 수집한 항목들을 자산 후보로 분류한다.</summary>
        List<ImportCandidate> Classify(List<ScannedItem> items);
    }
}
