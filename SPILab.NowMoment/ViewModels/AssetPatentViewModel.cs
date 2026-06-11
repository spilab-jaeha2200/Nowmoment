// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetPatentViewModel.cs — v4 특허/IP 화면 전용 VM
//
// CRUD 는 모두 v3.0 MainViewModel 명령을 그대로 패스스루:
//   • AddPatentCommand    → OpenAddPatent()  → PatentEditDialog (v3) + Audit
//   • EditPatentCommand   → EditPatent(p)   → before-image + Audit.LogUpdate
//   • DeletePatentCommand → DeletePatent(p) → 확인 + Audit.LogDelete
//
// 특화 사항:
//   • 그리드 컬럼: 제목 / 출원번호 / 상태 / 출원일 / 발명자 / 등록일
//   • 필터: 상태 (applied/registered/rejected/pending) — 프로젝트 컬럼 없음
//   • AssetPatent 모델은 ProjectName 컬럼이 없으므로 프로젝트 필터 제외
//   • 검색 키워드: PatentKeyword
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
    public class SelectablePatent : INotifyPropertyChanged
    {
        public AssetPatent Source { get; }
        public SelectablePatent(AssetPatent src) { Source = src; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public int    Id            => Source.Id;
        public string Title         => Source.Title;
        public string ApplicationNo => Source.ApplicationNo;
        public string Status        => Source.Status;
        public DateTime? FilingDate => Source.FilingDate;
        public string Inventors    => Source.Inventors;
        public DateTime CreatedAt  => Source.CreatedAt;
        public DateTime UpdatedAt  => Source.CreatedAt;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AssetPatentViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;

        public AssetPatentViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            Rows = new ObservableCollection<SelectablePatent>();
            StatusFilters = new ObservableCollection<string> { "상태: 전체" };

            _main.Patents.CollectionChanged += (_, __) => Reload();

            // ── v3 명령 패스스루 ──
            RefreshCommand   = new SimpleCommand(_ => _main.LoadPatents());
            AddCommand       = _main.AddPatentCommand;
            EditCommand      = new SimpleCommand(_ => InvokeV3Edit(),   _ => SelectedRow != null);
            DeleteCommand    = new SimpleCommand(_ => InvokeV3Delete(), _ => HasAnyChecked || SelectedRow != null);

            RowEditCommand   = _main.EditPatentCommand;
            RowDeleteCommand = _main.DeletePatentCommand;

            ExportCommand    = _main.ExportExcelAllCommand;
            CatalogCommand   = _main.ExportPdfCatalogCommand;

            Reload();
        }

        public ObservableCollection<SelectablePatent> Rows { get; }
        public ObservableCollection<string> StatusFilters { get; }

        private SelectablePatent? _selectedRow;
        public SelectablePatent? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                _main.SelectedPatent = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailName));
                OnPropertyChanged(nameof(DetailApplicationNo));
                OnPropertyChanged(nameof(DetailStatus));
                OnPropertyChanged(nameof(DetailFilingDate));
                OnPropertyChanged(nameof(DetailInventors));
                OnPropertyChanged(nameof(DetailDescription));
                OnPropertyChanged(nameof(SelectionSummary));
                (EditCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string Keyword
        {
            get => _main.PatentKeyword;
            set { _main.PatentKeyword = value; OnPropertyChanged(); }
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

        public string DetailTitle         => SelectedRow == null ? "선택된 자산이 없습니다" : $"선택 자산 상세 — {SelectedRow.Title}";
        public string DetailName          => SelectedRow?.Title ?? "—";
        public string DetailApplicationNo => string.IsNullOrWhiteSpace(SelectedRow?.ApplicationNo) ? "—" : SelectedRow!.ApplicationNo;
        public string DetailStatus        => SelectedRow?.Status ?? "—";
        public string DetailFilingDate    => SelectedRow?.FilingDate is DateTime d ? d.ToString("yyyy-MM-dd") : "—";
        public string DetailInventors     => string.IsNullOrWhiteSpace(SelectedRow?.Inventors) ? "—" : SelectedRow!.Inventors;
        public string DetailDescription   => string.IsNullOrWhiteSpace(SelectedRow?.Source.Description) ? "—" : SelectedRow!.Source.Description;

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
            foreach (var p in _main.Patents)
                Rows.Add(new SelectablePatent(p));
            RebuildFilters();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        private void RebuildFilters()
        {
            var statuses = _main.Patents.Select(p => p.Status)
                                        .Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct().OrderBy(s => s).ToList();
            StatusFilters.Clear();
            StatusFilters.Add("상태: 전체");
            foreach (var s in statuses) StatusFilters.Add(s);
        }

        private void ApplyClientFilter()
        {
            Rows.Clear();
            IEnumerable<AssetPatent> q = _main.Patents;
            if (_statusFilter != "상태: 전체")
                q = q.Where(p => string.Equals(p.Status, _statusFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var p in q) Rows.Add(new SelectablePatent(p));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>상단 [✎ 수정] → v3 EditPatentCommand → PatentEditDialog.</summary>
        private void InvokeV3Edit()
        {
            if (SelectedRow == null) return;
            if (_main.EditPatentCommand.CanExecute(SelectedRow.Source))
                _main.EditPatentCommand.Execute(SelectedRow.Source);
        }

        /// <summary>상단 [✗ 삭제] → 체크된 행 일괄 또는 단일 행을 v3 DeletePatentCommand 호출.</summary>
        private void InvokeV3Delete()
        {
            var checkedRows = Rows.Where(r => r.IsSelected).ToList();
            if (checkedRows.Count > 0)
            {
                foreach (var r in checkedRows)
                {
                    if (_main.DeletePatentCommand.CanExecute(r.Source))
                        _main.DeletePatentCommand.Execute(r.Source);
                }
            }
            else if (SelectedRow != null)
            {
                if (_main.DeletePatentCommand.CanExecute(SelectedRow.Source))
                    _main.DeletePatentCommand.Execute(SelectedRow.Source);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
