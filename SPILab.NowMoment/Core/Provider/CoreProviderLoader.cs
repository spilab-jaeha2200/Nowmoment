// ════════════════════════════════════════════════════════════════════
// CoreProviderLoader.cs — NowMoment v4.1 (Core 분리 / Phase 2)
//
// 개선 개발계획서 3.4 "Core 로드 방식 — Provider 패턴" 의 핵심 구현.
//
// 역할:
//   앱 시작 시 SPILab Core 페이로드(kg_builder/ 폴더)의 존재를 탐지하고,
//   IKgBuilder / IAssetClassifier 구현을 결정한다.
//
//   • Core 페이로드 발견 + Secure-Verify 통과 → PhysicsKgBuilder / AssetClassifierCore
//   • 그 외                                  → NullKgBuilder / AssetClassifierFallback
//
// 효과 (계획서 3.2):
//   Core 가 없어도 Shell 은 정상 빌드·실행된다. 외부 배포본은 kg_builder/
//   폴더를 패키징에서 제외하기만 하면 되고, 코드 분기는 불필요하다.
//
// ★ 본 로더는 어셈블리 동적 로드(Assembly.LoadFrom)가 아닌 "페이로드 폴더
//   존재 여부" 로 Core 유무를 판정한다. NowMoment 의 진짜 IP 는 .NET
//   어셈블리가 아니라 kg_builder/ 의 Python 빌더이기 때문이다(계획서 2.2).
//   Phase 3 에서 이 폴더가 .spc 암호화 번들로 대체되면, 판정 로직만
//   IsCorePayloadPresent() 안에서 교체하면 된다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Core.Security;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.Core.Provider
{
    /// <summary>Core 탐지·주입 결과 묶음.</summary>
    public sealed class CoreProvider
    {
        public IKgBuilder KgBuilder { get; init; } = null!;
        public IAssetClassifier Classifier { get; init; } = null!;
        public SecureVerifyGate Gate { get; init; } = null!;

        /// <summary>Core 페이로드가 탐지되었는지 (인증 성공과는 별개).</summary>
        public bool CorePayloadPresent { get; init; }

        /// <summary>실제로 Core 기능이 활성화되었는지 (페이로드 + 인증 모두 통과).</summary>
        public bool CoreActive => KgBuilder.IsAvailable;

        /// <summary>UI 상태바·진단용 한 줄 요약.</summary>
        public string StatusLine => CoreActive
            ? "SPILab Core: 활성 (KG 빌드·자동 분류 사용 가능)"
            : "SPILab Core: 비활성 — " +
              (CorePayloadPresent
                  ? "보안 인증 미통과"
                  : "페이로드 없음 (외부 배포본)");
    }

    public static class CoreProviderLoader
    {
        /// <summary>
        /// Core 를 탐지하고 Provider 를 구성한다. App 시작 시 1회 호출.
        /// </summary>
        /// <param name="auditSink">감사 로그 적재 콜백 (action, actorRole, detail).</param>
        /// <param name="backend">Phase 3 Secure-Verify 백엔드. null 이면 기본 정책.</param>
        public static CoreProvider Load(
            Action<string, string, string>? auditSink = null,
            ISecureVerifyBackend? backend = null)
        {
            var gate = new SecureVerifyGate(backend, auditSink);

            bool present = IsCorePayloadPresent();

            if (!present)
            {
                // 외부 배포본 — 페이로드가 아예 없음. 인증 시도조차 하지 않는다.
                const string reason =
                    "이 배포본에는 SPILab Core 페이로드(kg_builder)가 포함되지 않았습니다. " +
                    "KG 빌드·자동 분류는 사용할 수 없습니다.";
                auditSink?.Invoke("core.absent", "ShellOnly", "payload not found");
                return new CoreProvider
                {
                    Gate               = gate,
                    KgBuilder          = new NullKgBuilder(reason),
                    Classifier         = new Services.Import.AssetClassifierFallback(),
                    CorePayloadPresent = false,
                };
            }

            // 페이로드 존재 — Secure-Verify 게이트로 인가 시도 (계획서 5.1)
            var session = gate.Unlock();
            if (!session.Granted)
            {
                return new CoreProvider
                {
                    Gate               = gate,
                    KgBuilder          = new NullKgBuilder(
                        "Core 페이로드는 존재하나 보안 인증을 통과하지 못했습니다: " + session.DenyReason),
                    Classifier         = new Services.Import.AssetClassifierFallback(),
                    CorePayloadPresent = true,
                };
            }

            // 인가 성공 — 실제 Core 구현 주입
            return new CoreProvider
            {
                Gate               = gate,
                KgBuilder          = new PhysicsKgBuilder(gate, auditSink),
                Classifier         = CreateCoreClassifier(),
                CorePayloadPresent = true,
            };
        }

        /// <summary>
        /// 인가 성공 시 사용할 분류기를 만든다.
        ///
        /// ★ Phase 2 마무리 (계획서 2.2 C4):
        ///   분류 휴리스틱 본체(AssetClassifierCore)는 Core 페이로드
        ///   폴더(kg_builder/classifier/)로 이전했다. 외부 배포본은
        ///   그 폴더가 없어 컴파일에 포함되지 않으므로, 타입을 직접
        ///   참조할 수 없다 → MSBuild 심볼 CORE_PAYLOAD 로 분기한다.
        ///
        ///   • 내부 빌드(csproj 가 kg_builder/classifier/*.cs 포함)
        ///       → CORE_PAYLOAD 정의됨 → AssetClassifierCore (full 휴리스틱)
        ///   • 외부 빌드(kg_builder/ 제거됨)
        ///       → 심볼 없음 → AssetClassifierFallback (확장자 기반)
        ///
        ///   단, 외부 배포본은 그 이전에 IsCorePayloadPresent()=false 로
        ///   이미 NullKgBuilder/AssetClassifierFallback 경로를 타므로
        ///   이 메서드 자체가 호출되지 않는다. #else 가지는 "내부
        ///   소스인데 classifier 폴더만 빠진" 경계 상황의 안전망이다.
        /// </summary>
        private static IAssetClassifier CreateCoreClassifier()
        {
#if CORE_PAYLOAD
            return new SPILab.NowMoment.Core.ClassifierCore.AssetClassifierCore(
                new Services.Import.ClassifierUtil());
#else
            return new Services.Import.AssetClassifierFallback();
#endif
        }

        /// <summary>
        /// Core 페이로드 탐지. v4.1(Phase 2): kg_builder/ 폴더 + 빌더 .py 존재로 판정.
        /// Phase 3: 이 메서드만 .spc 번들 탐지·서명검증으로 교체하면 된다.
        /// </summary>
        public static bool IsCorePayloadPresent()
        {
            var baseDir = AppContext.BaseDirectory;

            // 개발 환경: .csproj 트리 위로 kg_builder 탐색
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                bool hasCsproj;
                try { hasCsproj = dir.EnumerateFiles("*.csproj").Any(); }
                catch { hasCsproj = false; }
                if (hasCsproj)
                    return HasBuilderScripts(Path.Combine(dir.FullName, "kg_builder"));
            }

            // 설치본: 실행파일 폴더의 kg_builder
            return HasBuilderScripts(Path.Combine(baseDir, "kg_builder"));
        }

        private static bool HasBuilderScripts(string kgBuilderDir)
        {
            if (!Directory.Exists(kgBuilderDir)) return false;
            // 빌더가 하나라도 있으면 페이로드 존재로 본다
            try
            {
                return Directory.EnumerateFiles(kgBuilderDir, "build_kg_*.py").Any();
            }
            catch { return false; }
        }
    }
}
