// ════════════════════════════════════════════════════════════════════
// ViewModels/KgImportViewModel.cs — v4 JSON/TTL 임포트 화면 전용 VM
//
// v3.0 KnowledgeGraphService.ImportFromFile(path, domain) 을 직접 호출하여
// 파일을 SQLite 트랜잭션으로 임포트한다. 임포트 전후 통계는 v3 GetStats 사용.
//
// 화면 흐름:
//   1) 도메인 선택 (v3 KgViewModel.Domains)
//   2) 파일 선택 (TTL / JSON / JSONLD / RDF / NT)
//   3) 임포트 전 통계 미리보기 (현재 도메인의 노드/엣지 수)
//   4) [커밋] 클릭 → ImportFromFile → 결과 표시 + KgViewModel.Reload
//
// 추가 기능:
//   • 빌더 출력 폴더 열기 (자주 사용하는 위치)
//   • 도메인 비우기 (v3 ClearDomain 명령 — 임포트 전 초기화)
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public class KgImportViewModel : INotifyPropertyChanged
    {
        private readonly KgViewModel _kg;
        private readonly KnowledgeGraphService _service;

        public KgImportViewModel(KgViewModel kg, KnowledgeGraphService service)
        {
            _kg = kg ?? throw new ArgumentNullException(nameof(kg));
            _service = service ?? throw new ArgumentNullException(nameof(service));

            _kg.PropertyChanged += OnKgPropertyChanged;
            _kg.Domains.CollectionChanged += (_, __) => OnPropertyChanged(nameof(Domains));

            BrowseFileCommand     = new SimpleCommand(_ => BrowseFile());
            CommitImportCommand   = new SimpleCommand(_ => CommitImport(),
                                                     _ => !string.IsNullOrEmpty(_selectedFilePath)
                                                          && File.Exists(_selectedFilePath)
                                                          && !string.IsNullOrEmpty(_kg.SelectedDomain));
            OpenOutputFolderCommand = new SimpleCommand(_ => OpenOutputFolder());
            // 도메인 비우기 — v3 ClearDomainCommand 실행 후 미리보기 통계 문구도 갱신.
            ClearDomainCommand    = new SimpleCommand(
                _ =>
                {
                    if (_kg.ClearDomainCommand?.CanExecute(null) == true)
                        _kg.ClearDomainCommand.Execute(null);
                    _kg.Reload();
                    UpdatePreviewStats();
                },
                _ => _kg.ClearDomainCommand?.CanExecute(null) == true);
            RefreshPreviewCommand = new SimpleCommand(_ => UpdatePreviewStats());

            UpdatePreviewStats();
        }

        // ── 도메인 ──
        public ObservableCollection<KgDomain> Domains => _kg.Domains;

        // latest KgViewModel.SelectedDomain 은 도메인 코드(string) 를 보관한다.
        // XAML 의 도메인 콤보박스는 KgDomain 객체를 바인딩하므로 어댑터 역할.
        public KgDomain? SelectedDomain
        {
            get
            {
                var code = _kg.SelectedDomain;
                if (string.IsNullOrEmpty(code)) return null;
                return _kg.Domains.FirstOrDefault(d => d.Code == code);
            }
            set
            {
                _kg.SelectedDomain = value?.Code ?? "";
                OnPropertyChanged();
                UpdatePreviewStats();
            }
        }

        // ── 파일 선택 ──
        private string _selectedFilePath = "";
        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                if (_selectedFilePath != value)
                {
                    _selectedFilePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedFileName));
                    OnPropertyChanged(nameof(SelectedFileSize));
                    OnPropertyChanged(nameof(SelectedFileFormat));
                    OnPropertyChanged(nameof(HasFile));
                    (CommitImportCommand as SimpleCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string SelectedFileName
            => string.IsNullOrEmpty(_selectedFilePath) ? "(파일 미선택)" : Path.GetFileName(_selectedFilePath);

        public string SelectedFileSize
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath)) return "";
                var bytes = new FileInfo(_selectedFilePath).Length;
                return bytes < 1024 ? $"{bytes} B"
                     : bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB"
                     : $"{bytes / 1024.0 / 1024.0:F2} MB";
            }
        }

        public string SelectedFileFormat
        {
            get
            {
                var ext = Path.GetExtension(_selectedFilePath).ToLowerInvariant();
                return ext switch
                {
                    ".ttl"   => "Turtle (.ttl)",
                    ".json"  => "JSON-LD (.json)",
                    ".jsonld"=> "JSON-LD (.jsonld)",
                    ".rdf"   => "RDF/XML (.rdf)",
                    ".nt"    => "N-Triples (.nt)",
                    ""       => "",
                    _        => $"기타 ({ext})",
                };
            }
        }

        public bool HasFile => !string.IsNullOrEmpty(_selectedFilePath) && File.Exists(_selectedFilePath);

        // ── 임포트 전 통계 (현재 도메인 상태) ──
        private int _existingNodes;
        public int ExistingNodes
        {
            get => _existingNodes;
            private set { if (_existingNodes != value) { _existingNodes = value; OnPropertyChanged(); } }
        }

        private int _existingEdges;
        public int ExistingEdges
        {
            get => _existingEdges;
            private set { if (_existingEdges != value) { _existingEdges = value; OnPropertyChanged(); } }
        }

        public string PreviewLabel
        {
            get
            {
                var d = SelectedDomain;
                if (d == null) return "(도메인 미선택)";
                var name = string.IsNullOrEmpty(d.Label) ? d.Code : d.Label;
                return $"[{name}] 도메인 현재: 노드 {ExistingNodes}개 · 엣지 {ExistingEdges}개";
            }
        }

        // ── 임포트 결과 ──
        private string _resultMessage = "임포트할 파일을 선택하세요.";
        public string ResultMessage
        {
            get => _resultMessage;
            private set { if (_resultMessage != value) { _resultMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsResultError)); OnPropertyChanged(nameof(IsResultSuccess)); } }
        }

        private bool _resultIsError;
        public bool IsResultError    => _resultIsError;
        public bool IsResultSuccess  => !_resultIsError && _resultMessage.Contains("완료");

        // ── Commands ──
        public ICommand BrowseFileCommand        { get; }
        public ICommand CommitImportCommand      { get; }
        public ICommand OpenOutputFolderCommand  { get; }
        public ICommand ClearDomainCommand       { get; }
        public ICommand RefreshPreviewCommand    { get; }

        // ── 내부 로직 ──

        private void OnKgPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(KgViewModel.SelectedDomain))
            {
                OnPropertyChanged(nameof(SelectedDomain));
                UpdatePreviewStats();
                (CommitImportCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void BrowseFile()
        {
            var d = SelectedDomain;
            var displayName = d == null ? "" : (string.IsNullOrEmpty(d.Label) ? d.Code : d.Label);
            var dlg = new OpenFileDialog
            {
                Title  = d == null
                    ? "KG 파일 선택"
                    : $"[{displayName}] 도메인에 임포트할 KG 파일 선택",
                Filter = "KG 파일 (*.ttl;*.json;*.jsonld;*.rdf;*.nt)|*.ttl;*.json;*.jsonld;*.rdf;*.nt|" +
                         "Turtle (*.ttl)|*.ttl|" +
                         "JSON-LD (*.json;*.jsonld)|*.json;*.jsonld|" +
                         "RDF/XML (*.rdf)|*.rdf|" +
                         "N-Triples (*.nt)|*.nt|" +
                         "모든 파일|*.*",
            };
            try { dlg.InitialDirectory = KgBuilderRunner.OutputDir; } catch { }
            if (dlg.ShowDialog() != true) return;
            SelectedFilePath = dlg.FileName;
            ResultMessage = $"파일 선택됨: {Path.GetFileName(dlg.FileName)}  —  [커밋] 클릭 시 임포트 실행";
            _resultIsError = false;
        }

        private void CommitImport()
        {
            var d = SelectedDomain;
            if (d == null || !HasFile) return;
            try
            {
                // ImportFromFile 의 domain 파라미터는 도메인 코드(string) 를 받는다.
                var stats = _service.ImportFromFile(_selectedFilePath, d.Code);
                // v3 KgViewModel 의 인메모리 컬렉션도 갱신
                _kg.Reload();
                UpdatePreviewStats();
                var byType = stats.NodesByType.Any()
                    ? "  " + string.Join(" · ", stats.NodesByType.Select(kv => $"{kv.Key}({kv.Value})"))
                    : "";
                var displayName = string.IsNullOrEmpty(d.Label) ? d.Code : d.Label;
                ResultMessage =
                    $"✅ 임포트 완료 — [{displayName}] 노드 {stats.Nodes}개 · 엣지 {stats.Edges}개{byType}";
                _resultIsError = false;
                OnPropertyChanged(nameof(IsResultSuccess));
                OnPropertyChanged(nameof(IsResultError));

                MessageBox.Show(
                    $"임포트가 완료되었습니다.\n\n도메인: {displayName}\n노드 {stats.Nodes}개 · 엣지 {stats.Edges}개",
                    "임포트 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ResultMessage = $"❌ 임포트 실패: {ex.Message}";
                _resultIsError = true;
                OnPropertyChanged(nameof(IsResultSuccess));
                OnPropertyChanged(nameof(IsResultError));
            }
        }

        private void OpenOutputFolder()
        {
            try
            {
                var dir = KgBuilderRunner.OutputDir;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("폴더 열기 실패:\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdatePreviewStats()
        {
            if (string.IsNullOrEmpty(_kg.SelectedDomain))
            {
                ExistingNodes = 0;
                ExistingEdges = 0;
                OnPropertyChanged(nameof(PreviewLabel));
                return;
            }
            try
            {
                // GetStats 의 domain 파라미터는 도메인 코드(string).
                var stats = _service.GetStats("", _kg.SelectedDomain);
                ExistingNodes = stats.Nodes;
                ExistingEdges = stats.Edges;
            }
            catch
            {
                ExistingNodes = 0;
                ExistingEdges = 0;
            }
            OnPropertyChanged(nameof(PreviewLabel));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
