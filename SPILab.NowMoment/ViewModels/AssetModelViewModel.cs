// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetModelViewModel.cs — v4 AI 모델/데이터 화면 전용 VM
//
// MainViewModel 의 Models 컬렉션과 명령(Add/Edit/Delete/Refresh)을
// 그대로 활용하면서, 화면 설계서 §4.1 의 3-영역 패턴에 필요한
// 추가 표시 요소(체크박스 다중선택, 필터, 상세 패널 텍스트)를 보강한다.
//
// AssetCodeViewModel 과 동일 구조 — 자산 종류만 AssetModel 로 교체.
//
// 표시 전용 보강:
//   • SelectableModel : 그리드 행 체크박스 바인딩용 래퍼
//   • ProjectFilters  : "프로젝트: 전체" 콤보용
//   • FrameworkFilters: "프레임워크: 전체" 콤보용 (PyTorch/sklearn/TF 등)
//   • SelectionSummary: 상태바 "12개 자산 / 1개 선택"
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>그리드 행에 체크박스를 부착하기 위한 래퍼.</summary>
    public class SelectableModel : INotifyPropertyChanged
    {
        public AssetModel Source { get; }
        public SelectableModel(AssetModel src) { Source = src; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        // ── 그리드 컬럼 바인딩 패스스루 ─────────────
        public int       Id          => Source.Id;
        public string    Name        => Source.Name;
        public string    Framework   => Source.Framework;
        public double?   Accuracy    => Source.Accuracy;
        // 그리드 표시용: 정확도를 % 문자열로 (없으면 빈 문자열)
        public string    AccuracyText => Source.Accuracy.HasValue
                                         ? $"{Source.Accuracy.Value * 100.0:0.0}%"
                                         : "—";
        public string    BaseModel   => Source.BaseModel;
        public string    ProjectName => Source.ProjectName;
        public string    FilePath    => Source.FilePath;
        public DateTime  CreatedAt   => Source.CreatedAt;
        // 모델에 UpdatedAt 컬럼이 없으므로 CreatedAt 으로 대체 (Phase 5 에서 컬럼 추가 예정)
        public DateTime  UpdatedAt   => Source.CreatedAt;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AssetModelViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;

        public AssetModelViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            Rows = new ObservableCollection<SelectableModel>();
            ProjectFilters   = new ObservableCollection<string> { "프로젝트: 전체" };
            FrameworkFilters = new ObservableCollection<string> { "프레임워크: 전체" };

            // MainViewModel 의 Models 컬렉션 변동을 추적해서 Rows 를 동기화
            _main.Models.CollectionChanged += (_, __) => Reload();

            // ── CRUD 명령: 모두 v3.0 MainViewModel 의 명령을 그대로 패스스루 ──
            //   • AddModelCommand    → OpenAddModel()    → ModelEditDialog (v3)
            //   • EditModelCommand   → EditModel(p)     → before-image + Audit.LogUpdate
            //   • DeleteModelCommand → DeleteModel(p)   → 확인 + Audit.LogDelete
            RefreshCommand     = new SimpleCommand(_ => _main.LoadModels());
            AddCommand         = _main.AddModelCommand;
            EditCommand        = new SimpleCommand(_ => InvokeV3Edit(),   _ => SelectedRow != null);
            DeleteCommand      = new SimpleCommand(_ => InvokeV3Delete(), _ => HasAnyChecked || SelectedRow != null);

            // 행별 컨텍스트 명령: 그리드 내부 [수정]/[삭제] 버튼이 직접 v3 명령에 바인딩
            //   CommandParameter="{Binding Source}" 로 AssetModel 을 그대로 넘긴다.
            RowEditCommand     = _main.EditModelCommand;
            RowDeleteCommand   = _main.DeleteModelCommand;

            ExportCommand      = _main.ExportExcelAllCommand;
            CatalogCommand     = _main.ExportPdfCatalogCommand;

            Reload();
        }

        // ── 컬렉션 ───────────────────────────────────────
        public ObservableCollection<SelectableModel> Rows { get; }
        public ObservableCollection<string> ProjectFilters   { get; }
        public ObservableCollection<string> FrameworkFilters { get; }

        // ── 선택 / 검색 / 필터 ───────────────────────────
        private SelectableModel? _selectedRow;
        public SelectableModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                _main.SelectedModel = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailName));
                OnPropertyChanged(nameof(DetailFramework));
                OnPropertyChanged(nameof(DetailAccuracy));
                OnPropertyChanged(nameof(DetailBaseModel));
                OnPropertyChanged(nameof(DetailProject));
                OnPropertyChanged(nameof(DetailFilePath));
                OnPropertyChanged(nameof(DetailDescription));
                OnPropertyChanged(nameof(SelectionSummary));
                (EditCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string Keyword
        {
            get => _main.ModelKeyword;
            set { _main.ModelKeyword = value; OnPropertyChanged(); }
        }

        private string _projectFilter = "프로젝트: 전체";
        public string ProjectFilter
        {
            get => _projectFilter;
            set { if (_projectFilter != value) { _projectFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
        }

        private string _frameworkFilter = "프레임워크: 전체";
        public string FrameworkFilter
        {
            get => _frameworkFilter;
            set { if (_frameworkFilter != value) { _frameworkFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
        }

        // ── 상태/요약 ────────────────────────────────────
        public bool HasSelection => SelectedRow != null;
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
                var ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                return $"{total}개 자산 / {sel}개 선택  |  마지막 갱신 {ts}";
            }
        }

        // ── 상세 패널 바인딩 ─────────────────────────────
        public string DetailTitle      => SelectedRow == null ? "선택된 자산이 없습니다" : $"선택 자산 상세 — {SelectedRow.Name}";
        public string DetailName       => SelectedRow?.Name ?? "—";
        public string DetailFramework  => SelectedRow?.Framework ?? "—";
        public string DetailAccuracy   => SelectedRow?.AccuracyText ?? "—";
        public string DetailBaseModel  => SelectedRow?.BaseModel ?? "—";
        public string DetailProject    => SelectedRow?.ProjectName ?? "—";
        public string DetailFilePath   => SelectedRow?.FilePath ?? "—";
        public string DetailDescription => SelectedRow?.Source.Description ?? "—";

        // ── Commands ─────────────────────────────────────
        public ICommand RefreshCommand   { get; }
        public ICommand AddCommand       { get; }
        public ICommand EditCommand      { get; }
        public ICommand DeleteCommand    { get; }
        public ICommand ExportCommand    { get; }
        public ICommand CatalogCommand   { get; }
        public ICommand RowEditCommand   { get; }
        public ICommand RowDeleteCommand { get; }

        // ── 내부 로직 ────────────────────────────────────
        private void Reload()
        {
            Rows.Clear();
            foreach (var m in _main.Models)
                Rows.Add(new SelectableModel(m));

            RebuildFilters();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        private void RebuildFilters()
        {
            var projects = _main.Models.Select(m => m.ProjectName)
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Distinct()
                                       .OrderBy(s => s)
                                       .ToList();
            ProjectFilters.Clear();
            ProjectFilters.Add("프로젝트: 전체");
            foreach (var p in projects) ProjectFilters.Add(p);

            var frameworks = _main.Models.Select(m => m.Framework)
                                         .Where(s => !string.IsNullOrWhiteSpace(s))
                                         .Distinct()
                                         .OrderBy(s => s)
                                         .ToList();
            FrameworkFilters.Clear();
            FrameworkFilters.Add("프레임워크: 전체");
            foreach (var fw in frameworks) FrameworkFilters.Add(fw);
        }

        private void ApplyClientFilter()
        {
            Rows.Clear();
            IEnumerable<AssetModel> q = _main.Models;
            if (_projectFilter != "프로젝트: 전체")
                q = q.Where(m => string.Equals(m.ProjectName, _projectFilter, StringComparison.OrdinalIgnoreCase));
            if (_frameworkFilter != "프레임워크: 전체")
                q = q.Where(m => string.Equals(m.Framework, _frameworkFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var m in q) Rows.Add(new SelectableModel(m));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>상단 툴바 [✎ 수정] 핸들러 → v3 EditModelCommand 호출 (ModelEditDialog).</summary>
        private void InvokeV3Edit()
        {
            if (SelectedRow == null) return;
            if (_main.EditModelCommand.CanExecute(SelectedRow.Source))
                _main.EditModelCommand.Execute(SelectedRow.Source);
        }

        /// <summary>상단 툴바 [✗ 삭제] 핸들러 → 체크된 행 일괄 또는 선택 행 1건 삭제.</summary>
        private void InvokeV3Delete()
        {
            var checkedRows = Rows.Where(r => r.IsSelected).ToList();
            if (checkedRows.Count > 0)
            {
                foreach (var r in checkedRows)
                {
                    if (_main.DeleteModelCommand.CanExecute(r.Source))
                        _main.DeleteModelCommand.Execute(r.Source);
                }
            }
            else if (SelectedRow != null)
            {
                if (_main.DeleteModelCommand.CanExecute(SelectedRow.Source))
                    _main.DeleteModelCommand.Execute(SelectedRow.Source);
            }
        }

        // ── INotifyPropertyChanged ───────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
