// ════════════════════════════════════════════════════════════════════
// NullKgBuilder.cs — NowMoment v4.1 (Core 분리 / Phase 2)
//
// 개선 개발계획서 3.4 의 폴백 구현.
//
// SPILab Core(kg_builder/ Python 페이로드)가 없거나 Secure-Verify 인증을
// 통과하지 못한 경우, Shell 의 DI 가 이 구현을 IKgBuilder 로 주입한다.
//
// 동작: 어떤 빌드 요청도 수행하지 않고 "기능 비활성" 결과를 반환한다.
// 효과: 외부 배포본(Core 제외)에서도 앱이 정상 실행되며, KG 빌드 버튼만
//       비활성/안내 상태가 된다. 자산관리·백업·내보내기는 영향 없음.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SPILab.NowMoment.Core.Contracts;

namespace SPILab.NowMoment.Core.Provider
{
    public sealed class NullKgBuilder : IKgBuilder
    {
        private readonly string _reason;

        public NullKgBuilder(string reason)
        {
            _reason = string.IsNullOrWhiteSpace(reason)
                ? "SPILab Core 가 설치되어 있지 않거나 보안 인증을 통과하지 못했습니다."
                : reason;
        }

        public IReadOnlyList<string> SupportedDomains => Array.Empty<string>();

        public bool IsAvailable => false;

        public string UnavailableReason => _reason;

        public Task<BuildResult> BuildAsync(
            BuildRequest request,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report("[Core 비활성] " + _reason);
            return Task.FromResult(BuildResult.SkippedCore(_reason));
        }
    }
}
