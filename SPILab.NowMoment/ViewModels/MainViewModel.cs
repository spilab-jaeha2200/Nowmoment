using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    // ── RelayCommand 헬퍼 ────────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object? p) => _execute(p);
        public event EventHandler? CanExecuteChanged
        { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    }

    // ── BaseViewModel ────────────────────────────────────
    public class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
        {
            if (Equals(field, value)) return false;
            field = value; OnPropertyChanged(n); return true;
        }
    }

    // ── MainViewModel ────────────────────────────────────
    public partial class MainViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        // v4: 이력 적재 (자산 CRUD 후 호출용 — Phase 2에서 각 핸들러에 연결)
        public AuditService Audit { get; }

        // 탭 선택
        private int _selectedTab;
        public int SelectedTab { get => _selectedTab; set { Set(ref _selectedTab, value); } }

        // 대시보드
        public ObservableCollection<StatItem> Stats { get; } = new();

        // 자산 컬렉션
        public ObservableCollection<AssetCode>       Codes       { get; } = new();
        public ObservableCollection<AssetModel>      Models      { get; } = new();
        public ObservableCollection<AssetDocument>   Documents   { get; } = new();
        public ObservableCollection<AssetPatent>     Patents     { get; } = new();
        public ObservableCollection<AssetExperiment> Experiments { get; } = new();
        public ObservableCollection<Project>         Projects    { get; } = new();
        public ObservableCollection<SearchResult>    SearchResults { get; } = new();

        // ── v3.0 F-001: 5종 자산 그리드의 현재 선택 항목 ─────────────
        // KG 탭의 [현재 자산에 링크] 버튼이 "어떤 자산을 링크할지" 결정할 때 참조한다.
        // DataGrid SelectedItem 과 양방향 바인딩.
        private AssetCode?       _selectedCode;
        private AssetModel?      _selectedModel;
        private AssetDocument?   _selectedDocument;
        private AssetPatent?     _selectedPatent;
        private AssetExperiment? _selectedExperiment;

        public AssetCode?       SelectedCode       { get => _selectedCode;       set => Set(ref _selectedCode,       value); }
        public AssetModel?      SelectedModel      { get => _selectedModel;      set => Set(ref _selectedModel,      value); }
        public AssetDocument?   SelectedDocument   { get => _selectedDocument;   set => Set(ref _selectedDocument,   value); }
        public AssetPatent?     SelectedPatent     { get => _selectedPatent;     set => Set(ref _selectedPatent,     value); }
        public AssetExperiment? SelectedExperiment { get => _selectedExperiment; set => Set(ref _selectedExperiment, value); }

        // 검색어
        private string _searchKeyword = "";
        public string SearchKeyword { get => _searchKeyword; set => Set(ref _searchKeyword, value); }

        // 각 탭별 검색어
        private string _codeKeyword = "", _modelKeyword = "", _docKeyword = "",
                        _patentKeyword = "", _expKeyword = "";
        public string CodeKeyword    { get => _codeKeyword;    set { Set(ref _codeKeyword, value);    LoadCodes(); } }
        public string ModelKeyword   { get => _modelKeyword;   set { Set(ref _modelKeyword, value);   LoadModels(); } }
        public string DocKeyword     { get => _docKeyword;     set { Set(ref _docKeyword, value);     LoadDocuments(); } }
        public string PatentKeyword  { get => _patentKeyword;  set { Set(ref _patentKeyword, value);  LoadPatents(); } }
        public string ExpKeyword     { get => _expKeyword;     set { Set(ref _expKeyword, value);     LoadExperiments(); } }

        // DB 경로 표시
        public string DbPath => _db.DbPath;

        // 상태바용: 파일명 + 크기 표시 (예: "nowmoment.db (24 MB)")
        public string DbDisplay
        {
            get
            {
                try
                {
                    var path = _db.DbPath;
                    if (string.IsNullOrEmpty(path)) return "(DB 미지정)";
                    var name = System.IO.Path.GetFileName(path);
                    if (!System.IO.File.Exists(path)) return name;
                    var bytes = new System.IO.FileInfo(path).Length;
                    // 1MB 미만은 KB, 이상은 MB (소수점 없이)
                    if (bytes < 1024L * 1024L)
                        return $"{name} ({Math.Max(1, bytes / 1024)} KB)";
                    var mb = bytes / (1024.0 * 1024.0);
                    return $"{name} ({mb:0} MB)";
                }
                catch { return System.IO.Path.GetFileName(_db.DbPath ?? ""); }
            }
        }

        // 상태바용: Python 버전 (최초 1회 탐지 후 캐시)
        private string? _pythonVersion;
        public string PythonVersion
        {
            get
            {
                if (_pythonVersion != null) return _pythonVersion;
                _pythonVersion = DetectPythonVersion();
                return _pythonVersion;
            }
        }

        private static string DetectPythonVersion()
        {
            // python → python3 → py 순으로 시도, "Python 3.11.8" 출력에서 버전만 추출
            foreach (var exe in new[] { "python", "python3", "py" })
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) continue;
                    var outp = p.StandardOutput.ReadToEnd();
                    var err  = p.StandardError.ReadToEnd();
                    p.WaitForExit(3000);
                    var text = (outp + err).Trim();   // 버전이 stderr 로 나오는 구버전 대응
                    if (text.StartsWith("Python", StringComparison.OrdinalIgnoreCase))
                        return text.Substring(6).Trim();   // "Python " 제거
                }
                catch { /* 다음 후보 시도 */ }
            }
            return "미설치";
        }

        // 상태바용: 현재 로그인 사용자
        public string CurrentUser => "관리자";

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand GlobalSearchCommand { get; }
        public ICommand AddCodeCommand { get; }
        public ICommand AddModelCommand { get; }
        public ICommand AddDocumentCommand { get; }
        public ICommand AddPatentCommand { get; }
        public ICommand AddExperimentCommand { get; }
        public ICommand DeleteCodeCommand { get; }
        public ICommand DeleteModelCommand { get; }
        public ICommand DeleteDocumentCommand { get; }
        public ICommand DeletePatentCommand { get; }
        public ICommand DeleteExperimentCommand { get; }
        public ICommand EditCodeCommand { get; }
        public ICommand EditModelCommand { get; }
        public ICommand EditDocumentCommand { get; }
        public ICommand EditPatentCommand { get; }
        public ICommand EditExperimentCommand { get; }

        // ── v4 Phase 2: 일괄 삭제 / 복제 ────────────────
        public ICommand BulkDeleteCodeCommand       { get; private set; } = null!;
        public ICommand BulkDeleteModelCommand      { get; private set; } = null!;
        public ICommand BulkDeleteDocumentCommand   { get; private set; } = null!;
        public ICommand BulkDeletePatentCommand     { get; private set; } = null!;
        public ICommand BulkDeleteExperimentCommand { get; private set; } = null!;
        public ICommand DuplicateCodeCommand        { get; private set; } = null!;
        public ICommand DuplicateModelCommand       { get; private set; } = null!;
        public ICommand DuplicateDocumentCommand    { get; private set; } = null!;
        public ICommand DuplicatePatentCommand      { get; private set; } = null!;
        public ICommand DuplicateExperimentCommand  { get; private set; } = null!;

        // ── v4 Phase 3: 사용자 설정 / Excel 내보내기 ────
        public ICommand OpenSettingsCommand    { get; private set; } = null!;
        public ICommand ExportExcelAllCommand  { get; private set; } = null!;
        public ICommand ExportExcelKindCommand { get; private set; } = null!;

        // ── v3.0 F-003: DB 백업 ─────────────────────────
        public ICommand BackupDbCommand { get; }

        // ── v3.0 F-002: 폴더 임포트 ─────────────────────
        public ICommand FolderImportCommand { get; }

        // ── v3.0 F-004: PDF 카탈로그 생성 ───────────────
        public ICommand ExportPdfCatalogCommand { get; }

        // ── v3.0 F-005: TTL Studio (메인 탭에 임베딩) ─
        // 팝업 다이얼로그 대신 MainWindow 의 TabItem 안에 직접 배치하므로
        // ViewModel 인스턴스를 속성으로 직접 노출.
        public TtlStudioViewModel TtlStudio { get; }

        public MainViewModel(DatabaseService db)
        {
            _db = db;
            Audit = new AuditService(_db);

            RefreshCommand      = new RelayCommand(_ => LoadAll());
            GlobalSearchCommand = new RelayCommand(_ => DoGlobalSearch());

            AddCodeCommand       = new RelayCommand(_ => OpenAddCode());
            AddModelCommand      = new RelayCommand(_ => OpenAddModel());
            AddDocumentCommand   = new RelayCommand(_ => OpenAddDocument());
            AddPatentCommand     = new RelayCommand(_ => OpenAddPatent());
            AddExperimentCommand = new RelayCommand(_ => OpenAddExperiment());

            DeleteCodeCommand       = new RelayCommand(p => DeleteCode(p));
            DeleteModelCommand      = new RelayCommand(p => DeleteModel(p));
            DeleteDocumentCommand   = new RelayCommand(p => DeleteDocument(p));
            DeletePatentCommand     = new RelayCommand(p => DeletePatent(p));
            DeleteExperimentCommand = new RelayCommand(p => DeleteExperiment(p));

            EditCodeCommand       = new RelayCommand(p => EditCode(p));
            EditModelCommand      = new RelayCommand(p => EditModel(p));
            EditDocumentCommand   = new RelayCommand(p => EditDocument(p));
            EditPatentCommand     = new RelayCommand(p => EditPatent(p));
            EditExperimentCommand = new RelayCommand(p => EditExperiment(p));

            // v4 Phase 2: 일괄 삭제 (DataGrid 다중선택 → 콘텍스트 메뉴 또는 Del 키)
            BulkDeleteCodeCommand       = new RelayCommand(p => BulkDeleteCode(p));
            BulkDeleteModelCommand      = new RelayCommand(p => BulkDeleteModel(p));
            BulkDeleteDocumentCommand   = new RelayCommand(p => BulkDeleteDocument(p));
            BulkDeletePatentCommand     = new RelayCommand(p => BulkDeletePatent(p));
            BulkDeleteExperimentCommand = new RelayCommand(p => BulkDeleteExperiment(p));

            // v4 Phase 2: 복제 (Ctrl+D / 콘텍스트 메뉴)
            DuplicateCodeCommand       = new RelayCommand(p => DuplicateCode(p));
            DuplicateModelCommand      = new RelayCommand(p => DuplicateModel(p));
            DuplicateDocumentCommand   = new RelayCommand(p => DuplicateDocument(p));
            DuplicatePatentCommand     = new RelayCommand(p => DuplicatePatent(p));
            DuplicateExperimentCommand = new RelayCommand(p => DuplicateExperiment(p));

            // v4 Phase 3: 사용자 설정 / Excel 내보내기
            OpenSettingsCommand    = new RelayCommand(_ => DoOpenSettings());
            ExportExcelAllCommand  = new RelayCommand(_ => DoExportExcel(null));
            ExportExcelKindCommand = new RelayCommand(p => DoExportExcel(p as string));

            BackupDbCommand = new RelayCommand(_ => DoBackupDb());
            // v3.0 F-002: parameter 로 자산 종류 prefilter 받음
            FolderImportCommand = new RelayCommand(p => DoFolderImport(p as string));
            // v3.0 F-004: parameter 로 자산 종류 받음 (탭별 카탈로그)
            ExportPdfCatalogCommand = new RelayCommand(p => DoExportPdfCatalog(p as string));
            // v3.0 F-005: TTL Studio 인스턴스 생성 — 메인 탭에 임베딩됨
            TtlStudio = new TtlStudioViewModel();

            LoadAll();
        }

        public void LoadAll()
        {
            LoadStats(); LoadProjects();
            LoadCodes(); LoadModels(); LoadDocuments();
            LoadPatents(); LoadExperiments();
        }

        private void LoadStats()
        {
            Stats.Clear();
            var s = _db.GetStats();
            foreach (var kv in s)
                Stats.Add(new StatItem { Label = kv.Key, Count = kv.Value });
        }

        private void LoadProjects()
        {
            Projects.Clear();
            foreach (var p in _db.GetProjects()) Projects.Add(p);
        }

        public void LoadCodes()
        {
            Codes.Clear();
            foreach (var x in _db.GetCodes(_codeKeyword)) Codes.Add(x);
        }
        public void LoadModels()
        {
            Models.Clear();
            foreach (var x in _db.GetModels(_modelKeyword)) Models.Add(x);
        }
        public void LoadDocuments()
        {
            Documents.Clear();
            foreach (var x in _db.GetDocuments(_docKeyword)) Documents.Add(x);
        }
        public void LoadPatents()
        {
            Patents.Clear();
            foreach (var x in _db.GetPatents(_patentKeyword)) Patents.Add(x);
        }
        public void LoadExperiments()
        {
            Experiments.Clear();
            foreach (var x in _db.GetExperiments(_expKeyword)) Experiments.Add(x);
        }

        private void DoGlobalSearch()
        {
            SearchResults.Clear();
            foreach (var r in _db.GlobalSearch(_searchKeyword))
                SearchResults.Add(r);
        }

        // ── 등록 다이얼로그 ──────────────────────────────
        // v3.0 F-001 Step 1.5: 신규 자산은 AssetId == 0 → KgPanel은 비활성 안내만 표시
        // v4.0 Phase 2: INSERT 직후 last_insert_rowid → Audit.LogCreate + 태그 동기화
        private void OpenAddCode()
        { OpenAddCodeV4(); }
        private void OpenAddModel()
        { OpenAddModelV4(); }
        private void OpenAddDocument()
        { OpenAddDocumentV4(); }
        private void OpenAddPatent()
        { OpenAddPatentV4(); }
        private void OpenAddExperiment()
        { OpenAddExperimentV4(); }

        // ── 편집 ─────────────────────────────────────────
        // v3.0 F-001 Step 1.5: 기존 자산은 AssetId > 0 → KgPanel 활성, 링크 즉시 추가/삭제 가능
        // v4.0 Phase 2: UPDATE 전 before-image 캡처 → Audit.LogUpdate(diff) + updated_at touch
        private void EditCode(object? p)
        { EditCodeV4(p); }
        private void EditModel(object? p)
        { EditModelV4(p); }
        private void EditDocument(object? p)
        { EditDocumentV4(p); }
        private void EditPatent(object? p)
        { EditPatentV4(p); }
        private void EditExperiment(object? p)
        { EditExperimentV4(p); }

        // ── 삭제 ─────────────────────────────────────────
        // v4.0 Phase 2: DELETE 전 snapshot 캡처 → Audit.LogDelete(old)
        private void DeleteCode(object? p)
        {
            if (p is not AssetCode a) return;
            if (!Confirm($"'{a.Name}' 을 삭제합니까?")) return;
            var snap = _db.GetCodeById(a.Id);
            _db.DeleteAsset("asset_code", a.Id);
            Audit.LogDelete("asset_code", a.Id, snap);
            LoadCodes(); LoadStats();
        }
        private void DeleteModel(object? p)
        {
            if (p is not AssetModel a) return;
            if (!Confirm($"'{a.Name}' 을 삭제합니까?")) return;
            var snap = _db.GetModelById(a.Id);
            _db.DeleteAsset("asset_model", a.Id);
            Audit.LogDelete("asset_model", a.Id, snap);
            LoadModels(); LoadStats();
        }
        private void DeleteDocument(object? p)
        {
            if (p is not AssetDocument a) return;
            if (!Confirm($"'{a.Title}' 을 삭제합니까?")) return;
            var snap = _db.GetDocumentById(a.Id);
            _db.DeleteAsset("asset_document", a.Id);
            Audit.LogDelete("asset_document", a.Id, snap);
            LoadDocuments(); LoadStats();
        }
        private void DeletePatent(object? p)
        {
            if (p is not AssetPatent a) return;
            if (!Confirm($"'{a.Title}' 을 삭제합니까?")) return;
            var snap = _db.GetPatentById(a.Id);
            _db.DeleteAsset("asset_patent", a.Id);
            Audit.LogDelete("asset_patent", a.Id, snap);
            LoadPatents(); LoadStats();
        }
        private void DeleteExperiment(object? p)
        {
            if (p is not AssetExperiment a) return;
            if (!Confirm($"'{a.Name}' 을 삭제합니까?")) return;
            var snap = _db.GetExperimentById(a.Id);
            _db.DeleteAsset("asset_experiment", a.Id);
            Audit.LogDelete("asset_experiment", a.Id, snap);
            LoadExperiments(); LoadStats();
        }

        // ── v3.0 F-003: DB 백업 ──────────────────────────
        private void DoBackupDb()
        {
            // 1) 저장 위치 선택
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "NowMoment DB 백업 — 저장 위치 선택",
                Filter   = "백업 zip 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                FileName = Services.Backup.BackupService.BuildDefaultFileName(),
                AddExtension = true,
                DefaultExt   = "zip",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true) return;

            // 2) 백업 실행
            try
            {
                var svc = new Services.Backup.BackupService(
                    _db.DbPath, _db.ConnectionString, () => _db.GetStats());
                var manifest = svc.CreateBackup(dlg.FileName);

                // 3) 결과 요약
                long sizeMb = new System.IO.FileInfo(dlg.FileName).Length / 1024;

                // v4 Phase 2: Audit 적재 — backup 액션
                Audit.LogAction("backup", "system", null, new {
                    file = System.IO.Path.GetFileName(dlg.FileName),
                    size_kb = sizeMb,
                    sha256 = manifest.Sha256Hex,
                    asset_counts = manifest.AssetCounts,
                });

                var summary = new System.Text.StringBuilder();
                summary.AppendLine("✅ 백업 완료");
                summary.AppendLine();
                summary.AppendLine($"파일: {System.IO.Path.GetFileName(dlg.FileName)}");
                summary.AppendLine($"위치: {System.IO.Path.GetDirectoryName(dlg.FileName)}");
                summary.AppendLine($"크기: {sizeMb:N0} KB");
                summary.AppendLine($"DB SHA-256: {manifest.Sha256Hex.Substring(0, 16)}…");
                summary.AppendLine();
                summary.AppendLine("자산 카운트:");
                foreach (var kv in manifest.AssetCounts)
                    summary.AppendLine($"  · {kv.Key}: {kv.Value}");

                MessageBox.Show(summary.ToString(),
                    "DB 백업", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"백업 실패: {ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── v3.0 F-002: 폴더 임포트 ──────────────────────
        // <param name="prefilterKind">
        //   null/빈문자열: 모든 자산 종류
        //   "code" / "model" / "document" / "experiment":
        //     해당 종류만 미리보기에 표시 (자산 탭별 호출 시 사용)
        // </param>
        private void DoFolderImport(string? prefilterKind = null)
        {
            var vm = new FolderImportViewModel(_db, _db.GetProjects());
            vm.PrefilterKind = prefilterKind ?? "";

            var dlg = new Views.FolderImportDialog(vm)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();

            // 다이얼로그가 닫힌 후 실제 임포트가 일어났을 수 있으므로 전체 재로드
            // (Done 단계까지 갔다면 자산이 추가됨)
            if (vm.IsDone)
            {
                // v4 Phase 2: Audit 적재 — import 액션
                Audit.LogAction("import", "system", null, new {
                    prefilter = prefilterKind ?? "all",
                    folder = vm.RootPath,
                    scanned = vm.ScannedCount,
                });
                LoadAll();
            }
        }

        // ── v3.0 F-004: PDF 카탈로그 생성 ─────────────────
        // <param name="kindFilter">
        //   v3.0.x 에서는 자산 탭별로 호출 가능하지만, 사용자 요청에 따라
        //   결과는 항상 전체 자산 + KG 통계 포함 (통합 카탈로그).
        //   파라미터는 호환성을 위해 유지하지만 옵션 분기에는 사용하지 않음.
        // </param>
        private void DoExportPdfCatalog(string? kindFilter = null)
        {
            // 항상 전체 자산 + KG 통계 포함 (통합 카탈로그)
            var opts = new Services.Pdf.CatalogPdfOptions
            {
                Title    = "기술자산 카탈로그",
                Subtitle = "정부과제 산출물용 통합 보고서",
                Author   = "SPILab Co., Ltd.",
                IncludeCode       = true,
                IncludeModel      = true,
                IncludeDocument   = true,
                IncludePatent     = true,
                IncludeExperiment = true,
                IncludeKgSummary  = true,
            };

            // 저장 위치 선택
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "기술자산 카탈로그 PDF 저장 위치",
                Filter   = "PDF 파일 (*.pdf)|*.pdf|모든 파일 (*.*)|*.*",
                FileName = $"NowMoment_Catalog_{System.DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                AddExtension    = true,
                DefaultExt      = "pdf",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true) return;

            // PDF 생성
            //
            // 어셈블리 로드 실패(특히 QuestPDF.dll 미배포)는 현장에서 가장 빈번한
            // 카탈로그 출력 오류이므로, CatalogPdfBuilder 생성자를 호출하기 전에
            // 사전 점검(pre-flight check)을 수행해 친절한 안내 메시지를 띄운다.
            // CLR 어셈블리 바인딩이 실제로 실패할 때까지 기다리지 않고 선제 진단.
            try
            {
                Application.Current.MainWindow.Cursor = System.Windows.Input.Cursors.Wait;

                // ── 사전 점검 1: QuestPDF 어셈블리가 현재 AppDomain 또는 디스크에
                //                존재하는지 확인. CLR이 아직 로드하지 않았을 수 있으므로
                //                실행파일 위치 기준으로 직접 파일 존재 여부도 확인.
                var pdfLoadError = TryDiagnoseQuestPdfAvailability();
                if (pdfLoadError != null)
                {
                    Application.Current.MainWindow.Cursor = null;
                    // v3.0.3: 사용자에게 위협적이지 않은 노란 경고 아이콘 사용
                    MessageBox.Show(pdfLoadError, "PDF 라이브러리 로드 실패",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var builder = new Services.Pdf.CatalogPdfBuilder(_db, KgService);
                builder.Build(opts, dlg.FileName);

                Application.Current.MainWindow.Cursor = null;

                // 결과 안내 + 파일 열기 옵션
                var fi = new System.IO.FileInfo(dlg.FileName);
                var r = MessageBox.Show(
                    $"✅ PDF 카탈로그 생성 완료\n\n" +
                    $"파일: {System.IO.Path.GetFileName(dlg.FileName)}\n" +
                    $"위치: {System.IO.Path.GetDirectoryName(dlg.FileName)}\n" +
                    $"크기: {fi.Length / 1024:N0} KB\n\n" +
                    $"전체 자산 (소스코드·모델·문서·특허·실험) + 지식그래프 통계가 포함되었습니다.\n\n" +
                    $"지금 PDF 를 열어보시겠습니까?",
                    "카탈로그 출력", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dlg.FileName,
                        UseShellExecute = true,
                    });
                }
            }
            // ── 어셈블리 로드 계열 예외: 사전 점검이 놓친 케이스를 잡는 안전망.
            //    실제로는 사전 점검에서 대부분 차단되므로 여기까지 오는 일은 드물지만,
            //    QuestPDF 이외의 의존 어셈블리(SkiaSharp 등)가 누락된 경우를 대비.
            catch (Exception ex) when (ex is System.IO.FileNotFoundException
                                    || ex is System.IO.FileLoadException
                                    || ex is BadImageFormatException
                                    || ex is TypeInitializationException
                                    || ex is System.Reflection.ReflectionTypeLoadException)
            {
                Application.Current.MainWindow.Cursor = null;

                // FileNotFoundException 의 FileName 속성에는 누락된 어셈블리 이름이 담긴다.
                string missing = "(알 수 없음)";
                if (ex is System.IO.FileNotFoundException fnf && !string.IsNullOrEmpty(fnf.FileName))
                    missing = fnf.FileName;
                else if (ex is System.IO.FileLoadException fle && !string.IsNullOrEmpty(fle.FileName))
                    missing = fle.FileName;

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                MessageBox.Show(
                    "PDF 생성에 필요한 라이브러리를 불러올 수 없습니다.\n\n" +
                    $"누락된 어셈블리: {missing}\n\n" +
                    "[원인] 실행 폴더에 필요한 DLL 파일이 없거나 손상되었습니다.\n\n" +
                    "[해결 방법]\n" +
                    "1) 프로그램을 재설치하거나, 설치 폴더에 빠진 DLL이 있는지 확인하세요.\n" +
                    "2) 안티바이러스가 DLL 을 격리하지 않았는지 확인하세요.\n" +
                    "3) 단일 EXE 배포본의 경우 publish 옵션\n" +
                    "   IncludeAllContentForSelfExtract=true,\n" +
                    "   IncludeNativeLibrariesForSelfExtract=true 를 추가해 재배포하세요.\n\n" +
                    $"실행 폴더: {exeDir}\n\n" +
                    "─── 상세 오류 ───\n" + ex.Message,
                    "PDF 라이브러리 로드 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Application.Current.MainWindow.Cursor = null;
                MessageBox.Show($"PDF 생성 실패: {ex.Message}\n\n{ex.StackTrace}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// QuestPDF 어셈블리 사용 가능 여부를 사전 진단한다.
        /// 사용 가능하면 null 반환, 문제가 있으면 사용자에게 보여줄 메시지 반환.
        /// CatalogPdfBuilder 생성자 호출 직전에 호출하여 CLR 바인딩 예외를 선제 차단한다.
        /// </summary>
        private static string? TryDiagnoseQuestPdfAvailability()
        {
            const string AssemblyName = "QuestPDF";
            const string DllName      = "QuestPDF.dll";

            // 1) 이미 로드되어 있는지 확인 (가장 빠른 경로 - 정상 케이스)
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(a.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase))
                    return null; // 이미 로드됨 → 정상
            }

            // 2) 실행 폴더에 DLL 파일이 있는지 확인 후 명시적으로 로드 시도
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = System.IO.Path.Combine(exeDir, DllName);
            if (!System.IO.File.Exists(dllPath))
            {
                return "PDF 생성에 필요한 라이브러리 파일이 없습니다.\n\n" +
                       $"누락된 파일: {DllName}\n" +
                       $"예상 위치  : {dllPath}\n\n" +
                       "[원인]\n" +
                       "  - 배포본에서 일부 파일이 빠졌거나,\n" +
                       "  - 안티바이러스가 격리했거나,\n" +
                       "  - 사용자가 실수로 삭제했을 수 있습니다.\n\n" +
                       "[해결 방법]\n" +
                       "  1) 프로그램을 재설치하세요.\n" +
                       "  2) NuGet 캐시에서 직접 복사하려면:\n" +
                       $"     %USERPROFILE%\\.nuget\\packages\\questpdf\\2024.12.0\\lib\\net8.0\\{DllName}\n" +
                       $"     위 파일을 [{exeDir}] 폴더에 복사하세요.";
            }

            try
            {
                System.Reflection.Assembly.LoadFrom(dllPath);
                return null; // 명시 로드 성공 → 정상
            }
            catch (Exception ex)
            {
                return "PDF 라이브러리 파일은 존재하지만 로드에 실패했습니다.\n\n" +
                       $"파일: {dllPath}\n" +
                       $"오류: {ex.GetType().Name} — {ex.Message}\n\n" +
                       "[가능한 원인]\n" +
                       "  - DLL 파일 손상(파일 크기 0 또는 깨짐)\n" +
                       "  - 32 비트/64 비트 아키텍처 불일치\n" +
                       "  - .NET 런타임 버전 불일치 (이 프로그램은 .NET 8 필요)\n" +
                       "  - 의존 라이브러리(SkiaSharp 등) 누락\n\n" +
                       "[해결 방법]\n" +
                       "  - 프로그램 재설치를 권장합니다.";
            }
        }

        private static bool Confirm(string msg)
            => MessageBox.Show(msg, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public class StatItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }
}
