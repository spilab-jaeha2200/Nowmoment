// ════════════════════════════════════════════════════════════
// FolderImportViewModel.cs — v3.0 F-002 폴더 임포트 (Step 2.2)
//
// 다이얼로그 전체 흐름:
//   Phase 1: 사용자가 루트 폴더 선택 + [스캔 시작]
//   Phase 2: 백그라운드 스캔 + 자동 분류 → 미리보기 그리드
//   Phase 3: 사용자가 체크 항목 / 자산명 / 종류 / 프로젝트 편집
//   Phase 4: [확정] → 선택 항목들을 DB 에 INSERT
//
// 스캔은 무거운 IO 라 Task 로 백그라운드 실행 + CancellationToken 으로 취소.
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;
using SPILab.NowMoment.Services.Import;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>다이얼로그 진행 단계 — 우측 영역 표시 전환용.</summary>
    public enum ImportPhase
    {
        SelectFolder = 0,   // 1단계: 폴더 선택 입력
        Scanning     = 1,   // 2단계: 스캔 진행 중 (진행률 표시)
        Preview      = 2,   // 3단계: 미리보기 그리드 + 편집
        Committing   = 3,   // 4단계: DB INSERT 실행 중
        Done         = 4,   // 5단계: 결과 요약 표시
    }

    public class FolderImportViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly FolderScanner   _scanner = new();
        // v4.1: 자동 분류는 Core Provider 경유 — Core 미탑재 시 PassthroughClassifier 폴백.
        private readonly Core.Contracts.IAssetClassifier _classifier
            = Core.Provider.CoreServices.Classifier;
        private CancellationTokenSource? _cts;

        // ── 입력 ─────────────────────────────────────────
        private string _rootPath = "";
        public string RootPath
        {
            get => _rootPath;
            set { Set(ref _rootPath, value); CommandManager.InvalidateRequerySuggested(); }
        }

        public ObservableCollection<Project> Projects { get; }
        private Project? _selectedProject;
        public Project? SelectedProject
        {
            get => _selectedProject;
            set => Set(ref _selectedProject, value);
        }

        // ── v3.0 F-002 hotfix: 자산 탭별 prefilter ─────────
        // 메인 헤더에서 호출(빈 문자열) 또는 자산 탭에서 호출("code"/"model"/...)
        // 빈 문자열이면 모든 종류 표시, 종류 명시 시 해당 종류만 미리보기.
        public string PrefilterKind { get; set; } = "";

        public string PrefilterKindLabel => PrefilterKind?.ToLowerInvariant() switch
        {
            "code"       => "소스코드",
            "model"      => "AI 모델",
            "document"   => "문서·논문",
            "experiment" => "실험",
            _            => "전체",
        };

        public string DialogTitle => string.IsNullOrEmpty(PrefilterKind)
            ? "📁 폴더 자산 임포트"
            : $"📁 [{PrefilterKindLabel}] 폴더 임포트";

        // ── 진행 상태 ────────────────────────────────────
        private ImportPhase _phase = ImportPhase.SelectFolder;
        public ImportPhase Phase
        {
            get => _phase;
            set
            {
                if (Set(ref _phase, value))
                {
                    OnPropertyChanged(nameof(IsSelectFolder));
                    OnPropertyChanged(nameof(IsScanning));
                    OnPropertyChanged(nameof(IsPreview));
                    OnPropertyChanged(nameof(IsCommitting));
                    OnPropertyChanged(nameof(IsDone));
                }
            }
        }
        public bool IsSelectFolder => Phase == ImportPhase.SelectFolder;
        public bool IsScanning     => Phase == ImportPhase.Scanning;
        public bool IsPreview      => Phase == ImportPhase.Preview;
        public bool IsCommitting   => Phase == ImportPhase.Committing;
        public bool IsDone         => Phase == ImportPhase.Done;

        private string _statusMessage = "임포트할 폴더를 선택하세요.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private int _scannedCount;
        public int ScannedCount
        {
            get => _scannedCount;
            set => Set(ref _scannedCount, value);
        }

        // ── 결과 ─────────────────────────────────────────
        public ObservableCollection<ImportCandidate> Candidates { get; } = new();

        public ObservableCollection<string> KindOptions { get; } = new()
            { "소스코드", "AI 모델", "문서", "실험", "미분류" };

        // 커밋 후 결과 요약
        private string _commitSummary = "";
        public string CommitSummary
        {
            get => _commitSummary;
            set => Set(ref _commitSummary, value);
        }

        // ── 명령 ─────────────────────────────────────────
        public ICommand BrowseRootCommand { get; }
        public ICommand ScanCommand       { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand SelectAllCommand  { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand CommitCommand     { get; }
        public ICommand BackToSelectCommand { get; }

        public FolderImportViewModel(DatabaseService db, List<Project> projects)
        {
            _db = db;
            Projects = new ObservableCollection<Project>(projects);

            BrowseRootCommand = new RelayCommand(_ => Browse());
            ScanCommand       = new RelayCommand(_ => _ = ScanAsync(),
                                                 _ => IsSelectFolder
                                                      && !string.IsNullOrWhiteSpace(_rootPath)
                                                      && Directory.Exists(_rootPath));
            CancelScanCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
            SelectAllCommand  = new RelayCommand(_ => SetAllSelection(true),  _ => IsPreview);
            SelectNoneCommand = new RelayCommand(_ => SetAllSelection(false), _ => IsPreview);
            CommitCommand     = new RelayCommand(_ => _ = CommitAsync(),
                                                 _ => IsPreview && Candidates.Any(c => c.IsSelected));
            BackToSelectCommand = new RelayCommand(_ => GoBackToSelectFolder(),
                                                   _ => IsPreview || IsDone);
        }

        // ── 폴더 선택 ───────────────────────────────────
        private void Browse()
        {
            // .NET 8 의 OpenFolderDialog 사용 (WPF 4.8+ 지원)
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "임포트할 폴더 선택",
            };
            if (dlg.ShowDialog() == true)
            {
                RootPath = dlg.FolderName;
                StatusMessage = $"선택된 폴더: {dlg.FolderName}";
            }
        }

        // ── 스캔 + 분류 (백그라운드) ────────────────────
        private async Task ScanAsync()
        {
            Candidates.Clear();
            ScannedCount = 0;
            Phase = ImportPhase.Scanning;
            StatusMessage = "스캔 중...";

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var items = await Task.Run(() =>
                    _scanner.Scan(_rootPath, token,
                        (count, _) => Application.Current.Dispatcher.Invoke(
                            () => ScannedCount = count)));

                if (token.IsCancellationRequested)
                {
                    StatusMessage = "스캔이 취소되었습니다.";
                    Phase = ImportPhase.SelectFolder;
                    return;
                }

                StatusMessage = $"분석 중... ({items.Count} 개 파일/폴더)";
                var found = await Task.Run(() => _classifier.Classify(items));

                // v3.0 F-002 hotfix: 자산 탭에서 호출 시 prefilter 적용
                // (해당 종류만 미리보기에 표시 — 다른 종류 자산은 임포트 대상에서 제외)
                if (!string.IsNullOrEmpty(PrefilterKind))
                {
                    var targetKind = PrefilterKind.ToLowerInvariant() switch
                    {
                        "code"       => ImportAssetKind.Code,
                        "model"      => ImportAssetKind.Model,
                        "document"   => ImportAssetKind.Document,
                        "experiment" => ImportAssetKind.Experiment,
                        _            => ImportAssetKind.Unknown,
                    };
                    if (targetKind != ImportAssetKind.Unknown)
                        found = found.Where(c => c.Kind == targetKind).ToList();
                }

                // 그리드에 채우기 (UI 스레드)
                foreach (var c in found)
                    Candidates.Add(c);

                if (Candidates.Count == 0)
                {
                    StatusMessage = string.IsNullOrEmpty(PrefilterKind)
                        ? "분류 가능한 자산을 찾지 못했습니다. 다른 폴더를 시도해보세요."
                        : $"이 폴더에서 [{PrefilterKindLabel}] 자산을 찾지 못했습니다. 다른 폴더를 시도해보세요.";
                    Phase = ImportPhase.SelectFolder;
                    return;
                }

                int high   = Candidates.Count(c => c.Confidence == ImportConfidence.High);
                int medium = Candidates.Count(c => c.Confidence == ImportConfidence.Medium);
                int low    = Candidates.Count(c => c.Confidence == ImportConfidence.Low);
                StatusMessage = $"발견 {Candidates.Count}건 — 높음 {high} · 중간 {medium} · 낮음 {low}";

                Phase = ImportPhase.Preview;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "스캔이 취소되었습니다.";
                Phase = ImportPhase.SelectFolder;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"스캔 중 오류:\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "스캔 실패.";
                Phase = ImportPhase.SelectFolder;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// SetAllSelection 호출 직후 발생 — View(코드비하인드)가 이를 구독해서
        /// DataGrid.CommitEdit + Items.Refresh 를 호출해 활성 셀 갱신 누락 문제를 해결한다.
        /// </summary>
        public event EventHandler? SelectionBulkChanged;

        private void SetAllSelection(bool value)
        {
            // ImportCandidate.IsSelected 가 INotifyPropertyChanged 를 구현하므로
            // 컬렉션 리빌드 없이 setter 호출만으로 그리드 갱신됨.
            foreach (var c in Candidates) c.IsSelected = value;

            // [확정 (DB 추가)] 버튼의 활성 상태가 IsSelected 카운트에 의존하므로
            // 명령 가능 여부를 강제로 다시 평가시킨다.
            CommandManager.InvalidateRequerySuggested();

            // ★ WPF DataGrid 의 활성 셀(현재 선택된 행)은 외부 setter 변경이
            //   즉시 반영되지 않는 알려진 이슈가 있으므로, View 에 신호를 보내
            //   CommitEdit + Items.Refresh 를 호출하게 한다.
            SelectionBulkChanged?.Invoke(this, EventArgs.Empty);

            StatusMessage = value
                ? $"전체 {Candidates.Count}건 선택됨"
                : "전체 선택 해제";
        }

        // ── DB 커밋 (Step 2.4 연계) ─────────────────────
        private async Task CommitAsync()
        {
            var selected = Candidates.Where(c => c.IsSelected
                                                  && c.Kind != ImportAssetKind.Unknown
                                                  && !c.IsDuplicate).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("커밋할 항목이 없습니다.\n선택된 항목 중 분류된(미분류 아닌) 신규 후보가 없습니다.",
                    "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"{selected.Count} 건을 DB 에 추가합니다. 계속하시겠습니까?\n\n" +
                $"(임포트 후에는 자동 롤백되지 않습니다.\n" +
                $" 필요 시 [💾 DB 백업] 으로 백업하시기 바랍니다.)",
                "임포트 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            Phase = ImportPhase.Committing;
            StatusMessage = "DB 에 추가 중...";

            // v3.0 F-002 Step 2.4: ImportCommitter 본 구현 활성화
            int projectId = _selectedProject?.Id ?? 0;

            var (ok, fail, log) = await Task.Run(() =>
                ImportCommitter.CommitAll(_db, selected, projectId));

            CommitSummary = log;
            StatusMessage = $"임포트 완료 — 성공 {ok} · 실패 {fail}";
            Phase = ImportPhase.Done;
        }

        // ── 폴더 선택 단계로 되돌리기 ────────────────────
        private void GoBackToSelectFolder()
        {
            Candidates.Clear();
            CommitSummary = "";
            ScannedCount = 0;
            StatusMessage = "임포트할 폴더를 선택하세요.";
            Phase = ImportPhase.SelectFolder;
        }
    }
}
