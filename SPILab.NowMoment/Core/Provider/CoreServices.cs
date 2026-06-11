// ════════════════════════════════════════════════════════════════════
// CoreServices.cs — NowMoment v4.1 (Core 분리 / Phase 2)
//
// Core Provider 의 전역 접근점.
//
// NowMoment v4.0 에는 DI 컨테이너가 실제로 배선되어 있지 않고(서비스가
// 'new' 로 직접 생성됨), ViewModel 들이 XAML/StartupUri 로 생성되어
// 생성자 주입이 어렵다. 따라서 v4.1 Phase 2 에서는 본격 DI 컨테이너
// 도입 대신, App 시작 시 1회 초기화하는 가벼운 서비스 로케이터를 둔다.
//
// 이는 개선 개발계획서 6.2 의 "Provider 로더" 를 최소 침습으로 적용하기
// 위한 선택이며, 추후 DI 컨테이너 전면 도입 시 Initialize() 한 곳만
// 컨테이너 등록으로 교체하면 된다.
//
// 사용:
//   App.OnAppStartup 에서  CoreServices.Initialize(auditSink);
//   이후 어디서나        CoreServices.KgBuilder / .Classifier 로 접근.
// ════════════════════════════════════════════════════════════════════
using System;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Core.Security;

namespace SPILab.NowMoment.Core.Provider
{
    public static class CoreServices
    {
        private static CoreProvider? _provider;

        /// <summary>App 시작 시 1회 호출. 중복 호출은 무시된다.</summary>
        public static void Initialize(
            Action<string, string, string>? auditSink = null,
            ISecureVerifyBackend? backend = null)
        {
            if (_provider != null) return;
            _provider = CoreProviderLoader.Load(auditSink, backend);
        }

        /// <summary>초기화 전 접근 시에도 안전하도록 폴백 Provider 를 보장.</summary>
        private static CoreProvider Provider
        {
            get
            {
                if (_provider == null)
                {
                    // Initialize 가 누락된 경우의 안전망 — 잠금 상태로 폴백
                    _provider = CoreProviderLoader.Load();
                }
                return _provider;
            }
        }

        public static IKgBuilder KgBuilder => Provider.KgBuilder;
        public static IAssetClassifier Classifier => Provider.Classifier;
        public static SecureVerifyGate Gate => Provider.Gate;

        /// <summary>Core 기능이 실제로 활성화되어 있는지.</summary>
        public static bool CoreActive => Provider.CoreActive;

        /// <summary>UI 상태 표시용 한 줄 요약.</summary>
        public static string StatusLine => Provider.StatusLine;
    }
}
