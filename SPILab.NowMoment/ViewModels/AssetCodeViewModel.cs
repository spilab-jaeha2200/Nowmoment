// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetCodeViewModel.cs — v4 소스코드/모듈 화면 전용 VM
//
// MainViewModel 의 Codes 컬렉션과 명령(Add/Edit/Delete/Refresh)을
// 그대로 활용하면서, 화면 설계서 §4.1 의 3-영역 패턴에 필요한
// 추가 표시 요소(체크박스 다중선택, 필터, 상세 패널 텍스트)를 보강한다.
//
// 표시 전용 보강:
//   • SelectableCode  : 그리드 행 체크박스 바인딩용 래퍼
//   • ProjectFilters  : "프로젝트: 전체" 콤보용
//   • TagFilters      : "태그: 전체" 콤보용
//   • SelectionSummary: 상태바 "12개 자산 / 1개 선택"
//   • DetailLines     : 상세 패널 우측 KG 노드 표시 (자리 표시자)
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
    public class SelectableCode : INotifyPropertyChanged
    {
        public AssetCode Source { get; }
        public SelectableCode(AssetCode src) { Source = src; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        // ── 그리드 컬럼 바인딩 패스스루 ─────────────
        public int    Id          => Source.Id;
        public string Name        => Source.Name;
        public string Language    => Source.Language;
        public string Version     => Source.Version;
        public string ProjectName => Source.ProjectName;
        public string Tags        => Source.Tags;
        public DateTime CreatedAt => Source.CreatedAt;
        // 모델에 UpdatedAt 컬럼이 없으므로 CreatedAt 으로 대체 (Phase 5 에서 컬럼 추가 예정)
        public DateTime UpdatedAt => Source.CreatedAt;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AssetCodeViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;

        public AssetCodeViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            Rows = new ObservableCollection<SelectableCode>();
            ProjectFilters = new ObservableCollection<string> { "프로젝트: 전체" };
            TagFilters     = new ObservableCollection<string> { "태그: 전체" };

            // MainViewModel 의 Codes 컬렉션 변동을 추적해서 Rows 를 동기화
            _main.Codes.CollectionChanged += (_, __) => Reload();

            // ── CRUD 명령: 모두 v3.0 MainViewModel 의 명령을 그대로 패스스루 ──
            //   • AddCodeCommand    → OpenAddCode()    → CodeEditDialog (v3)
            //   • EditCodeCommand   → EditCode(p)     → before-image + Audit.LogUpdate
            //   • DeleteCodeCommand → DeleteCode(p)   → 확인 + Audit.LogDelete
            //
            //   v3 명령은 CommandParameter 로 AssetCode 인스턴스를 기대하므로,
            //   상단 툴바 버튼(인자 없음)을 위한 얇은 래퍼만 제공한다.
            //   - 등록은 인자가 필요 없으므로 v3 명령을 직접 노출
            //   - 수정/삭제는 SelectedRow.Source(AssetCode) 또는 체크된 행 일괄 처리
            RefreshCommand     = new SimpleCommand(_ => _main.LoadCodes());
            AddCommand         = _main.AddCodeCommand;
            EditCommand        = new SimpleCommand(_ => InvokeV3Edit(),   _ => SelectedRow != null);
            DeleteCommand      = new SimpleCommand(_ => InvokeV3Delete(), _ => HasAnyChecked || SelectedRow != null);

            // 행별 컨텍스트 명령: 그리드 내부 [수정]/[삭제] 버튼이 직접 v3 명령에 바인딩
            //   CommandParameter="{Binding Source}" 로 AssetCode 를 그대로 넘긴다.
            RowEditCommand     = _main.EditCodeCommand;
            RowDeleteCommand   = _main.DeleteCodeCommand;

            ExportCommand      = _main.ExportExcelAllCommand;
            CatalogCommand     = _main.ExportPdfCatalogCommand;

            Reload();
        }

        // ── 컬렉션 ───────────────────────────────────────
        public ObservableCollection<SelectableCode> Rows { get; }
        public ObservableCollection<string> ProjectFilters { get; }
        public ObservableCollection<string> TagFilters { get; }

        // ── 선택 / 검색 / 필터 ───────────────────────────
        private SelectableCode? _selectedRow;
        public SelectableCode? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                _main.SelectedCode = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailName));
                OnPropertyChanged(nameof(DetailLanguage));
                OnPropertyChanged(nameof(DetailVersion));
                OnPropertyChanged(nameof(DetailProject));
                OnPropertyChanged(nameof(DetailRepo));
                OnPropertyChanged(nameof(DetailLicense));
                OnPropertyChanged(nameof(DetailDescription));
                OnPropertyChanged(nameof(SelectionSummary));
                (EditCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string Keyword
        {
            get => _main.CodeKeyword;
            set { _main.CodeKeyword = value; OnPropertyChanged(); }
        }

        private string _projectFilter = "프로젝트: 전체";
        public string ProjectFilter
        {
            get => _projectFilter;
            set { if (_projectFilter != value) { _projectFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
        }

        private string _tagFilter = "태그: 전체";
        public string TagFilter
        {
            get => _tagFilter;
            set { if (_tagFilter != value) { _tagFilter = value; OnPropertyChanged(); ApplyClientFilter(); } }
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
        public string DetailLanguage   => SelectedRow?.Language ?? "—";
        public string DetailVersion    => SelectedRow?.Version ?? "—";
        public string DetailProject    => SelectedRow?.ProjectName ?? "—";
        public string DetailRepo       => SelectedRow?.Source.RepoUrl ?? "—";
        // License 는 v4 컬럼 (현재 모델 미보유): RepoUrl 도메인 추론 또는 기본값
        public string DetailLicense    => "SPILab Internal";
        public string DetailDescription => SelectedRow?.Source.Description ?? "—";

        // ── Commands ─────────────────────────────────────
        public ICommand RefreshCommand   { get; }
        public ICommand AddCommand       { get; }
        public ICommand EditCommand      { get; }
        public ICommand DeleteCommand    { get; }
        public ICommand ExportCommand    { get; }
        public ICommand CatalogCommand   { get; }

        // 그리드 행별 [수정]/[삭제] 버튼이 직접 바인딩 (v3 명령 그대로)
        public ICommand RowEditCommand   { get; }
        public ICommand RowDeleteCommand { get; }

        // ── 내부 로직 ────────────────────────────────────
        private void Reload()
        {
            // Rows 재구성
            Rows.Clear();
            foreach (var c in _main.Codes)
                Rows.Add(new SelectableCode(c));

            // 필터 목록 재계산
            RebuildFilters();
            OnPropertyChanged(nameof(SelectionSummary));
        }

        private void RebuildFilters()
        {
            // 프로젝트
            var projects = _main.Codes.Select(c => c.ProjectName)
                                      .Where(s => !string.IsNullOrWhiteSpace(s))
                                      .Distinct()
                                      .OrderBy(s => s)
                                      .ToList();
            ProjectFilters.Clear();
            ProjectFilters.Add("프로젝트: 전체");
            foreach (var p in projects) ProjectFilters.Add(p);

            // 태그 (콤마 분리, # 접두 무시)
            var tags = _main.Codes
                .SelectMany(c => (c.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(s => s.Trim().TrimStart('#'))
                .Where(s => s.Length > 0)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            TagFilters.Clear();
            TagFilters.Add("태그: 전체");
            foreach (var t in tags) TagFilters.Add("#" + t);
        }

        private void ApplyClientFilter()
        {
            // 단순 클라이언트 필터: Rows 를 재계산해서 보이는 항목만 남김
            Rows.Clear();
            IEnumerable<AssetCode> q = _main.Codes;
            if (_projectFilter != "프로젝트: 전체")
                q = q.Where(c => string.Equals(c.ProjectName, _projectFilter, StringComparison.OrdinalIgnoreCase));
            if (_tagFilter != "태그: 전체")
            {
                var needle = _tagFilter.TrimStart('#');
                q = q.Where(c => (c.Tags ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => string.Equals(t.Trim().TrimStart('#'), needle, StringComparison.OrdinalIgnoreCase)));
            }
            foreach (var c in q) Rows.Add(new SelectableCode(c));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        /// <summary>
        /// 상단 툴바 [✎ 수정] 버튼 핸들러.
        /// v3 의 EditCodeCommand 를 호출하여 v3 CodeEditDialog 를 띄운다.
        /// CommandCanExecute(SelectedRow != null) 로 보호되어 있어 SelectedRow 는 not-null 보장.
        /// </summary>
        private void InvokeV3Edit()
        {
            if (SelectedRow == null) return;
            // v3 명령은 CommandParameter 로 AssetCode 인스턴스를 받는다 (MainViewModel.EditCode 시그니처).
            if (_main.EditCodeCommand.CanExecute(SelectedRow.Source))
                _main.EditCodeCommand.Execute(SelectedRow.Source);
        }

        /// <summary>
        /// 상단 툴바 [✗ 삭제] 버튼 핸들러.
        /// 체크된 행이 있으면 일괄 삭제(각 행마다 v3 DeleteCodeCommand 호출 = 행마다 확인 다이얼로그).
        /// 체크된 행이 없으면 현재 선택 행 1건만 삭제.
        /// 일괄 삭제 시 사용자가 "전체 일괄 확인" UX 를 원한다면 추후 Audit 일괄 모드 추가 예정.
        /// </summary>
        private void InvokeV3Delete()
        {
            var checkedRows = Rows.Where(r => r.IsSelected).ToList();
            if (checkedRows.Count > 0)
            {
                foreach (var r in checkedRows)
                {
                    if (_main.DeleteCodeCommand.CanExecute(r.Source))
                        _main.DeleteCodeCommand.Execute(r.Source);
                }
            }
            else if (SelectedRow != null)
            {
                if (_main.DeleteCodeCommand.CanExecute(SelectedRow.Source))
                    _main.DeleteCodeCommand.Execute(SelectedRow.Source);
            }
        }

        // ── INotifyPropertyChanged ───────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>경량 RelayCommand (이미 프로젝트 다른 곳에 RelayCommand 존재할 경우 이름 충돌 회피용).</summary>
    public class SimpleCommand : ICommand
    {
        private readonly Action<object?> _exec;
        private readonly Func<object?, bool>? _can;
        public SimpleCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
        public bool CanExecute(object? p) => _can == null || _can(p);
        public void Execute(object? p) => _exec(p);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
