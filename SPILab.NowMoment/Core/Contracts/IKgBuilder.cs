// ════════════════════════════════════════════════════════════════════
// IKgBuilder.cs — NowMoment v4.1  (Core 분리 / Phase 1)
//
// 개선 개발계획서 3.3 "인터페이스 계약" 구현.
//
// 목적:
//   Shell(NowMoment) 과 SPILab Core(물리 규칙 엔진 + KG 빌더) 사이의
//   유일한 결합 계약. Shell 은 이 인터페이스만 알고, 실제 구현체
//   (Python 빌더를 실행하는 PhysicsKgBuilder) 는 런타임에 Provider 가
//   주입한다. Core 가 없으면 NullKgBuilder 가 대신 주입된다.
//
// 보안 효과:
//   이 파일에는 "어떤 룰이 있는지 / 수식이 무엇인지" 가 전혀 없다.
//   순수 인터페이스이므로 외부 배포본에 포함되어도 IP 노출이 아니다.
// ════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SPILab.NowMoment.Core.Contracts
{
    /// <summary>
    /// KG 빌더 계약. 물리 규칙 엔진(.cs) 을 정적 분석하여 KG(JSON-LD/TTL) 를
    /// 산출하는 능력을 추상화한다. 실제 IP(167 룰·수식·인용)는 구현체가 보유.
    /// </summary>
    public interface IKgBuilder
    {
        /// <summary>이 빌더가 처리 가능한 도메인 코드 목록 (예: cs, photo, cmp, etch, thinfilm).</summary>
        IReadOnlyList<string> SupportedDomains { get; }

        /// <summary>
        /// Core 가 정상 활성화되어 실제 빌드가 가능한지 여부.
        /// false 면 NullKgBuilder(기능 비활성 폴백) 가 주입된 상태.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Core 미가용 시 UI 에 표시할 사유 (없으면 빈 문자열).</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// KG 빌드 실행. 1단계(build) 또는 2단계(dump→build) 는 구현체가 판단한다.
        /// </summary>
        Task<BuildResult> BuildAsync(
            BuildRequest request,
            System.IProgress<string>? progress = null,
            CancellationToken ct = default);
    }
}
