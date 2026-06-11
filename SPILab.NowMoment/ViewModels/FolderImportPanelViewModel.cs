// ════════════════════════════════════════════════════════════════════
// ViewModels/FolderImportPanelViewModel.cs — v4 폴더 임포트 화면 전용 VM
//
// v3 FolderImportViewModel 의 풍부한 phase machine 을 그대로 사용하되,
// v4 인라인 화면에 맞는 추가 표시(스캔 통계, 필터/충돌 콤보)를 제공.
//
// v3 명령 패스스루:
//   • BrowseRootCommand     → 폴더 다이얼로그
//   • ScanCommand           → 비동기 스캔 (Phase: SelectFolder → Scanning → Preview)
//   • CancelScanCommand     → 스캔 취소
//   • SelectAll / SelectNone → 일괄 체크
//   • CommitCommand         → DB 커밋 (Phase: Preview → Committing → Done)
//   • BackToSelectCommand   → "이전" 단계로 복귀
//
// v4 추가:
//   • CompletedScanLabel    → "스캔 완료: <root> → 파일 N개 · 자동 분류 N/N · 소요 1.2초"
//   • FilterKind / FilterDuplicate 콤보 (CollectionView 필터)
//   • SelectionStatusLabel  → "44건 선택 · 3건 중돌 (해결 필요)"
//   • BulkReassignCommand   → 체크된 행의 Kind 일괄 변경
//   • ResolveConflictsCommand → 중복(IsDuplicate=true) 행 자동 체크 해제
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public class FolderImportPanelViewModel : INotifyPropertyChanged
    {
        private readonly FolderImportViewModel _inner;
        private readonly Stopwatch _scanWatch = new();
        private double _lastScanSeconds;

        public FolderImportPanelViewModel(DatabaseService db)
        {
            _inner = new FolderImportViewModel(db, db.GetProjects());
            _inner.PropertyChanged += OnInnerPropertyChanged;
            _inner.Candidates.CollectionChanged += (_, __) => RefreshCounts();

            // CollectionView 로 필터 가능한 후보 컬렉션 노출
            CandidatesView = CollectionViewSource.GetDefaultView(_inner.Candidates);
            CandidatesView.Filter = FilterCandidate;

            FilterKindOptions = new ObservableCollection<string>
            {
                "필터: 전체", "소스코드", "AI 모델", "문서", "특허", "실험", "데이터", "미분류"
            };
            FilterDuplicateOptions = new ObservableCollection<string>
            {
                "충돌만", "중복 제외", "전체 표시"
            };
            _filterDuplicate = "전체 표시";

            // 일괄 재지정 옵션
            BulkAssignOptions = new ObservableCollection<string>
            {
                "소스코드", "AI 모델", "문서", "특허", "실험", "미분류"
            };

            // v4 신규 보조 명령
            BulkReassignCommand    = new SimpleCommand(o => BulkReassign(o as string), _ => HasCheckedCandidates);
            ResolveConflictsCommand = new SimpleCommand(_ => ResolveConflicts(), _ => HasConflicts);
        }

        // ── v3 phase / 명령 그대로 노출 ──
        public string RootPath
        {
            get => _inner.RootPath;
            set { _inner.RootPath = value; OnPropertyChanged(); }
        }
        public ObservableCollection<Project> Projects => _inner.Projects;
        public Project? SelectedProject
        {
            get => _inner.SelectedProject;
            set { _inner.SelectedProject = value; OnPropertyChanged(); }
        }
        public string StatusMessage => _inner.StatusMessage;
        public int ScannedCount => _inner.ScannedCount;
        public string CommitSummary => _inner.CommitSummary;

        public ImportPhase Phase => _inner.Phase;
        public bool IsSelectFolder => _inner.IsSelectFolder;
        public bool IsScanning     => _inner.IsScanning;
        public bool IsPreview      => _inner.IsPreview;
        public bool IsCommitting   => _inner.IsCommitting;
        public bool IsDone         => _inner.IsDone;

        // Phase 단계 진행 인디케이터 (1/2/3 표시)
        public int StepIndex
        {
            get
            {
                if (IsSelectFolder || IsScanning) return 1;
                if (IsPreview || IsCommitting)   return 2;
                if (IsDone)                       return 3;
                return 1;
            }
        }
        public bool Step1Done => StepIndex >= 2;
        public bool Step2Done => StepIndex >= 3;
        public bool Step1Active => StepIndex == 1;
        public bool Step2Active => StepIndex == 2;
        public bool Step3Active => StepIndex == 3;

        public ICollectionView CandidatesView { get; }
        public ObservableCollection<ImportCandidate> Candidates => _inner.Candidates;

        public ICommand BrowseRootCommand   => _inner.BrowseRootCommand;
        public ICommand ScanCommand         => _inner.ScanCommand;
        public ICommand CancelScanCommand   => _inner.CancelScanCommand;
        public ICommand SelectAllCommand    => _inner.SelectAllCommand;
        public ICommand SelectNoneCommand   => _inner.SelectNoneCommand;
        public ICommand CommitCommand       => _inner.CommitCommand;
        public ICommand BackToSelectCommand => _inner.BackToSelectCommand;

        // ── v4 신규 ──

        public ObservableCollection<string> FilterKindOptions      { get; }
        public ObservableCollection<string> FilterDuplicateOptions { get; }
        public ObservableCollection<string> BulkAssignOptions      { get; }

        private string _filterKind = "필터: 전체";
        public string FilterKind
        {
            get => _filterKind;
            set { if (_filterKind != value) { _filterKind = value; OnPropertyChanged(); CandidatesView.Refresh(); RefreshCounts(); } }
        }

        private string _filterDuplicate;
        public string FilterDuplicate
        {
            get => _filterDuplicate;
            set { if (_filterDuplicate != value) { _filterDuplicate = value; OnPropertyChanged(); CandidatesView.Refresh(); RefreshCounts(); } }
        }

        private string _bulkAssignTarget = "소스코드";
        public string BulkAssignTarget
        {
            get => _bulkAssignTarget;
            set { if (_bulkAssignTarget != value) { _bulkAssignTarget = value; OnPropertyChanged(); } }
        }

        // 스캔 완료 배너
        public string CompletedScanLabel
        {
            get
            {
                if (!IsPreview && !IsDone) return "";
                var classified = _inner.Candidates.Count(c => c.Kind != ImportAssetKind.Unknown);
                var total = _inner.Candidates.Count;
                return $"✓ 스캔 완료:  {_inner.RootPath}   →   파일 {total}개  ·  자동 분류 {classified}/{total}  ·  소요 {_lastScanSeconds:0.0}초";
            }
        }

        // "47개 항목 · 3개 충돌 감지"
        public string PreviewCountLabel
        {
            get
            {
                var total = _inner.Candidates.Count;
                var dup = _inner.Candidates.Count(c => c.IsDuplicate);
                return dup > 0 ? $"{total}개 항목  ·  {dup}개 충돌 감지" : $"{total}개 항목";
            }
        }

        // "44건 선택 · 3건 충돌 (해결 필요)"
        public string SelectionStatusLabel
        {
            get
            {
                var sel = _inner.Candidates.Count(c => c.IsSelected);
                var dup = _inner.Candidates.Count(c => c.IsDuplicate);
                if (dup > 0)
                    return $"{sel}건 선택  ·  {dup}건 충돌 (해결 필요)";
                return $"{sel}건 선택";
            }
        }

        public bool HasCheckedCandidates => _inner.Candidates.Any(c => c.IsSelected);
        public bool HasConflicts => _inner.Candidates.Any(c => c.IsDuplicate);

        public ICommand BulkReassignCommand     { get; }
        public ICommand ResolveConflictsCommand { get; }

        // ── 내부 ──

        private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // v3 변동을 그대로 전파
            OnPropertyChanged(e.PropertyName);
            // 단계 머신 관련 파생 속성
            if (e.PropertyName == nameof(FolderImportViewModel.Phase))
            {
                OnPropertyChanged(nameof(IsSelectFolder));
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(IsPreview));
                OnPropertyChanged(nameof(IsCommitting));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(StepIndex));
                OnPropertyChanged(nameof(Step1Done));
                OnPropertyChanged(nameof(Step2Done));
                OnPropertyChanged(nameof(Step1Active));
                OnPropertyChanged(nameof(Step2Active));
                OnPropertyChanged(nameof(Step3Active));

                if (e.PropertyName == nameof(FolderImportViewModel.Phase))
                {
                    // Scanning 진입 시 시간 측정 시작
                    if (_inner.IsScanning)
                    {
                        _scanWatch.Restart();
                    }
                    // Preview 진입 시 시간 측정 종료
                    else if (_inner.IsPreview && _scanWatch.IsRunning)
                    {
                        _scanWatch.Stop();
                        _lastScanSeconds = _scanWatch.Elapsed.TotalSeconds;
                        OnPropertyChanged(nameof(CompletedScanLabel));
                    }
                }
                RefreshCounts();
            }
        }

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(CompletedScanLabel));
            OnPropertyChanged(nameof(PreviewCountLabel));
            OnPropertyChanged(nameof(SelectionStatusLabel));
            OnPropertyChanged(nameof(HasCheckedCandidates));
            OnPropertyChanged(nameof(HasConflicts));
            (BulkReassignCommand     as SimpleCommand)?.RaiseCanExecuteChanged();
            (ResolveConflictsCommand as SimpleCommand)?.RaiseCanExecuteChanged();
        }

        private bool FilterCandidate(object obj)
        {
            if (obj is not ImportCandidate c) return false;

            // 충돌 필터
            switch (_filterDuplicate)
            {
                case "충돌만":   if (!c.IsDuplicate) return false; break;
                case "중복 제외": if (c.IsDuplicate)  return false; break;
            }
            // 종류 필터
            if (_filterKind == "필터: 전체") return true;
            var kindLabel = _filterKind switch
            {
                "소스코드" => ImportAssetKind.Code,
                "AI 모델" => ImportAssetKind.Model,
                "문서"    => ImportAssetKind.Document,
                "특허"    => ImportAssetKind.Document, // patent 는 Document 의 doctype 이라 동일 매핑
                "실험"    => ImportAssetKind.Experiment,
                "데이터"  => ImportAssetKind.Model,   // 데이터도 별도 모델 분류로 표시
                "미분류"  => ImportAssetKind.Unknown,
                _ => ImportAssetKind.Unknown,
            };
            return c.Kind == kindLabel;
        }

        private void BulkReassign(string? target)
        {
            if (string.IsNullOrEmpty(target)) target = BulkAssignTarget;
            var kind = target switch
            {
                "소스코드" => ImportAssetKind.Code,
                "AI 모델" => ImportAssetKind.Model,
                "문서"    => ImportAssetKind.Document,
                "특허"    => ImportAssetKind.Document,
                "실험"    => ImportAssetKind.Experiment,
                "미분류"  => ImportAssetKind.Unknown,
                _ => ImportAssetKind.Unknown,
            };
            foreach (var c in _inner.Candidates.Where(c => c.IsSelected))
            {
                c.Kind = kind;
                c.ConfidencePercent = 100;
            }
            CandidatesView.Refresh();
            RefreshCounts();
        }

        private void ResolveConflicts()
        {
            // 중복 항목 자동 체크 해제 → 커밋에서 제외
            foreach (var c in _inner.Candidates.Where(c => c.IsDuplicate))
                c.IsSelected = false;
            CandidatesView.Refresh();
            RefreshCounts();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
