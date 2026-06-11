// ════════════════════════════════════════════════════════════════════
// ViewModels/KgDomainViewModel.cs — v4 도메인 관리 화면 (SCR-B07)
//
// v3 KgViewModel.Domains 컬렉션을 그대로 표시.
// 추가/삭제는 v3 명령 패스스루:
//   • AddDomainCommand    → KgDomainEditDialog → KgDomainService.Add
//   • DeleteDomainCommand → 확인 → KgDomainService.Delete + 도메인 비우기
//
// v4 보강:
//   • 도메인 행 래퍼 (KgDomain + 노드/엣지 통계)
//   • IsBuiltIn 플래그 표시 (빌트인 도메인은 삭제 불가)
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>그리드 행 표시용 도메인 래퍼 (통계 포함).</summary>
    public class DomainRow : INotifyPropertyChanged
    {
        public KgDomain Source { get; }

        public DomainRow(KgDomain src, int nodes, int edges)
        {
            Source = src;
            _nodes = nodes;
            _edges = edges;
        }

        public string Code           => Source.Code;
        public string Label          => Source.Label;
        public string BuilderKind    => Source.BuilderKind;
        public string BuilderScript  => Source.BuilderScript;
        public string OutputBasename => Source.OutputBasename;
        public bool   IsBuiltIn      => Source.IsBuiltIn;
        public DateTime CreatedAt    => Source.CreatedAt;

        public string BuiltInBadge => Source.IsBuiltIn ? "빌트인" : "사용자";

        private int _nodes;
        public int Nodes
        {
            get => _nodes;
            set { if (_nodes != value) { _nodes = value; OnPropertyChanged(); } }
        }

        private int _edges;
        public int Edges
        {
            get => _edges;
            set { if (_edges != value) { _edges = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class KgDomainViewModel : INotifyPropertyChanged
    {
        private readonly KgViewModel _kg;
        private readonly KnowledgeGraphService? _service;

        public KgDomainViewModel(KgViewModel kg, KnowledgeGraphService? service = null)
        {
            _kg = kg ?? throw new ArgumentNullException(nameof(kg));
            _service = service;

            Rows = new ObservableCollection<DomainRow>();

            _kg.Domains.CollectionChanged += (_, __) => RebuildRows();

            // v3 명령 패스스루
            AddDomainCommand    = _kg.AddDomainCommand;
            DeleteDomainCommand = _kg.DeleteDomainCommand;
            ClearDomainCommand  = _kg.ClearDomainCommand;
            RefreshCommand      = new SimpleCommand(_ => { _kg.ReloadDomains(); RebuildRows(); });

            RebuildRows();
        }

        public ObservableCollection<DomainRow> Rows { get; }

        private DomainRow? _selectedRow;
        public DomainRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (_selectedRow == value) return;
                _selectedRow = value;
                // v3 SelectedDomain 동기화 → DeleteDomainCommand 가 정확한 도메인 인식.
                // KgViewModel.SelectedDomain 은 도메인 코드(string) 이므로 .Code 전달.
                _kg.SelectedDomain = value?.Source?.Code ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedHeader));
                OnPropertyChanged(nameof(SelectedBuilderKindText));
                OnPropertyChanged(nameof(SelectedBuilderScript));
                OnPropertyChanged(nameof(SelectedOutputBasename));
                OnPropertyChanged(nameof(SelectedStats));
                OnPropertyChanged(nameof(IsSelectionDeletable));
            }
        }

        public bool HasSelection => _selectedRow != null;
        public bool IsSelectionDeletable => _selectedRow != null && !_selectedRow.IsBuiltIn;

        public string SelectedHeader => _selectedRow == null
            ? "(선택된 도메인 없음)"
            : $"🗂  {_selectedRow.Label}  ·  {_selectedRow.Code}  [{_selectedRow.BuiltInBadge}]";

        public string SelectedBuilderKindText => _selectedRow == null ? "—" : _selectedRow.BuilderKind;
        public string SelectedBuilderScript   => _selectedRow == null || string.IsNullOrEmpty(_selectedRow.BuilderScript)
            ? "—" : _selectedRow.BuilderScript;
        public string SelectedOutputBasename  => _selectedRow == null || string.IsNullOrEmpty(_selectedRow.OutputBasename)
            ? "—" : _selectedRow.OutputBasename;
        public string SelectedStats => _selectedRow == null
            ? "—" : $"노드 {_selectedRow.Nodes}개  ·  엣지 {_selectedRow.Edges}개";

        public string TotalLabel => $"총 {Rows.Count}개 도메인";

        public ICommand AddDomainCommand    { get; }
        public ICommand DeleteDomainCommand { get; }
        public ICommand ClearDomainCommand  { get; }
        public ICommand RefreshCommand      { get; }

        // ── 내부 ──

        private void RebuildRows()
        {
            var prevCode = SelectedRow?.Code;
            Rows.Clear();
            foreach (var d in _kg.Domains)
            {
                var (n, e) = QueryStats(d.Code);
                Rows.Add(new DomainRow(d, n, e));
            }
            // 선택 복원
            if (prevCode != null)
            {
                foreach (var r in Rows)
                    if (r.Code == prevCode) { SelectedRow = r; break; }
            }
            OnPropertyChanged(nameof(TotalLabel));
        }

        private (int nodes, int edges) QueryStats(string domain)
        {
            if (_service == null) return (0, 0);
            try
            {
                var s = _service.GetStats("", domain);
                return (s.Nodes, s.Edges);
            }
            catch
            {
                return (0, 0);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
