// ════════════════════════════════════════════════════════════════════
// ViewModels/KgNodesEdgesViewModel.cs — v4 KG 노드/엣지 편집 화면 전용 VM
//
// CRUD 는 모두 v3.0 KgViewModel 명령을 그대로 패스스루:
//   • AddNodeCommand     → KG 노드 추가 다이얼로그 (KgNodeEditDialog)
//   • EditNodeCommand    → KG 노드 편집 다이얼로그
//   • DeleteNodeCommand  → 확인 + 인접 엣지/자산링크 동반 삭제
//   • AddEdgeCommand     → KG 엣지 추가 다이얼로그 (KgEdgeEditDialog)
//   • DeleteEdgeCommand  → 엣지 삭제
//   • RefreshCommand     → DB 재조회
//
// 화면 설계서 SCR-B01 (Image 1) 표시 보강:
//   • NodeRow (차수 포함): 인접 엣지 수를 카운트하여 표시
//   • EdgeRow (src → dst 표기): "photo:R6 → photo:Wafer" 형식 문자열 보강
//   • PropsEditor: 선택 노드의 props JSON 을 편집 → JSON 검증 → 저장 → 되돌리기
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>그리드 행 표시용 노드 래퍼 (차수 포함).</summary>
    public class NodeRow : INotifyPropertyChanged
    {
        public KgNode Source { get; }
        private int _degree;

        public NodeRow(KgNode src, int degree) { Source = src; _degree = degree; }

        public string Id     => Source.Id;
        public string Type   => Source.Type;
        public string Label  => Source.Label;
        public int    Degree { get => _degree; set { if (_degree != value) { _degree = value; OnPropertyChanged(); } } }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>그리드 행 표시용 엣지 래퍼 (src → dst 표기).</summary>
    public class EdgeRow : INotifyPropertyChanged
    {
        public KgEdge Source { get; }

        public EdgeRow(KgEdge src) { Source = src; }

        public int    Id       => Source.Id;
        public string SrcDst   => $"{Source.SrcId} → {Source.DstId}";
        public string Rel      => Source.Rel;
        public string SrcLabel => Source.SrcLabel;
        public string DstLabel => Source.DstLabel;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class KgNodesEdgesViewModel : INotifyPropertyChanged
    {
        private readonly KgViewModel _kg;
        private readonly KnowledgeGraphService? _kgService;

        public KgNodesEdgesViewModel(KgViewModel kg, KnowledgeGraphService? kgService = null)
        {
            _kg = kg ?? throw new ArgumentNullException(nameof(kg));
            _kgService = kgService;

            NodeRows = new ObservableCollection<NodeRow>();
            EdgeRows = new ObservableCollection<EdgeRow>();

            // v3 KgViewModel 의 Nodes/Edges 변경을 추적해서 v4 행 컬렉션 동기화
            _kg.Nodes.CollectionChanged += (_, __) => RebuildNodeRows();
            _kg.Edges.CollectionChanged += (_, __) => { RebuildEdgeRows(); RecomputeDegrees(); };
            _kg.PropertyChanged += OnKgPropertyChanged;

            // ── v3 명령 패스스루 ──
            //   v3 KgViewModel 의 명령들은 내부적으로 SelectedNode 등을 참조하므로
            //   여기서는 명령 객체 자체를 그대로 노출.
            AddNodeCommand    = _kg.AddNodeCommand;
            EditNodeCommand   = _kg.EditNodeCommand;
            DeleteNodeCommand = _kg.DeleteNodeCommand;
            AddEdgeCommand    = _kg.AddEdgeCommand;
            DeleteEdgeCommand = _kg.DeleteEdgeCommand;
            RefreshCommand    = _kg.RefreshCommand;

            // props 편집 명령
            ValidateJsonCommand = new SimpleCommand(_ => ValidatePropsJson(), _ => SelectedNodeRow != null);
            SaveJsonCommand     = new SimpleCommand(_ => SavePropsJson(),     _ => SelectedNodeRow != null && _kgService != null);
            RevertJsonCommand   = new SimpleCommand(_ => RevertPropsJson(),   _ => SelectedNodeRow != null);

            RebuildNodeRows();
            RebuildEdgeRows();
            RecomputeDegrees();
        }

        // ── 도메인 / 필터 / 검색 (v3 바인딩 패스스루) ──
        public ObservableCollection<KgDomain>      Domains       => _kg.Domains;
        public ObservableCollection<string>        NodeTypes     => _kg.NodeTypes;
        public ObservableCollection<NodeRow>       NodeRows      { get; }
        public ObservableCollection<EdgeRow>       EdgeRows      { get; }

        /// <summary>노드 그리드 헤더 체크박스 → 전체 노드 행 선택/해제.</summary>
        public void SetAllNodesSelected(bool selected)
        {
            foreach (var r in NodeRows)
                r.IsSelected = selected;
        }

        /// <summary>엣지 그리드 헤더 체크박스 → 전체 엣지 행 선택/해제.</summary>
        public void SetAllEdgesSelected(bool selected)
        {
            foreach (var r in EdgeRows)
                r.IsSelected = selected;
        }

        // latest KgViewModel.SelectedDomain 은 도메인 코드(string) 를 보관한다.
        // 본 VM 의 XAML 콤보박스는 KgDomain 객체를 바인딩하므로,
        // string ↔ KgDomain 변환 어댑터 역할을 한다.
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
                OnPropertyChanged(nameof(NodesHeader));
                OnPropertyChanged(nameof(EdgesHeader));
            }
        }

        public string TypeFilter
        {
            get => _kg.TypeFilter;
            set { _kg.TypeFilter = value; OnPropertyChanged(); }
        }

        public string Keyword
        {
            get => _kg.Keyword;
            set { _kg.Keyword = value; OnPropertyChanged(); }
        }

        // ── 선택 노드 ──
        private NodeRow? _selectedNodeRow;
        public NodeRow? SelectedNodeRow
        {
            get => _selectedNodeRow;
            set
            {
                if (_selectedNodeRow == value) return;
                _selectedNodeRow = value;
                // v3 KgViewModel.SelectedNode 동기화 → v3 명령들이 정확한 노드를 인식
                _kg.SelectedNode = value?.Source;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(PropsEditorHeader));
                OnPropertyChanged(nameof(PropsJson));
                _propsJsonDirty = false;
                _propsJsonOriginal = value?.Source.PropsJson ?? "";
                OnPropertyChanged(nameof(ValidationStatus));
                _validationStatus = "";
                (ValidateJsonCommand as SimpleCommand)?.RaiseCanExecuteChanged();
                (SaveJsonCommand     as SimpleCommand)?.RaiseCanExecuteChanged();
                (RevertJsonCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool HasSelection => SelectedNodeRow != null;

        // ── 헤더 텍스트 (Image 1 의 "노드 (총 N개)", "엣지 (총 N개, photo 도메인)") ──
        public string NodesHeader => $"📍 노드  (총 {NodeRows.Count}개)";
        public string EdgesHeader
        {
            get
            {
                // KgDomain 에는 Name 속성이 없으므로 Label (없으면 Code) 사용
                var d = SelectedDomain;
                var dom = d == null ? "전체" : (string.IsNullOrEmpty(d.Label) ? d.Code : d.Label);
                return $"🔗 엣지  (총 {EdgeRows.Count}개, {dom} 도메인)";
            }
        }
        public string PropsEditorHeader
            => SelectedNodeRow == null
                ? "선택 노드 props (JSON) — (선택 없음)"
                : $"선택 노드 props (JSON)  —  {SelectedNodeRow.Id}";

        // ── props JSON 편집 ──
        private string _propsJsonOriginal = "";
        private bool   _propsJsonDirty;
        private string _propsJsonEdit = "";

        public string PropsJson
        {
            get
            {
                if (_propsJsonDirty) return _propsJsonEdit;
                return PrettyJson(SelectedNodeRow?.Source.PropsJson ?? "");
            }
            set
            {
                _propsJsonEdit = value;
                _propsJsonDirty = true;
                OnPropertyChanged();
                (SaveJsonCommand   as SimpleCommand)?.RaiseCanExecuteChanged();
                (RevertJsonCommand as SimpleCommand)?.RaiseCanExecuteChanged();
            }
        }

        private string _validationStatus = "";
        public string ValidationStatus
        {
            get => _validationStatus;
            private set { if (_validationStatus != value) { _validationStatus = value; OnPropertyChanged(); } }
        }

        public ICommand AddNodeCommand     { get; }
        public ICommand EditNodeCommand    { get; }
        public ICommand DeleteNodeCommand  { get; }
        public ICommand AddEdgeCommand     { get; }
        public ICommand DeleteEdgeCommand  { get; }
        public ICommand RefreshCommand     { get; }
        public ICommand ValidateJsonCommand { get; }
        public ICommand SaveJsonCommand    { get; }
        public ICommand RevertJsonCommand  { get; }

        // ── 내부 로직 ──

        /// <summary>v3 KgViewModel.Nodes → v4 NodeRows 재구성.</summary>
        private void RebuildNodeRows()
        {
            var prevSelectedId = SelectedNodeRow?.Id;
            NodeRows.Clear();
            foreach (var n in _kg.Nodes)
                NodeRows.Add(new NodeRow(n, ComputeDegree(n.Id)));
            // 선택 복원
            if (prevSelectedId != null)
                SelectedNodeRow = NodeRows.FirstOrDefault(r => r.Id == prevSelectedId);
            OnPropertyChanged(nameof(NodesHeader));
        }

        private void RebuildEdgeRows()
        {
            EdgeRows.Clear();
            foreach (var e in _kg.Edges) EdgeRows.Add(new EdgeRow(e));
            OnPropertyChanged(nameof(EdgesHeader));
        }

        /// <summary>모든 노드의 차수를 다시 계산.</summary>
        private void RecomputeDegrees()
        {
            foreach (var row in NodeRows)
                row.Degree = ComputeDegree(row.Id);
        }

        /// <summary>특정 노드의 인접 엣지 수 (출입 모두).</summary>
        private int ComputeDegree(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0;
            int c = 0;
            foreach (var e in _kg.Edges)
                if (e.SrcId == nodeId || e.DstId == nodeId) c++;
            return c;
        }

        private void OnKgPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // ★ v4 버그수정: 다른 탭(임포트/빌더)에서 _kg.SelectedDomain 이 바뀌면
            //   노드 화면의 도메인 콤보박스(SelectedDomain 어댑터)도 함께 갱신해야 한다.
            //   기존에는 EdgesHeader 만 갱신해서, 콤보박스 표시값과 실제 그리드 내용이
            //   어긋나는 문제가 있었다 (예: 콤보는 photo 인데 그리드는 cs 0개).
            if (e.PropertyName == nameof(KgViewModel.SelectedDomain))
            {
                OnPropertyChanged(nameof(SelectedDomain));   // 콤보박스 선택 동기화
                OnPropertyChanged(nameof(NodesHeader));
                OnPropertyChanged(nameof(EdgesHeader));
                // _kg.Nodes/Edges 는 KgViewModel.Reload() 가 이미 도메인 기준으로
                // 다시 채우지만, 그 호출 순서가 보장되지 않는 경우를 대비해
                // 현재 _kg 컬렉션 기준으로 행을 재구성한다.
                RebuildNodeRows();
                RebuildEdgeRows();
                RecomputeDegrees();
            }
        }

        // ── props JSON 편집 ──

        private void ValidatePropsJson()
        {
            var src = _propsJsonDirty ? _propsJsonEdit : (SelectedNodeRow?.Source.PropsJson ?? "");
            try
            {
                using var _ = JsonDocument.Parse(src);
                ValidationStatus = "✅ 유효한 JSON";
            }
            catch (JsonException ex)
            {
                ValidationStatus = "❌ JSON 오류: " + ex.Message;
            }
        }

        private void SavePropsJson()
        {
            if (SelectedNodeRow == null || _kgService == null) return;
            var src = _propsJsonDirty ? _propsJsonEdit : SelectedNodeRow.Source.PropsJson;
            try
            {
                using var _ = JsonDocument.Parse(src); // 검증 먼저
            }
            catch (JsonException ex)
            {
                ValidationStatus = "❌ 저장 실패 — JSON 오류: " + ex.Message;
                return;
            }
            try
            {
                _kgService.UpdateNodeProps(SelectedNodeRow.Id, src);
                SelectedNodeRow.Source.PropsJson = src;
                _propsJsonOriginal = src;
                _propsJsonDirty = false;
                ValidationStatus = "💾 저장 완료";
                OnPropertyChanged(nameof(PropsJson));
            }
            catch (Exception ex)
            {
                ValidationStatus = "❌ 저장 실패: " + ex.Message;
            }
        }

        private void RevertPropsJson()
        {
            _propsJsonDirty = false;
            _propsJsonEdit = "";
            ValidationStatus = "↶ 되돌렸습니다";
            OnPropertyChanged(nameof(PropsJson));
        }

        /// <summary>JSON 들여쓰기 (실패 시 원본 그대로).</summary>
        private static string PrettyJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { return raw; }
        }

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
