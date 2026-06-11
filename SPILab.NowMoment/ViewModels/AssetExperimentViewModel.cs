// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetExperimentViewModel.cs — v4 실험/측정 화면 전용 VM
//
// CRUD 는 모두 v3.0 MainViewModel 명령을 그대로 패스스루:
//   • AddExperimentCommand    → OpenAddExperiment()  → ExperimentEditDialog (v3) + Audit
//   • EditExperimentCommand   → EditExperiment(p)   → before-image + Audit.LogUpdate
//   • DeleteExperimentCommand → DeleteExperiment(p) → 확인 + Audit.LogDelete
//
// 특화 사항:
//   • 그리드 컬럼: 실험명 / 자산참조 / 상태 / 결과경로 / 등록일
//   • 필터: 상태 (running/completed/failed 등) — 프로젝트 컬럼 없음
//   • AssetExperiment 모델은 ProjectName 컬럼이 없으므로 프로젝트 필터 제외
//   • 검색 키워드: ExpKeyword
//   • Params / Metrics 는 JSON 문자열 → 상세 패널에서 일부 미리보기
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>그리드 행 체크박스용 래퍼.</summary>
    public class SelectableExperiment : INotifyPropertyChanged
    {
        public AssetExperiment Source { get; }
        public SelectableExperiment(AssetExperiment src) { Source = src; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public int    Id         => Source.Id;
        public string Name       => Source.Name;
        public string AssetRef   => Source.AssetRef;
        public string Status     => Source.Status;
        public string ResultPath => Source.ResultPath;
        public DateTime CreatedAt => Source.CreatedAt;
        public DateTime UpdatedAt => Source.CreatedAt;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AssetExperimentViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;

        public AssetExperimentViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            Rows = new ObservableCollection<SelectableExperiment>();
            StatusFilters = new ObservableCollection<string> { "상태: 전체" };

            _main.Experiments.CollectionChanged += (_, __) => Reload();

            // ── v3 명령 패스스루 ──
            RefreshCommand   = new SimpleCommand(_ => _main.LoadExperiments());
            AddCommand       = _main.AddExperimentCommand;
            EditCommand      = new SimpleCommand(_ => InvokeV3Edit(),   _ => SelectedRow != null);
            DeleteCommand    = new SimpleCommand(_ => InvokeV3Delete(), _ => HasAnyChecked || SelectedRow != null);

            RowEditCommand   = _main.EditExperimentCommand;
            RowDeleteCommand = _main.DeleteExperimentCommand;

            ExportCommand    = _main.ExportExcelAllCommand;
            CatalogCommand   = _main.ExportPdfCatalogCommand;

            Reload();
        }

        public ObservableCollection<SelectableExperiment> Rows { get; }
        public ObservableCollection<string> StatusFilters { get; }

        private SelectableExperiment? _selectedRow;
        public SelectableExperiment? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                _main.SelectedExperiment = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailName));
                OnPropertyChanged(nameof(DetailAssetRef));
                OnPropertyChanged(nameof(DetailStatus));
                OnPropertyChanged(nameof(DetailResultPath));
                OnPropertyChanged(nameof(DetailParams));
                OnPropertyChanged(nameof(DetailMetrics));
                OnPropertyChanged(nameof(SelectionSummary));
                (EditCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string Keyword
        {
            get => _main.ExpKeyword;
            set { _main.ExpKeyword = value; OnPropertyChanged(); }
        }

        private string _statusFilter = "상태: 전체";
        public string StatusFilter
        {
            get => _statusFilter;
            set { if (_statusFilter != value) { _statusFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
        }

        public bool HasSelection  => SelectedRow != null;
        public bool HasAnyChecked => Rows.Any(r => r.IsSelected);

        /// <summary>헤더 체크박스 → 전체 행 선택/해제 일괄 토글.</summary>
        public void SetAllSelected(bool selected)
        {
            foreach (var r in Rows)
                r.IsSelected = selected;
            OnPropertyChanged(nameof(HasAnyChecked));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        public string SelectionSummary
        {
            get
            {
                var total = Rows.Count;
                var sel = Rows.Count(r => r.IsSelected);
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                return $"{total}개 자산 / {sel}개 선택  |  마지막 갱신 {ts}";
            }
        }

        public string DetailTitle      => SelectedRow == null ? "선택된 자산이 없습니다" : $"선택 자산 상세 — {SelectedRow.Name}";
        public string DetailName       => SelectedRow?.Name ?? "—";
        public string DetailAssetRef   => string.IsNullOrWhiteSpace(SelectedRow?.AssetRef) ? "—" : SelectedRow!.AssetRef;
        public string DetailStatus     => SelectedRow?.Status ?? "—";
        public string DetailResultPath => string.IsNullOrWhiteSpace(SelectedRow?.ResultPath) ? "—" : SelectedRow!.ResultPath;
        public string DetailParams     => Compact(SelectedRow?.Source.Params);
        public string DetailMetrics    => Compact(SelectedRow?.Source.Metrics);

        /// <summary>JSON 문자열을 한 줄 미리보기로 압축 (최대 80자).</summary>
        private static string Compact(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "{}") return "—";
            var oneline = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (oneline.Contains("  ")) oneline = oneline.Replace("  ", " ");
            return oneline.Length > 80 ? oneline.Substring(0, 80) + "…" : oneline;
        }

        public ICommand RefreshCommand   { get; }
        public ICommand AddCommand       { get; }
        public ICommand EditCommand      { get; }
        public ICommand DeleteCommand    { get; }
        public ICommand ExportCommand    { get; }
        public ICommand CatalogCommand   { get; }
        public ICommand RowEditCommand   { get; }
        public ICommand RowDeleteCommand { get; }

        private void Reload()
        {
            Rows.Clear();
            foreach (var e in _main.Experiments)
                Rows.Add(new SelectableExperiment(e));
            RebuildFilters();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        private void RebuildFilters()
        {
            var statuses = _main.Experiments.Select(e => e.Status)
                                            .Where(s => !string.IsNullOrWhiteSpace(s))
                                            .Distinct().OrderBy(s => s).ToList();
            StatusFilters.Clear();
            StatusFilters.Add("상태: 전체");
            foreach (var s in statuses) StatusFilters.Add(s);
        }

        private void ApplyClientFilter()
        {
            Rows.Clear();
            IEnumerable<AssetExperiment> q = _main.Experiments;
            if (_statusFilter != "상태: 전체")
                q = q.Where(e => string.Equals(e.Status, _statusFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var e in q) Rows.Add(new SelectableExperiment(e));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>상단 [✎ 수정] → v3 EditExperimentCommand → ExperimentEditDialog.</summary>
        private void InvokeV3Edit()
        {
            if (SelectedRow == null) return;
            if (_main.EditExperimentCommand.CanExecute(SelectedRow.Source))
                _main.EditExperimentCommand.Execute(SelectedRow.Source);
        }

        /// <summary>상단 [✗ 삭제] → 체크된 행 일괄 또는 단일 행을 v3 DeleteExperimentCommand 호출.</summary>
        private void InvokeV3Delete()
        {
            var checkedRows = Rows.Where(r => r.IsSelected).ToList();
            if (checkedRows.Count > 0)
            {
                foreach (var r in checkedRows)
                {
                    if (_main.DeleteExperimentCommand.CanExecute(r.Source))
                        _main.DeleteExperimentCommand.Execute(r.Source);
                }
            }
            else if (SelectedRow != null)
            {
                if (_main.DeleteExperimentCommand.CanExecute(SelectedRow.Source))
                    _main.DeleteExperimentCommand.Execute(SelectedRow.Source);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
