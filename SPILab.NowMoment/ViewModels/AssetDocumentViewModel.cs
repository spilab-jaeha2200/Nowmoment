// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetDocumentViewModel.cs — v4 문서/논문 화면 전용 VM
//
// CRUD 는 모두 v3.0 MainViewModel 명령을 그대로 패스스루:
//   • AddDocumentCommand    → OpenAddDocument()  → DocumentEditDialog (v3) + Audit
//   • EditDocumentCommand   → EditDocument(p)   → before-image + Audit.LogUpdate
//   • DeleteDocumentCommand → DeleteDocument(p) → 확인 + Audit.LogDelete
//
// 특화 사항:
//   • 그리드 컬럼: 제목 / 종류(DocType) / 버전 / 프로젝트 / 파일경로 / 등록일
//   • 필터: 프로젝트 + 종류 (paper/proposal/report/manual)
//   • 검색 키워드: DocKeyword
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
    public class SelectableDocument : INotifyPropertyChanged
    {
        public AssetDocument Source { get; }
        public SelectableDocument(AssetDocument src) { Source = src; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public int    Id          => Source.Id;
        public string Title       => Source.Title;
        public string DocType     => Source.DocType;
        public string Version     => Source.Version;
        public string ProjectName => Source.ProjectName;
        public string FilePath    => Source.FilePath;
        public DateTime CreatedAt => Source.CreatedAt;
        public DateTime UpdatedAt => Source.CreatedAt; // UpdatedAt 컬럼 미보유

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AssetDocumentViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;

        public AssetDocumentViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            Rows = new ObservableCollection<SelectableDocument>();
            ProjectFilters = new ObservableCollection<string> { "프로젝트: 전체" };
            DocTypeFilters = new ObservableCollection<string> { "종류: 전체" };

            _main.Documents.CollectionChanged += (_, __) => Reload();

            // ── v3 명령 패스스루 ──
            RefreshCommand   = new SimpleCommand(_ => _main.LoadDocuments());
            AddCommand       = _main.AddDocumentCommand;
            EditCommand      = new SimpleCommand(_ => InvokeV3Edit(),   _ => SelectedRow != null);
            DeleteCommand    = new SimpleCommand(_ => InvokeV3Delete(), _ => HasAnyChecked || SelectedRow != null);

            RowEditCommand   = _main.EditDocumentCommand;
            RowDeleteCommand = _main.DeleteDocumentCommand;

            ExportCommand    = _main.ExportExcelAllCommand;
            CatalogCommand   = _main.ExportPdfCatalogCommand;

            Reload();
        }

        public ObservableCollection<SelectableDocument> Rows { get; }
        public ObservableCollection<string> ProjectFilters { get; }
        public ObservableCollection<string> DocTypeFilters { get; }

        private SelectableDocument? _selectedRow;
        public SelectableDocument? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                _main.SelectedDocument = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailName));
                OnPropertyChanged(nameof(DetailDocType));
                OnPropertyChanged(nameof(DetailVersion));
                OnPropertyChanged(nameof(DetailProject));
                OnPropertyChanged(nameof(DetailFilePath));
                OnPropertyChanged(nameof(DetailSummary));
                OnPropertyChanged(nameof(SelectionSummary));
                (EditCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string Keyword
        {
            get => _main.DocKeyword;
            set { _main.DocKeyword = value; OnPropertyChanged(); }
        }

        private string _projectFilter = "프로젝트: 전체";
        public string ProjectFilter
        {
            get => _projectFilter;
            set { if (_projectFilter != value) { _projectFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
        }

        private string _docTypeFilter = "종류: 전체";
        public string DocTypeFilter
        {
            get => _docTypeFilter;
            set { if (_docTypeFilter != value) { _docTypeFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
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

        public string DetailTitle    => SelectedRow == null ? "선택된 자산이 없습니다" : $"선택 자산 상세 — {SelectedRow.Title}";
        public string DetailName     => SelectedRow?.Title ?? "—";
        public string DetailDocType  => SelectedRow?.DocType ?? "—";
        public string DetailVersion  => SelectedRow?.Version ?? "—";
        public string DetailProject  => SelectedRow?.ProjectName ?? "—";
        public string DetailFilePath => string.IsNullOrWhiteSpace(SelectedRow?.FilePath) ? "—" : SelectedRow!.FilePath;
        public string DetailSummary  => string.IsNullOrWhiteSpace(SelectedRow?.Source.Summary) ? "—" : SelectedRow!.Source.Summary;

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
            foreach (var d in _main.Documents)
                Rows.Add(new SelectableDocument(d));
            RebuildFilters();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        private void RebuildFilters()
        {
            var projects = _main.Documents.Select(d => d.ProjectName)
                                          .Where(s => !string.IsNullOrWhiteSpace(s))
                                          .Distinct().OrderBy(s => s).ToList();
            ProjectFilters.Clear();
            ProjectFilters.Add("프로젝트: 전체");
            foreach (var p in projects) ProjectFilters.Add(p);

            var types = _main.Documents.Select(d => d.DocType)
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Distinct().OrderBy(s => s).ToList();
            DocTypeFilters.Clear();
            DocTypeFilters.Add("종류: 전체");
            foreach (var t in types) DocTypeFilters.Add(t);
        }

        private void ApplyClientFilter()
        {
            Rows.Clear();
            IEnumerable<AssetDocument> q = _main.Documents;
            if (_projectFilter != "프로젝트: 전체")
                q = q.Where(d => string.Equals(d.ProjectName, _projectFilter, StringComparison.OrdinalIgnoreCase));
            if (_docTypeFilter != "종류: 전체")
                q = q.Where(d => string.Equals(d.DocType, _docTypeFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var d in q) Rows.Add(new SelectableDocument(d));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>상단 [✎ 수정] → v3 EditDocumentCommand → DocumentEditDialog.</summary>
        private void InvokeV3Edit()
        {
            if (SelectedRow == null) return;
            if (_main.EditDocumentCommand.CanExecute(SelectedRow.Source))
                _main.EditDocumentCommand.Execute(SelectedRow.Source);
        }

        /// <summary>상단 [✗ 삭제] → 체크된 행 일괄 또는 단일 행을 v3 DeleteDocumentCommand 호출.</summary>
        private void InvokeV3Delete()
        {
            var checkedRows = Rows.Where(r => r.IsSelected).ToList();
            if (checkedRows.Count > 0)
            {
                foreach (var r in checkedRows)
                {
                    if (_main.DeleteDocumentCommand.CanExecute(r.Source))
                        _main.DeleteDocumentCommand.Execute(r.Source);
                }
            }
            else if (SelectedRow != null)
            {
                if (_main.DeleteDocumentCommand.CanExecute(SelectedRow.Source))
                    _main.DeleteDocumentCommand.Execute(SelectedRow.Source);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
