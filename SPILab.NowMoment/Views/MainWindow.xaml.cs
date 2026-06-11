// ════════════════════════════════════════════════════════════════════
// MainWindow.xaml.cs — KG 와이어업 (v2.3 hotfix)
//
// 변경점 (이전 버전 대비):
//   * KG 서비스 생성 ~ 시드 임포트 전체를 try-catch 로 감쌌다.
//     KG 모듈이 실패해도 NowMoment 본체는 정상적으로 뜨도록 한다.
//   * 실패 시 %APPDATA%\SPILab\NowMoment\crash.log 에 원인을 기록한다.
//   * App.xaml.cs 의 글로벌 예외 핸들러와 함께, MainWindow 가 절대로
//     "조용히 죽는" 일이 없게 만든다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using SPILab.NowMoment.Services;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 1) 기존 DB 서비스 — 여기서 실패하면 NowMoment 자체가 못 돌아가므로
            //    예외는 그대로 위로 던진다 (App.xaml.cs 의 글로벌 핸들러가 처리).
            var db = new DatabaseService();

            // 2) 메인 VM 먼저 생성. KG 가 실패해도 본체는 뜨도록.
            var main = new MainViewModel(db);

            // v4 Phase 3: TTL Studio 자동 영속화 부착 + 마지막 스냅샷 복원
            try { main.TtlStudio.AttachDatabase(db); }
            catch (Exception ttlEx) { LogKgFailure(ttlEx); /* 본체 동작에 영향 X */ }

            // 3) KG 모듈 — 전부 try-catch. 어떤 예외가 나도 본체는 산다.
            try
            {
                var kg = new KnowledgeGraphService(db.DbPath);
                main.AttachKg(kg);

                // [v2.4] 빌더 설정 부착 — kg_settings 테이블에서 src 경로 로드
                var kgSettings = new KgSettingsService(db.DbPath);
                main.Kg!.AttachBuilder(kgSettings);

                // [v2.7] 도메인 등록부 부착 — kg_domain 테이블 시드 + Kg.Domains 채움
                var kgDomains = new KgDomainService(db.DbPath);
                main.Kg!.AttachDomains(kgDomains);

                // 자동 시드 — 도메인별로 각각 시드 (cs / photo).
                TrySeedDomain(kg, "kg_raypann_cs.json",    KnowledgeGraphService.DOMAIN_CS);
                TrySeedDomain(kg, "kg_raypann_photo.json", KnowledgeGraphService.DOMAIN_PHOTO);
                main.Kg!.Reload();
            }
            catch (Exception ex)
            {
                LogKgFailure(ex);
                // 사용자에게는 알리되 앱은 계속 동작
                MessageBox.Show(
                    "KG 모듈 초기화에 실패했습니다.\n" +
                    "NowMoment 는 정상 동작하지만 KG 탭은 사용할 수 없습니다.\n\n" +
                    "원인:\n" + ex.Message + "\n\n" +
                    "상세 로그: %APPDATA%\\SPILab\\NowMoment\\crash.log",
                    "KG 초기화 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DataContext = main;
        }

        /// <summary>
        /// 해당 도메인의 KG 가 비어있고, 시드 JSON 파일이 발견되면 임포트.
        /// </summary>
        private static void TrySeedDomain(KnowledgeGraphService kg, string jsonName, string domain)
        {
            try
            {
                if (kg.GetStats("", domain).Nodes > 0) return;  // 이미 있음 — skip
                var seed = ResolveKgSeedPath(jsonName);
                if (seed != null && File.Exists(seed))
                    kg.ImportFromJson(seed, domain);
            }
            catch
            {
                // 자동 시드 실패는 무시 — 사용자가 수동으로 임포트 가능
            }
        }

        /// <summary>
        /// KG 임포트용 JSON 파일 경로를 해석한다.
        /// 우선순위:
        ///   1) %APPDATA%\SPILab\NowMoment\kg_builder\<fileName>   ← 사용자가 빌드한 결과
        ///   2) <project_root>/kg_builder/<fileName>               ← 개발 환경 빌더 기본 출력
        ///   3) <project_root>/<fileName>                          ← 루트 직접 배치
        ///   4) BaseDirectory                                      ← 배포 환경 폴백
        /// </summary>
        private static string? ResolveKgSeedPath(string fileName)
        {
            // 1) 데이터 폴더 (%APPDATA%) — 인스톨 환경에서 KG 빌드 결과 위치
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SPILab", "NowMoment", "kg_builder", fileName);
            if (File.Exists(appData)) return appData;

            // 2) 개발 환경 — .csproj 가 있는 트리에서 kg_builder/<file>
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (!dir.EnumerateFiles("*.csproj").Any()) continue;

                var inBuilder = Path.Combine(dir.FullName, "kg_builder", fileName);
                if (File.Exists(inBuilder)) return inBuilder;

                var inRoot = Path.Combine(dir.FullName, fileName);
                if (File.Exists(inRoot)) return inRoot;

                break; // .csproj 찾았으면 그 트리는 종료
            }
            // 3) BaseDirectory 폴백
            var fallback = Path.Combine(baseDir, fileName);
            return File.Exists(fallback) ? fallback : null;
        }

        private static void LogKgFailure(Exception ex)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SPILab", "NowMoment");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [KG] ===");
                sb.AppendLine(ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { /* 로그 실패 무시 */ }
        }
    }
}
