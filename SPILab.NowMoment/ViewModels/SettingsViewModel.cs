// ════════════════════════════════════════════════════════════════════
// SettingsViewModel.cs — v4 Phase 8 (좌측 카테고리 + 우측 패널 UI)
//
// user_setting 테이블의 그룹별 KV 를 편집하는 ViewModel.
// 8 카테고리: general / python / kg / db / security / export / network / about
// 다이얼로그로 호출되며, 저장 시 모든 항목을 한 트랜잭션으로 commit.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>좌측 카테고리 트리의 행 데이터 모델.</summary>
    public class SettingsCategory
    {
        public string Key   { get; set; } = "";   // general / python / kg / db / security / export / network / about
        public string Icon  { get; set; } = "";   // 이모지 1자
        public string Label { get; set; } = "";   // "일반" / "Python" / ...
    }

    public class SettingsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly AuditService _audit;

        // ── 좌측 카테고리 트리 ────────────────────────────────────
        public ObservableCollection<SettingsCategory> Categories { get; } = new()
        {
            new SettingsCategory { Key = "general",  Icon = "🎨", Label = "일반" },
            new SettingsCategory { Key = "python",   Icon = "🐍", Label = "Python" },
            new SettingsCategory { Key = "kg",       Icon = "🕸", Label = "KG 빌더" },
            new SettingsCategory { Key = "db",       Icon = "💾", Label = "DB" },
            new SettingsCategory { Key = "security", Icon = "🔒", Label = "보안" },
            new SettingsCategory { Key = "export",   Icon = "📤", Label = "내보내기" },
            new SettingsCategory { Key = "network",  Icon = "🌐", Label = "네트워크" },
            new SettingsCategory { Key = "about",    Icon = "ℹ",  Label = "정보" },
        };

        private SettingsCategory? _selectedCategory;
        public SettingsCategory? SelectedCategory
        {
            get => _selectedCategory;
            set { if (Set(ref _selectedCategory, value)) OnPropertyChanged(nameof(SelectedCategoryKey)); }
        }
        public string SelectedCategoryKey => _selectedCategory?.Key ?? "general";

        // ── 일반 ──────────────────────────────────────
        public ObservableCollection<string> ThemeOptions   { get; } = new() { "Light", "Dark", "System" };
        public ObservableCollection<string> LanguageOptions{ get; } = new() { "한국어", "English" };
        public ObservableCollection<string> StartScreens   { get; } = new() {
            "대시보드", "소스코드", "AI 모델", "지식그래프", "TTL Studio"
        };

        private string _theme = "Light";
        private string _language = "한국어";
        private string _startScreen = "대시보드";
        public string Theme       { get => _theme;       set => Set(ref _theme, value); }
        public string Language    { get => _language;    set => Set(ref _language, value); }
        public string StartScreen { get => _startScreen; set => Set(ref _startScreen, value); }

        // ── Python ────────────────────────────────────
        private string _pythonPath = "";
        private string _venvPath = "";
        private string _envVars = "";
        private int    _kgTimeoutSec = 300;
        public string PythonPath   { get => _pythonPath;   set { if (Set(ref _pythonPath, value)) DetectPython(); } }
        public string VenvPath     { get => _venvPath;     set => Set(ref _venvPath, value); }
        public string EnvVars      { get => _envVars;      set => Set(ref _envVars, value); }
        public int    KgTimeoutSec { get => _kgTimeoutSec; set => Set(ref _kgTimeoutSec, Math.Max(10, value)); }

        private string _pythonDetect = "(미감지)";
        public string PythonDetectStatus { get => _pythonDetect; set => Set(ref _pythonDetect, value); }

        // ── KG ────────────────────────────────────────
        private string _kgScriptDir = "";
        private string _kgOutputDir = "";
        public string KgScriptDir { get => _kgScriptDir; set => Set(ref _kgScriptDir, value); }
        public string KgOutputDir { get => _kgOutputDir; set => Set(ref _kgOutputDir, value); }

        // ── DB ────────────────────────────────────────
        public ObservableCollection<string> AutoBackupOptions { get; } = new() {
            "사용 안 함", "매일", "매주", "매월"
        };
        private string _autoBackup = "사용 안 함";
        private int    _walCheckpointMin = 60;
        public string AutoBackup       { get => _autoBackup;       set => Set(ref _autoBackup, value); }
        public int    WalCheckpointMin { get => _walCheckpointMin; set => Set(ref _walCheckpointMin, Math.Max(5, value)); }

        // ── 보안 (v4 Phase 8 신규) ────────────────────
        private bool   _dbEncryption = false;
        private string _backupPassword = "";
        public bool   DbEncryption   { get => _dbEncryption;   set => Set(ref _dbEncryption, value); }
        public string BackupPassword { get => _backupPassword; set => Set(ref _backupPassword, value); }

        // ── 내보내기 (v4 Phase 8 신규) ────────────────
        public ObservableCollection<string> ExportFormatOptions { get; } = new() { "xlsx", "csv", "json" };
        public ObservableCollection<string> ExportEncodingOptions { get; } = new() { "UTF-8", "UTF-8 BOM", "CP949" };
        private string _exportFormat = "xlsx";
        private string _exportEncoding = "UTF-8";
        private bool   _exportIncludeHeader = true;
        public string ExportFormat        { get => _exportFormat;        set => Set(ref _exportFormat, value); }
        public string ExportEncoding      { get => _exportEncoding;      set => Set(ref _exportEncoding, value); }
        public bool   ExportIncludeHeader { get => _exportIncludeHeader; set => Set(ref _exportIncludeHeader, value); }

        // ── 네트워크 (v4 Phase 8 신규) ────────────────
        private bool   _proxyEnabled = false;
        private string _proxyHost = "";
        private int    _proxyPort = 8080;
        private int    _networkTimeoutSec = 30;
        public bool   ProxyEnabled      { get => _proxyEnabled;       set => Set(ref _proxyEnabled, value); }
        public string ProxyHost         { get => _proxyHost;          set => Set(ref _proxyHost, value); }
        public int    ProxyPort         { get => _proxyPort;          set => Set(ref _proxyPort, Math.Min(65535, Math.Max(1, value))); }
        public int    NetworkTimeoutSec { get => _networkTimeoutSec;  set => Set(ref _networkTimeoutSec, Math.Max(1, value)); }

        // ── 정보 (read-only, v4 Phase 8 신규) ─────────
        public string AppName    => "NowMoment v4";
        public string AppVersion {
            get {
                try { return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "4.0.0.0"; }
                catch { return "4.0.0.0"; }
            }
        }
        public string AppCompany => "SPILab Co., Ltd.";
        public string AppCopyright => "Copyright © SPILab Co., Ltd. 2026";
        public string AppDescription => "Physics-aware Hybrid AI 기반 기술자산 관리 시스템";
        public string AppLicense => "Proprietary — 사내 전용";

        // ── 명령 ──────────────────────────────────────
        public ICommand SaveCommand           { get; }
        public ICommand CancelCommand         { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand BrowsePythonCommand   { get; }
        public ICommand BrowseVenvCommand     { get; }
        public ICommand BrowseKgScriptCommand { get; }
        public ICommand BrowseKgOutputCommand { get; }

        public event Action<bool>? RequestClose;  // true=저장, false=취소

        public SettingsViewModel(DatabaseService db, AuditService audit)
        {
            _db = db;
            _audit = audit;
            LoadFromDb();

            // 초기 카테고리 = 일반
            _selectedCategory = Categories[0];

            SaveCommand            = new RelayCommand(_ => DoSave());
            // 본문 임베드 화면에서 [취소] = 미저장 변경을 버리고 DB 값 다시 로드.
            CancelCommand          = new RelayCommand(_ => { LoadFromDb(); RequestClose?.Invoke(false); });
            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
            BrowsePythonCommand    = new RelayCommand(_ => BrowseFile("Python 실행 파일", "python.exe|python.exe|모든 파일|*.*",  v => PythonPath = v));
            BrowseVenvCommand      = new RelayCommand(_ => BrowseFolder("가상환경 폴더",   v => VenvPath = v));
            BrowseKgScriptCommand  = new RelayCommand(_ => BrowseFolder("KG 빌더 스크립트 폴더", v => KgScriptDir = v));
            BrowseKgOutputCommand  = new RelayCommand(_ => BrowseFolder("KG 빌더 출력 폴더",     v => KgOutputDir = v));
        }

        private void LoadFromDb()
        {
            // 일반
            Theme        = _db.GetSetting("general", "theme",        "Light");
            Language     = _db.GetSetting("general", "language",     "한국어");
            StartScreen  = _db.GetSetting("general", "start_screen", "대시보드");

            // Python
            PythonPath   = _db.GetSetting("python",  "path",         "");
            VenvPath     = _db.GetSetting("python",  "venv",         "");
            EnvVars      = _db.GetSetting("python",  "env_vars",     "");
            KgTimeoutSec = int.TryParse(_db.GetSetting("python", "kg_timeout_sec", "300"), out var v) ? v : 300;

            // KG
            KgScriptDir  = _db.GetSetting("kg", "script_dir", "");
            KgOutputDir  = _db.GetSetting("kg", "output_dir", "");

            // DB
            AutoBackup       = _db.GetSetting("db", "auto_backup",  "사용 안 함");
            WalCheckpointMin = int.TryParse(_db.GetSetting("db", "wal_checkpoint_min", "60"), out var w) ? w : 60;

            // 보안
            DbEncryption   = string.Equals(_db.GetSetting("security", "db_encryption", "false"), "true", StringComparison.OrdinalIgnoreCase);
            BackupPassword = _db.GetSetting("security", "backup_password", "");

            // 내보내기
            ExportFormat        = _db.GetSetting("export", "format", "xlsx");
            ExportEncoding      = _db.GetSetting("export", "encoding", "UTF-8");
            ExportIncludeHeader = string.Equals(_db.GetSetting("export", "include_header", "true"), "true", StringComparison.OrdinalIgnoreCase);

            // 네트워크
            ProxyEnabled      = string.Equals(_db.GetSetting("network", "proxy_enabled", "false"), "true", StringComparison.OrdinalIgnoreCase);
            ProxyHost         = _db.GetSetting("network", "proxy_host", "");
            ProxyPort         = int.TryParse(_db.GetSetting("network", "proxy_port", "8080"), out var p) ? p : 8080;
            NetworkTimeoutSec = int.TryParse(_db.GetSetting("network", "timeout_sec", "30"), out var t) ? t : 30;
        }

        private void DoSave()
        {
            _db.SetSetting("general", "theme",        Theme);
            _db.SetSetting("general", "language",     Language);
            _db.SetSetting("general", "start_screen", StartScreen);

            _db.SetSetting("python",  "path",            PythonPath);
            _db.SetSetting("python",  "venv",            VenvPath);
            _db.SetSetting("python",  "env_vars",        EnvVars);
            _db.SetSetting("python",  "kg_timeout_sec",  KgTimeoutSec.ToString());

            _db.SetSetting("kg", "script_dir", KgScriptDir);
            _db.SetSetting("kg", "output_dir", KgOutputDir);

            _db.SetSetting("db", "auto_backup",        AutoBackup);
            _db.SetSetting("db", "wal_checkpoint_min", WalCheckpointMin.ToString());

            _db.SetSetting("security", "db_encryption",   DbEncryption ? "true" : "false");
            _db.SetSetting("security", "backup_password", BackupPassword);

            _db.SetSetting("export", "format",         ExportFormat);
            _db.SetSetting("export", "encoding",       ExportEncoding);
            _db.SetSetting("export", "include_header", ExportIncludeHeader ? "true" : "false");

            _db.SetSetting("network", "proxy_enabled", ProxyEnabled ? "true" : "false");
            _db.SetSetting("network", "proxy_host",    ProxyHost);
            _db.SetSetting("network", "proxy_port",    ProxyPort.ToString());
            _db.SetSetting("network", "timeout_sec",   NetworkTimeoutSec.ToString());

            _audit.LogAction("update", "user_setting", null, new {
                groups = new[] { "general", "python", "kg", "db", "security", "export", "network" }
            });

            // 테마 즉시 적용
            try { SPILab.NowMoment.Services.ThemeManager.Apply(Theme); }
            catch { /* 테마 적용 실패해도 저장은 성공이므로 무시 */ }

            RequestClose?.Invoke(true);
        }

        private void RestoreDefaults()
        {
            Theme = "Light"; Language = "한국어"; StartScreen = "대시보드";
            PythonPath = ""; VenvPath = ""; EnvVars = ""; KgTimeoutSec = 300;
            KgScriptDir = ""; KgOutputDir = "";
            AutoBackup = "사용 안 함"; WalCheckpointMin = 60;
            DbEncryption = false; BackupPassword = "";
            ExportFormat = "xlsx"; ExportEncoding = "UTF-8"; ExportIncludeHeader = true;
            ProxyEnabled = false; ProxyHost = ""; ProxyPort = 8080; NetworkTimeoutSec = 30;
        }

        private void DetectPython()
        {
            if (string.IsNullOrWhiteSpace(_pythonPath))
            {
                PythonDetectStatus = "(경로를 입력하세요)";
                return;
            }
            if (!File.Exists(_pythonPath))
            {
                PythonDetectStatus = "✗ 파일이 존재하지 않습니다";
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) { PythonDetectStatus = "✗ 실행 실패"; return; }
                proc.WaitForExit(3000);
                var output = proc.StandardOutput.ReadToEnd().Trim();
                var err    = proc.StandardError.ReadToEnd().Trim();
                var ver = !string.IsNullOrEmpty(output) ? output : err;
                PythonDetectStatus = string.IsNullOrEmpty(ver) ? "✗ 버전 확인 실패" : $"✓ 감지됨: {ver}";
            }
            catch (Exception ex)
            {
                PythonDetectStatus = $"✗ {ex.Message}";
            }
        }

        private static void BrowseFile(string title, string filter, Action<string> apply)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
            if (dlg.ShowDialog() == true) apply(dlg.FileName);
        }

        private static void BrowseFolder(string title, Action<string> apply)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (dlg.ShowDialog() == true) apply(dlg.FolderName);
        }
    }
}
