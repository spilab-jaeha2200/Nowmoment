using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SPILab.NowMoment
{
    public partial class App : Application
    {
        // 메인 윈도우가 한 번이라도 뜬 적 있는지. 뜨기 전 예외면 프로세스를 강제 종료한다.
        private bool _mainWindowShown = false;

        public App()
        {
            this.Activated   += OnAppActivated;
            this.Startup     += OnAppStartup;

            this.DispatcherUnhandledException        += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
                (s, e) => { LogException(e.Exception, "TaskUnobserved"); e.SetObserved(); };
        }

        /// <summary>
        /// 시작 시 user_setting 테이블의 'general/theme' 값을 읽어 즉시 적용한다.
        /// DB 접근 실패하면 그냥 Light 로 fallback (App.xaml 의 기본 팔레트가 이미 라이트).
        /// </summary>
        private void OnAppStartup(object sender, StartupEventArgs e)
        {
            try
            {
                var db = new Services.DatabaseService();
                var theme = db.GetSetting("general", "theme", "Light");
                Services.ThemeManager.Apply(theme);
            }
            catch (Exception ex)
            {
                LogException(ex, "ThemeStartup");
                // 실패해도 라이트 기본값이 이미 적용된 상태이므로 화면은 정상 표시됨
            }
        }

        /// <summary>
        /// 메인 윈도우가 표시되어 활성화된 직후 1회만 호출된다.
        ///
        /// ★ v4.1: SPILab Core 초기화(개선계획서 6.2 / Phase 3)를 여기서 한다.
        ///   Secure-Verify 인증 대화상자는 모달(ShowDialog)로 표시되는데,
        ///   메인 창보다 먼저 띄우면 — 사용자가 [취소]로 그 창을 닫는 순간
        ///   앱에 열린 창이 없어져 ShutdownMode(OnLastWindowClose) 에 의해
        ///   프로세스가 종료된다. 따라서 메인 창이 뜬 *뒤* 로 초기화를 미뤄
        ///   인증을 취소해도 앱이 계속 실행되도록 한다(계획서 3.2 — Core
        ///   미인증 시 Shell 정상 동작).
        /// </summary>
        private void OnAppActivated(object sender, EventArgs e)
        {
            if (_mainWindowShown) return;   // 1회만
            _mainWindowShown = true;

            try
            {
                var db = new Services.DatabaseService();

                // ── v4.1: SPILab Core Provider 초기화 ──
                //   Core 페이로드(kg_builder) 탐지 + Secure-Verify 게이트 구성.
                //   인증 취소·실패 시 NullKgBuilder/AssetClassifierFallback 로
                //   폴백되어 앱은 정상 동작하고 KG 빌드·자동분류만 비활성화된다.
                try
                {
                    var audit = new Services.AuditService(db);

                    // Phase 3 작업 3·4 — Secure-Verify 백엔드 구성.
                    //   인증·인가·무결성검증·키발급을 담당. SSO 어댑터 미주입 시
                    //   로컬 PBKDF2 비밀번호 검증으로 동작한다(계획서 8장).
                    var backend = BuildSecureVerifyBackend(db);

                    Core.Provider.CoreServices.Initialize(
                        auditSink: audit.CreateCoreAuditSink(),
                        backend:   backend);

                    // ── v4.1: Secure-Verify 인증 결과 메시지 ──
                    //   Initialize 과정에서 인증 대화상자가 표시되고 Unlock()
                    //   까지 수행된다. 결과를 세 갈래로 안내한다:
                    //     · 성공(Granted)         → 인증 완료 메시지
                    //     · 사용자 취소           → 메시지 없음 (의도적 건너뜀)
                    //     · 그 외 실패            → 인증 실패 메시지 + 사유
                    //   어느 경우든 앱은 계속 실행되며, 실패·취소 시 Core
                    //   기능(KG 빌더·자동분류)만 비활성 상태가 된다.
                    var session = Core.Provider.CoreServices.Gate?.Current;
                    if (session != null && session.Granted)
                    {
                        MessageBox.Show(
                            $"Secure-Verify 인증이 완료되었습니다.\n\n"
                            + $"사용자: {session.Actor}\n"
                            + $"권한: {session.Role}\n\n"
                            + "Core 기능(KG 빌더·자동분류)을 사용할 수 있습니다.",
                            "Secure-Verify 인증 완료",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else if (session != null
                             && !session.DenyReason.Contains("취소"))
                    {
                        // 사용자가 [취소] 한 경우(DenyReason 에 '취소' 포함)는
                        // 의도적 건너뜀이므로 메시지를 띄우지 않는다.
                        MessageBox.Show(
                            $"Secure-Verify 인증에 실패하였습니다.\n\n"
                            + $"사유: {session.DenyReason}\n\n"
                            + "Core 기능(KG 빌더·자동분류)은 비활성 상태이며, "
                            + "그 외 기능은 정상적으로 사용할 수 있습니다.\n"
                            + "사번·비밀번호를 확인한 뒤 프로그램을 다시 실행해 "
                            + "인증하거나 권한 관리자에게 문의하십시오.",
                            "Secure-Verify 인증 실패",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception cex)
                {
                    LogException(cex, "CoreInit");
                    // Core 초기화 실패해도 Shell 기능은 계속 사용 가능 (잠금 폴백)
                    Core.Provider.CoreServices.Initialize();
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "CoreActivate");
                // 초기화 자체가 실패해도 앱은 계속 실행 — Core 만 비활성
                try { Core.Provider.CoreServices.Initialize(); } catch { }
            }
        }

        /// <summary>
        /// Secure-Verify 백엔드를 구성한다 (Phase 3 작업 3·4).
        ///
        /// 권한 매트릭스·.spc 번들·검증 공개키는 %APPDATA%\SPILab\NowMoment
        /// 아래에 둔다. 파일이 없으면 해당 검증 단계는 건너뛰며, 권한
        /// 매트릭스가 없으면 모든 사용자가 Shell-Only 로 거부된다(안전 폴백).
        ///
        /// SSO 어댑터(ISsoProvider)는 미주입 — 로컬 PBKDF2 비밀번호 검증으로
        /// 동작한다. 조직 SSO 확정 시 이 메서드에서 sso: 인자로 주입하면
        /// 인증 단계만 SSO 로 교체된다(계획서 8장 "백엔드만 교체").
        /// </summary>
        private static Core.Security.ISecureVerifyBackend BuildSecureVerifyBackend(
            Services.DatabaseService db)
        {
            string baseDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SPILab", "NowMoment");

            var options = new Core.Security.SecureVerifyOptions
            {
                AccessMatrixPath = System.IO.Path.Combine(baseDir, "core_access.json"),
                SpcBundlePath    = System.IO.Path.Combine(baseDir, "SPILab.Core.spc"),
                VerifyKeyPath    = System.IO.Path.Combine(baseDir, "sign_ed25519.pub"),
                RequireSecondFactor = false,   // 조직 정책에 따라 true 로
            };

            // 인증 대화상자 — ICredentialPrompt 구현. UI 스레드에서 모달 표시.
            var prompt = new Views.SecureVerifyDialog();

            // Phase 3 작업 5 — 무결성 검증·키 저장소 어댑터.
            //   bundleVerifier : build_spc.py verify 를 외부 프로세스로 호출.
            //   keyVault       : DPAPI 보호 키를 세션 단위로 발급/폐기.
            //   관련 도구·키 파일이 없으면 각 어댑터가 검증 단계에서
            //   "실패" 를 반환하므로, Core 활성화가 안전하게 거부된다.
            // ★ v4.1: 번들 무결성 검증용 Python 실행 파일 경로 해석.
            //   "python" 만 쓰면 Windows 가 Microsoft Store 의 빈 python
            //   스텁을 잡아, 검증이 "Python" 한 줄만 출력하고 실패한다
            //   (DenyReason='번들 무결성 검증 실패: Python'). 설정 화면의
            //   Python 경로(user_setting: python/path)를 우선 쓰고, 없으면
            //   흔한 설치 경로를 탐색한다.
            string pythonExe = ResolvePythonExe(db);

            string pipelineDir = System.IO.Path.Combine(baseDir, "build_pipeline");
            var bundleVerifier = new Core.Provider.SpcBundleVerifier(
                pythonExe:      pythonExe,
                buildSpcScript: System.IO.Path.Combine(pipelineDir, "build_spc.py"),
                bundleKeyPath:  System.IO.Path.Combine(baseDir, "bundle.key.dpapi"));
            var keyVault = new Core.Provider.DpapiCoreKeyVault(
                protectedKeyPath: System.IO.Path.Combine(baseDir, "core.key.dpapi"));

            return new Core.Security.LocalSecureVerifyBackend(
                options,
                credentialPrompt: prompt,
                sso:            null,            // ← 조직 SSO 어댑터 주입 지점
                secondFactor:   null,            // ← 2FA 검증기 주입 지점
                bundleVerifier: bundleVerifier,
                keyVault:       keyVault);
        }

        /// <summary>
        /// 번들 검증·KG 빌드에 쓸 Python 실행 파일 경로를 해석한다.
        ///
        /// 우선순위:
        ///   1) 설정 화면에서 지정한 경로 (user_setting: python/path)
        ///   2) 흔한 Anaconda / 표준 Python 설치 경로 탐색
        ///   3) 위 모두 실패 시 "python" (PATH 의존 — 최후 폴백)
        ///
        /// "python" 만 쓰면 Microsoft Store 의 빈 스텁이 잡혀 검증이
        /// 실패하므로, 실제 python.exe 절대경로를 찾는 것이 핵심이다.
        /// </summary>
        private static string ResolvePythonExe(Services.DatabaseService db)
        {
            // 1) 설정 화면에서 사용자가 지정한 경로
            try
            {
                string configured = db.GetSetting("python", "path", "");
                if (!string.IsNullOrWhiteSpace(configured)
                    && System.IO.File.Exists(configured))
                    return configured;
            }
            catch { /* 설정 조회 실패 시 아래 탐색으로 진행 */ }

            // 2) 흔한 설치 경로 탐색
            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            foreach (var cand in new[]
            {
                System.IO.Path.Combine(programData, "anaconda3", "python.exe"),
                System.IO.Path.Combine(userProfile, "anaconda3", "python.exe"),
                System.IO.Path.Combine(programData, "miniconda3", "python.exe"),
                System.IO.Path.Combine(userProfile, "miniconda3", "python.exe"),
                System.IO.Path.Combine(localAppData,
                    "Programs", "Python", "Python312", "python.exe"),
                System.IO.Path.Combine(localAppData,
                    "Programs", "Python", "Python311", "python.exe"),
            })
            {
                if (System.IO.File.Exists(cand))
                    return cand;
            }

            // 3) 최후 폴백 — PATH 에 의존 (Store 스텁이 잡힐 수 있음)
            return "python";
        }


        // ════════════════════════════════════════════════════════════════
        // v4.0 정책 (Phase 5+):
        //   기본 실행 → MainWindow_v4 (라이트 톤 NavigationView)
        //   App.xaml 의 StartupUri="Views/MainWindow_v4.xaml" 가 직접 띄우므로
        //   OnStartup 별도 분기는 필요 없다. 명령줄 인자(--v3/--v4)·환경변수
        //   (NOWMOMENT_V3/NOWMOMENT_V4) 분기는 모두 제거됨.
        //
        //   다크 모드는 v3 화면을 띄우는 방식이 아니라 v4 화면에 ThemeManager
        //   를 통해 다크 팔레트를 적용하는 방식으로 처리한다 (SettingsDialog
        //   의 "테마: 라이트/다크/시스템" 콤보 → ThemeManager.Current).
        // ════════════════════════════════════════════════════════════════

        private void OnDispatcherUnhandledException(
            object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "Dispatcher");
            ShowError(e.Exception);

            if (!_mainWindowShown)
            {
                // 메인 윈도우가 한 번도 뜨지 못한 채 발생한 예외 →
                // Handled=true 로 두면 좀비 프로세스가 된다. 즉시 종료.
                e.Handled = true;
                Shutdown(1);
                Environment.Exit(1); // 마지막 안전망
            }
            else
            {
                // 정상 운영 중 예외는 살린다 (사용자가 작업 중인 데이터 보호).
                e.Handled = true;
            }
        }

        private void OnDomainUnhandledException(
            object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException(ex, "Domain");
                ShowError(ex);
            }
            // IsTerminating == true 면 어차피 CLR 이 종료시킴. 추가 작업 불필요.
        }

        private static void ShowError(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    ex.ToString(),
                    "NowMoment — 처리되지 않은 예외",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* MessageBox 도 못 띄우는 상황이면 포기 */ }
        }

        private static void LogException(Exception ex, string source)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SPILab", "NowMoment");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              "  [" + source + "] ===");
                sb.AppendLine(ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
