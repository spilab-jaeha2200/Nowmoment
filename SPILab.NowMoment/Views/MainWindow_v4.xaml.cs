// ════════════════════════════════════════════════════════════════════
// MainWindow_v4.xaml.cs — v4 Phase 4
//
// NavigationView 사이드바의 ListBoxItem 클릭에 따라 ContentHost 에 적절한
// UserControl 을 로드. 같은 화면을 반복 클릭해도 인스턴스를 캐싱해서
// 상태가 보존되도록 한다.
//
// 처리 대상 (Tag 값):
//   dashboard / asset_code / asset_model / asset_document / asset_patent /
//   asset_experiment / project_tag / audit_log /
//   kg_main / kg_builder / kg_import / kg_graph / asset_kg_link / ttl_studio / kg_domain /
//   folder_import / db_backup / pdf_catalog / excel_export
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SPILab.NowMoment.Services;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class MainWindow_v4 : Window
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel   _main;
        private readonly Dictionary<string, FrameworkElement> _cache = new();
        private bool _navigating;  // SelectionChanged 다른 ListBox 가 트리거할 때 재진입 방지

        public MainWindow_v4()
        {
            InitializeComponent();

            _db = new DatabaseService();
            _main = new MainViewModel(_db);

            // TTL 자동 영속화
            try { _main.TtlStudio.AttachDatabase(_db); }
            catch (Exception ex) { Log(ex, "TtlAttach"); }

            // KG 모듈 부착 (v3 와 동일 패턴)
            try
            {
                var kg = new KnowledgeGraphService(_db.DbPath);
                _main.AttachKg(kg);
                var kgSettings = new KgSettingsService(_db.DbPath);
                _main.Kg!.AttachBuilder(kgSettings);
                var kgDomains = new KgDomainService(_db.DbPath);
                _main.Kg!.AttachDomains(kgDomains);
                _main.Kg!.Reload();
            }
            catch (Exception ex)
            {
                Log(ex, "KgAttach");
                MessageBox.Show(
                    "KG 모듈 초기화에 실패했습니다.\n" + ex.Message,
                    "KG 초기화 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            DataContext = _main;

            // 시작 시 대시보드를 자동 선택 — Loaded 시점에 해야 NavTop 의 ItemContainer 가
            // 모두 생성된 상태에서 SelectionChanged 가 정상 발화한다.
            this.Loaded += (_, __) =>
            {
                try
                {
                    if (NavTop != null && NavTop.Items.Count > 0 && NavTop.SelectedIndex < 0)
                        NavTop.SelectedIndex = 0;  // = dashboard
                }
                catch (Exception ex) { Log(ex, "InitialNavSelect"); }
            };

            // WindowStyle=None 상태에서 최대화 시 작업표시줄을 덮지 않도록
            // 현재 모니터의 작업영역(WorkArea)으로 크기를 제한한다.
            this.StateChanged += (_, __) =>
            {
                if (WindowState == WindowState.Maximized)
                {
                    var wa = SystemParameters.WorkArea;
                    MaxHeight = wa.Height + 8;   // 8px = None 모드 보정값
                    MaxWidth  = wa.Width  + 8;
                }
                else
                {
                    MaxHeight = double.PositiveInfinity;
                    MaxWidth  = double.PositiveInfinity;
                }
            };
        }

        // ── 커스텀 타이틀바: 헤더 드래그로 창 이동 ─────────
        private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            // 더블클릭 시 최대화/복원 토글
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); }
            catch { /* 드래그 중 상태 전환 등 예외 무시 */ }
        }

        // ── 커스텀 타이틀바: 최소화 / 최대화 / 닫기 ────────
        private void MinBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            // 최대화 아이콘 ↔ 복원 아이콘 전환 (Segoe MDL2 Assets)
            if (MaxBtn != null)
                MaxBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ── NavigationView SelectionChanged ─────────────
        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_navigating) return;
            if (sender is not ListBox lb || lb.SelectedItem is not ListBoxItem item) return;
            var tag = item.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;

            _navigating = true;
            try
            {
                // 다른 ListBox 의 선택 해제 (한 군데만 선택되도록)
                if (lb != NavTop)    NavTop.SelectedItem    = null;
                if (lb != NavAssets) NavAssets.SelectedItem = null;
                if (lb != NavKg)     NavKg.SelectedItem     = null;
                if (lb != NavTools)  NavTools.SelectedItem  = null;
            }
            finally { _navigating = false; }

            Navigate(tag);
        }

        private void Navigate(string tag)
        {
            // WelcomePanel 은 XAML 에서 제거됨 (시작 시 즉시 대시보드 표시 정책).

            if (_cache.TryGetValue(tag, out var cached))
            {
                ContentHost.Content = cached;
                return;
            }

            FrameworkElement? view = tag switch
            {
                "dashboard"        => CreateDashboard(),
                "audit_log"        => CreateAuditLog(),
                "project_tag"      => CreateProjectTag(),

                "asset_code"       => CreateAssetCode(),
                "asset_model"      => CreateAssetModel(),
                "asset_document"   => CreateAssetDocument(),
                "asset_patent"     => CreateAssetPatent(),
                "asset_experiment" => CreateAssetExperiment(),

                "kg_main"          => CreateKgNodesEdges(),
                "kg_builder"       => CreateKgBuilder(),
                "kg_import"        => CreateKgImport(),
                "kg_graph"         => CreateKgGraph(),
                "asset_kg_link"    => CreateAssetKgLinkViewer(),
                "ttl_studio"       => CreateTtlStudio(),
                "kg_domain"        => CreateKgDomain(),

                "folder_import"    => CreateFolderImport(),
                "db_backup"        => CreateBackup(),
                "pdf_catalog"      => CreatePdfCatalog(),
                "excel_export"     => CreateExcelExport(),  // 메뉴에서는 제거되었지만 다른 경로 호출 호환용 유지

                "settings"         => CreateSettings(),

                _ => CreatePlaceholder("(미정의)", $"화면 ID '{tag}' 는 아직 구현되지 않았습니다."),
            };
            if (view == null) return;
            _cache[tag] = view;
            ContentHost.Content = view;
        }

        // 우상단 [⚙ 설정] 버튼 → 본문에 설정 화면 표시.
        // 좌측 nav 4개 ListBox 선택을 모두 해제하여 메뉴와 충돌하지 않게 한다.
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            _navigating = true;
            try
            {
                if (NavTop    != null) NavTop.SelectedItem    = null;
                if (NavAssets != null) NavAssets.SelectedItem = null;
                if (NavKg     != null) NavKg.SelectedItem     = null;
                if (NavTools  != null) NavTools.SelectedItem  = null;
            }
            finally { _navigating = false; }

            Navigate("settings");
        }

        // ── 화면 팩토리 ───────────────────────────────────
        private FrameworkElement CreateDashboard()
        {
            // v4 Phase 5: 신규 DashboardView 사용 (상단 4카드 + 도넛 차트 + 최근 활동)
            try
            {
                var vm = new DashboardViewModel(_db, _main.KgService);
                return new DashboardView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "DashboardView");
                return CreatePlaceholder(
                    "⌂ 대시보드",
                    "대시보드 로드 실패:\n" + ex.Message);
            }
        }

        // v4: 설정 화면 — 기존 SettingsDialog(Window) 대신 본문에 임베드되는
        // SettingsView(UserControl) 를 사용. 좌측 카테고리 + 우측 패널 구조 동일.
        private FrameworkElement CreateSettings()
        {
            try
            {
                var vm = new SettingsViewModel(_db, _main.Audit);

                // 저장(true) / 취소(false) 시 설정 화면을 닫는다.
                // 본문 임베드 화면이므로 "닫기" = 대시보드로 이동.
                // 기본값 복원은 RequestClose 를 호출하지 않으므로 화면이 유지된다.
                vm.RequestClose += _ =>
                {
                    // 다음에 설정을 다시 열 때 DB 의 최신값으로 새로 로드되도록 캐시 제거.
                    _cache.Remove("settings");
                    if (NavTop != null && NavTop.Items.Count > 0)
                        NavTop.SelectedIndex = 0;   // = dashboard
                    else
                        Navigate("dashboard");
                };

                return new SettingsView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "SettingsView");
                return CreatePlaceholder(
                    "⚙ 설정",
                    "설정 화면 로드 실패:\n" + ex.Message);
            }
        }

        private FrameworkElement CreateAuditLog()
        {
            var vm = new AuditLogViewModel(_db);
            return new AuditLogView { DataContext = vm };
        }

        private FrameworkElement CreateProjectTag()
        {
            var vm = new ProjectTagViewModel(_db, _main.Audit);
            return new ProjectTagView { DataContext = vm };
        }

        // v4 Phase 5+: 신규 AssetCodeView 사용 (체크박스 다중선택 + 상세패널 + 필터)
        // CRUD 는 모두 MainViewModel 의 v3 명령(AddCodeCommand/EditCodeCommand/DeleteCodeCommand)에
        // 패스스루되므로 기존 동작·Audit 통합 그대로 유지.
        private FrameworkElement CreateAssetCode()
        {
            try
            {
                var vm = new AssetCodeViewModel(_main);
                return new AssetCodeView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetCodeView");
                return CreatePlaceholder(
                    "📄 소스코드 / 모듈",
                    "소스코드 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 AssetModelView - AssetCode 와 동일 패턴.
        // CRUD 는 MainViewModel.AddModelCommand/EditModelCommand/DeleteModelCommand 로 패스스루.
        private FrameworkElement CreateAssetModel()
        {
            try
            {
                var vm = new AssetModelViewModel(_main);
                return new AssetModelView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetModelView");
                return CreatePlaceholder(
                    "🤖 AI 모델 / 데이터",
                    "AI 모델 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 AssetDocumentView - AssetCode 와 동일 패턴.
        // CRUD 는 MainViewModel.AddDocumentCommand/EditDocumentCommand/DeleteDocumentCommand 로 패스스루.
        private FrameworkElement CreateAssetDocument()
        {
            try
            {
                var vm = new AssetDocumentViewModel(_main);
                return new AssetDocumentView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetDocumentView");
                return CreatePlaceholder(
                    "📜 문서 / 논문",
                    "문서 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 AssetPatentView - AssetCode 와 동일 패턴.
        // CRUD 는 MainViewModel.AddPatentCommand/EditPatentCommand/DeletePatentCommand 로 패스스루.
        private FrameworkElement CreateAssetPatent()
        {
            try
            {
                var vm = new AssetPatentViewModel(_main);
                return new AssetPatentView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetPatentView");
                return CreatePlaceholder(
                    "📑 특허 / IP",
                    "특허 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 AssetExperimentView - AssetCode 와 동일 패턴.
        // CRUD 는 MainViewModel.AddExperimentCommand/EditExperimentCommand/DeleteExperimentCommand 로 패스스루.
        private FrameworkElement CreateAssetExperiment()
        {
            try
            {
                var vm = new AssetExperimentViewModel(_main);
                return new AssetExperimentView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetExperimentView");
                return CreatePlaceholder(
                    "🔬 실험 / 측정",
                    "실험 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 KgBuilderView - KG 빌드 명령/로그/도메인 선택 화면.
        // 모든 빌드 명령은 KgViewModel (v3) 의 명령을 그대로 위임하므로
        // 기존 Python 빌더 프로세스/Audit 흐름 그대로 유지.
        private FrameworkElement CreateKgBuilder()
        {
            try
            {
                if (_main.Kg == null)
                {
                    return CreatePlaceholder(
                        "⚙ KG 빌더",
                        "KG 모듈이 초기화되지 않았습니다.\nApp 시작 시 KG 모듈 로드에 실패한 경우 crash.log 를 확인해 주세요.");
                }
                var vm = new KgBuilderViewModel(_main.Kg);
                return new KgBuilderView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "KgBuilderView");
                return CreatePlaceholder(
                    "⚙ KG 빌더",
                    "KG 빌더 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 KgNodesEdgesView — SCR-B01 KG 노드/엣지 편집.
        // v3 KgViewModel 의 Add/Edit/DeleteNode + Add/DeleteEdge + Refresh 명령을
        // 그대로 위임 (= v3 KgNodeEditDialog / KgEdgeEditDialog 호출).
        // PropsJson 인라인 편집은 KnowledgeGraphService.UpdateNodeProps 사용.
        private FrameworkElement CreateKgNodesEdges()
        {
            try
            {
                if (_main.Kg == null)
                {
                    return CreatePlaceholder(
                        "🕸 KG 노드 / 엣지",
                        "KG 모듈이 초기화되지 않았습니다.\nApp 시작 시 KG 모듈 로드에 실패한 경우 crash.log 를 확인해 주세요.");
                }
                var vm = new KgNodesEdgesViewModel(_main.Kg, _main.KgService);
                return new KgNodesEdgesView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "KgNodesEdgesView");
                return CreatePlaceholder(
                    "🕸 KG 노드 / 엣지",
                    "KG 노드/엣지 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 KgGraphViewerView — SCR-B04 그래프 시각화.
        // v3 KgGraphView (D3-like Canvas 렌더링) 를 임베드하고, 좌측 도메인/타입 필터 +
        // 우측 상세 패널을 새로 결합. v3 KgViewModel 의 Domains/StatsByType/SelectedNode 등을
        // 그대로 위임하며, EditNodeCommand 는 v3 KgNodeEditDialog 호출.
        private FrameworkElement CreateKgGraph()
        {
            try
            {
                if (_main.Kg == null)
                {
                    return CreatePlaceholder(
                        "🕸 그래프 시각화",
                        "KG 모듈이 초기화되지 않았습니다.\nApp 시작 시 KG 모듈 로드에 실패한 경우 crash.log 를 확인해 주세요.");
                }
                var vm = new KgGraphViewerViewModel(_main.Kg);
                return new KgGraphViewerView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "KgGraphViewerView");
                return CreatePlaceholder(
                    "🕸 그래프 시각화",
                    "그래프 시각화 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 TtlStudioPanelView — SCR-B05 TTL Studio (오프라인 패널).
        // v3 TtlStudioViewModel 의 모든 명령/상태를 그대로 위임 (Add/Remove Class/Property/Instance/Triple,
        // SPARQL 실행/지우기, 파일 New/Open/Save/SaveAs). 자동 영속화는 MainWindow_v4 생성자에서
        // TtlStudio.AttachDatabase 가 이미 처리하므로 추가 작업 없음.
        private FrameworkElement CreateTtlStudio()
        {
            try
            {
                var vm = new TtlStudioPanelViewModel(_main.TtlStudio);
                return new TtlStudioPanelView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "TtlStudioPanelView");
                return CreatePlaceholder(
                    "🛠 TTL Studio",
                    "TTL Studio 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 KgImportView — SCR-B02 JSON/TTL 임포트.
        // KnowledgeGraphService.ImportFromFile 위임. 도메인은 KgViewModel.SelectedDomain 사용.
        private FrameworkElement CreateKgImport()
        {
            try
            {
                if (_main.Kg == null || _main.KgService == null)
                {
                    return CreatePlaceholder(
                        "📥 JSON / TTL 임포트",
                        "KG 모듈이 초기화되지 않았습니다.\nApp 시작 시 KG 모듈 로드에 실패한 경우 crash.log 를 확인해 주세요.");
                }
                var vm = new KgImportViewModel(_main.Kg, _main.KgService);
                return new KgImportView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "KgImportView");
                return CreatePlaceholder(
                    "📥 JSON / TTL 임포트",
                    "임포트 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 5+: 신규 AssetKgLinkViewerView — SCR-B03 자산 ↔ KG 링크 관리.
        // 자산 5종(코드/모델/문서/특허/실험) 트리 + 우측에 v3 AssetKgLinkPanelView 임베드.
        // 실제 LinkAsset/UnlinkAsset 은 v3 AssetKgLinkPanelViewModel 내부에서 처리.
        private FrameworkElement CreateAssetKgLinkViewer()
        {
            try
            {
                // Kg 모듈 미초기화 시 — 다른 KG 화면(CreateKgImport/CreateKgDomain)과
                // 동일하게 가드. 이게 없으면 KgService=null 이 그대로 패널까지 전달되어
                // 우측 본문이 빈 화면으로 보인다 (IsAvailable=false → 표/안내 모두 미표시).
                if (_main.Kg == null || _main.KgService == null)
                {
                    return CreatePlaceholder(
                        "🔗 자산 ↔ KG 링크",
                        "KG 모듈이 초기화되지 않았습니다.\nApp 시작 시 KG 모듈 로드에 실패한 경우 crash.log 를 확인해 주세요.");
                }
                var vm = new AssetKgLinkViewerViewModel(_main, _main.KgService);
                return new AssetKgLinkViewerView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "AssetKgLinkViewerView");
                return CreatePlaceholder(
                    "🔗 자산 ↔ KG 링크",
                    "링크 관리 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 9: 신규 KgDomainView — kg_domain CRUD (도메인 추가/편집/삭제).
        private FrameworkElement CreateKgDomain()
        {
            try
            {
                if (_main.Kg == null)
                {
                    return CreatePlaceholder(
                        "🗂 도메인 관리",
                        "KG 모듈이 초기화되지 않았습니다.");
                }
                var vm = new KgDomainViewModel(_main.Kg, _main.KgService);
                return new KgDomainView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "KgDomainView");
                return CreatePlaceholder(
                    "🗂 도메인 관리",
                    "도메인 관리 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 9: 폴더 임포트 — 전용 패널 화면 (FolderImportPanelView).
        private FrameworkElement CreateFolderImport()
        {
            try
            {
                var vm = new FolderImportPanelViewModel(_db);
                return new FolderImportPanelView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "FolderImportPanelView");
                return CreatePlaceholder(
                    "📁 폴더 임포트",
                    "폴더 임포트 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4: DB 백업/복원 — DbBackupRestoreView (백업 활성 + 복원 비활성 2섹션).
        private FrameworkElement CreateBackup()
        {
            try
            {
                var vm = new DbBackupRestoreViewModel(
                    onBackup: () => _main.BackupDbCommand.Execute(null));
                return new DbBackupRestoreView { DataContext = vm };
            }
            catch (Exception ex)
            {
                Log(ex, "DbBackupRestoreView");
                return CreatePlaceholder(
                    "💾 DB 백업 / 복원",
                    "DB 백업/복원 화면 로드 실패:\n" + ex.Message);
            }
        }

        // v4 Phase 9: PDF 카탈로그 — ToolActionView 기반 표준 액션 화면.
        private FrameworkElement CreatePdfCatalog()
        {
            var vm = new ToolActionViewModel(
                icon: "📑",
                title: "PDF 카탈로그 출력",
                subtitle: "QuestPDF 로 9-페이지 표준 양식 카탈로그 PDF 를 생성합니다.",
                description:
                    "5종 자산 (코드 / AI 모델 / 문서 / 특허 / 실험) 과 KG 통계를 모두 포함한 " +
                    "통합 카탈로그를 QuestPDF 로 생성합니다. 표지 + 자산 종류별 섹션 + KG 요약 " +
                    "+ 부록 (감사 로그 요약) 등 표준 양식으로 출력됩니다.",
                buttonLabel: "▶  PDF 저장 위치 선택 후 생성",
                onClick: () => _main.ExportPdfCatalogCommand.Execute(null),
                bullets: new[]
                {
                    "표지 + 5종 자산 섹션 + KG 통계 + 감사 로그 요약 (9페이지 표준)",
                    "QuestPDF 라이브러리 — 한글 폰트 임베딩",
                    "고해상도 출력 (인쇄용)",
                    "기본 파일명: NowMoment_Catalog_yyyyMMdd_HHmmss.pdf",
                });
            return new ToolActionView { DataContext = vm };
        }

        private FrameworkElement CreateExcelExport()
        {
            return CreateActionScreen(
                "📤 Excel 내보내기",
                "ClosedXML 로 자산 5종을 .xlsx 통합문서로 내보냅니다.\n" +
                "시트 5장 (소스코드/AI모델/문서/특허/실험), 헤더 고정, 자동필터 포함.",
                "Excel 내보내기",
                () => _main.ExportExcelAllCommand.Execute(null));
        }

        // ── 헬퍼 ──────────────────────────────────────────
        private static FrameworkElement CreatePlaceholder(string title, string body)
        {
            var grid = new Grid { Margin = new Thickness(40) };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            sp.Children.Add(new TextBlock {
                Text = title,
                FontSize = 24, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F,0x38,0x64)),
                Margin = new Thickness(0,0,0,12),
            });
            sp.Children.Add(new TextBlock {
                Text = body,
                FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 22,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B,0x55,0x63)),
            });
            grid.Children.Add(sp);
            return grid;
        }

        private static FrameworkElement CreateActionScreen(string title, string body, string buttonLabel, Action onClick)
        {
            var grid = new Grid { Margin = new Thickness(40) };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            sp.Children.Add(new TextBlock {
                Text = title,
                FontSize = 24, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F,0x38,0x64)),
                Margin = new Thickness(0,0,0,12),
            });
            sp.Children.Add(new TextBlock {
                Text = body,
                FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 22,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B,0x55,0x63)),
                Margin = new Thickness(0,0,0,20),
            });
            var btn = new Button {
                Content = "▶  " + buttonLabel,
                Padding = new Thickness(24, 10, 24, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F,0x38,0x64)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btn.Click += (_,_) => onClick();
            sp.Children.Add(btn);
            grid.Children.Add(sp);
            return grid;
        }

        private static void Log(Exception ex, string source)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SPILab", "NowMoment");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + source + "] ===");
                sb.AppendLine(ex.ToString()); sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
