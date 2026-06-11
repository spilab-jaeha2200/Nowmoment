// ════════════════════════════════════════════════════════════════════
// ViewModels/KgGraphViewerViewModel.cs — v4 그래프 시각화 화면 전용 VM
//
// 중앙 그래프 영역은 v3 KgGraphView (UserControl) 를 그대로 호스팅하므로
// 노드/엣지 렌더링 자체는 v3 코드가 담당.
//
// 이 VM 의 역할:
//   1) 좌측 필터 패널 바인딩 (도메인 / 노드 타입 / 표시 옵션 / 노드 크기 모드)
//   2) 우측 선택 노드 상세 패널 바인딩 (선택 노드 메타 + 연결 자산 + 노드 편집 명령)
//   3) 상단 줌+/줌- / PNG / 레이아웃 리셋 명령 (v4 신규)
//
// v3 KgViewModel 의 EditNodeCommand 는 그대로 패스스루하므로 노드 편집은
// v3 KgNodeEditDialog 그대로 호출됨.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>좌측 도메인 체크박스 행 (다중 선택 표시용 래퍼).</summary>
    public class DomainFilter : INotifyPropertyChanged
    {
        public KgDomain Source { get; }
        public DomainFilter(KgDomain src, bool isChecked) { Source = src; _isChecked = isChecked; }

        // KgDomain 에는 Name 속성이 없으므로 Code(식별자) 와 DisplayName(표시명) 으로 노출.
        public string Code        => Source.Code;
        public string DisplayName => string.IsNullOrEmpty(Source.Label) ? Source.Code : Source.Label;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        }

        public event EventHandler? CheckedChanged;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>좌측 노드 타입 체크박스 행 (Rule / Entity / Material / Process 등 + 개수).</summary>
    public class NodeTypeFilter : INotifyPropertyChanged
    {
        public string Type  { get; }
        public int    Count { get; }
        public NodeTypeFilter(string type, int count, bool isChecked) { Type = type; Count = count; _isChecked = isChecked; }

        public string DisplayName => $"{Type} ({Count})";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        }

        public event EventHandler? CheckedChanged;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>우측 패널의 연결 자산 한 줄 (자산 종류 아이콘 + 자산명 + 링크 종류).</summary>
    public class LinkedAssetItem
    {
        public string AssetTypeLabel { get; init; } = "";
        public string AssetName      { get; init; } = "";
        public string LinkType       { get; init; } = "";
        public string Icon
        {
            get
            {
                return AssetTypeLabel switch
                {
                    "코드"     or "code"     or "asset_code"     => "📄",
                    "모델"     or "model"    or "asset_model"    => "🤖",
                    "문서"     or "document" or "asset_document" => "📜",
                    "특허"     or "patent"   or "asset_patent"   => "📑",
                    "실험"     or "experiment" or "asset_experiment" => "🔬",
                    _ => "🔗",
                };
            }
        }
    }

    public class KgGraphViewerViewModel : INotifyPropertyChanged
    {
        private readonly KgViewModel _kg;

        public KgGraphViewerViewModel(KgViewModel kg)
        {
            _kg = kg ?? throw new ArgumentNullException(nameof(kg));

            DomainFilters   = new ObservableCollection<DomainFilter>();
            NodeTypeFilters = new ObservableCollection<NodeTypeFilter>();
            LinkedAssets    = new ObservableCollection<LinkedAssetItem>();
            NodeSizeModes   = new ObservableCollection<string>
            {
                "차수(degree)",
                "균일",
                "타입별 고정",
            };            // v3 KgViewModel 변경 구독
            _kg.PropertyChanged += OnKgPropertyChanged;
            _kg.Domains.CollectionChanged          += (_, __) => RebuildDomainFilters();
            _kg.StatsByType.CollectionChanged      += (_, __) => RebuildNodeTypeFilters();
            _kg.LinkedAssetsForSelectedNode.CollectionChanged += (_, __) => RebuildLinkedAssets();

            // ── v3 명령 패스스루 ──
            EditNodeCommand = _kg.EditNodeCommand;
            RefreshCommand  = _kg.RefreshCommand;

            // ── v4 신규 보조 명령 ──
            // 줌/PNG/레이아웃 리셋은 KgGraphView 인스턴스를 모르는 VM 에서는 직접
            // 처리하기 어려우므로, View 측에서 핸들러를 붙여 처리한다.
            // VM 은 이벤트만 발행한다.
            ZoomInRequestCommand     = new SimpleCommand(_ => ZoomInRequested?.Invoke(this, EventArgs.Empty));
            ZoomOutRequestCommand    = new SimpleCommand(_ => ZoomOutRequested?.Invoke(this, EventArgs.Empty));
            ExportPngRequestCommand  = new SimpleCommand(_ => ExportPngRequested?.Invoke(this, EventArgs.Empty));
            RelayoutRequestCommand   = new SimpleCommand(_ => RelayoutRequested?.Invoke(this, EventArgs.Empty));

            RebuildDomainFilters();
            RebuildNodeTypeFilters();
            RebuildLinkedAssets();
        }

        // ── View 가 구독하는 사용자 액션 이벤트 ──
        public event EventHandler? ZoomInRequested;
        public event EventHandler? ZoomOutRequested;
        public event EventHandler? ExportPngRequested;
        public event EventHandler? RelayoutRequested;

        // ── 좌측 필터 컬렉션 ──
        public ObservableCollection<DomainFilter>    DomainFilters    { get; }
        public ObservableCollection<NodeTypeFilter>  NodeTypeFilters  { get; }
        public ObservableCollection<string>          NodeSizeModes    { get; }

        /// <summary>v3 KgGraphView 임베드 바인딩용 — 그래프 컨트롤이 KgViewModel 을 그대로 받아 자체 렌더링.</summary>
        public KgViewModel Kg => _kg;

        // ── 표시 옵션 ──
        private bool _showLabels = true;
        public bool ShowLabels { get => _showLabels; set { if (_showLabels != value) { _showLabels = value; OnPropertyChanged(); ShowLabelsChanged?.Invoke(this, value); } } }
        public event EventHandler<bool>? ShowLabelsChanged;

        private bool _showEdgeLabels = true;
        public bool ShowEdgeLabels { get => _showEdgeLabels; set { if (_showEdgeLabels != value) { _showEdgeLabels = value; OnPropertyChanged(); ShowEdgeLabelsChanged?.Invoke(this, value); } } }
        public event EventHandler<bool>? ShowEdgeLabelsChanged;

        private bool _showIsolated;
        public bool ShowIsolated { get => _showIsolated; set { if (_showIsolated != value) { _showIsolated = value; OnPropertyChanged(); ShowIsolatedChanged?.Invoke(this, value); } } }
        public event EventHandler<bool>? ShowIsolatedChanged;

        private string _nodeSizeMode = "차수(degree)";
        public string NodeSizeMode { get => _nodeSizeMode; set { if (_nodeSizeMode != value) { _nodeSizeMode = value; OnPropertyChanged(); NodeSizeModeChanged?.Invoke(this, value); } } }
        public event EventHandler<string>? NodeSizeModeChanged;

        // ── 우측 선택 노드 상세 ──
        public KgNode? SelectedNode => _kg.SelectedNode;

        public string HeaderTitle => _kg.SelectedNode == null ? "(선택 없음)" : _kg.SelectedNode.Id;
        public string HeaderSubtitle
        {
            get
            {
                if (_kg.SelectedNode == null) return "노드를 선택하세요";
                // _kg.SelectedDomain 은 도메인 코드(string). 빈 문자열이면 "—" 표시.
                var dom = string.IsNullOrEmpty(_kg.SelectedDomain) ? "—" : _kg.SelectedDomain;
                return $"타입: {_kg.SelectedNode.Type}  ·  도메인: {dom}";
            }
        }
        public string DetailLabel    => _kg.SelectedNode?.Label ?? "—";
        public string DetailDegree
        {
            get
            {
                if (_kg.SelectedNode == null) return "—";
                var id = _kg.SelectedNode.Id;
                var allEdges = _kg.GetAllEdges();
                int inDeg = allEdges.Count(e => e.DstId == id);
                int outDeg = allEdges.Count(e => e.SrcId == id);
                int total = inDeg + outDeg;
                return $"{total} (in:{inDeg}, out:{outDeg})";
            }
        }

        /// <summary>props JSON 에서 핵심 키 두 개 추출 (severity / version 등).</summary>
        public string DetailPropKey1Value => ExtractTopProp(0).val;
        public string DetailPropKey1Name  => ExtractTopProp(0).key;
        public string DetailPropKey2Value => ExtractTopProp(1).val;
        public string DetailPropKey2Name  => ExtractTopProp(1).key;

        public bool HasProp1 => !string.IsNullOrEmpty(DetailPropKey1Name);
        public bool HasProp2 => !string.IsNullOrEmpty(DetailPropKey2Name);

        public ObservableCollection<LinkedAssetItem> LinkedAssets { get; }
        public string LinkedAssetsHeader => LinkedAssets.Count == 0
            ? "🔗 연결 자산  (없음)"
            : $"🔗 연결 자산  ({LinkedAssets.Count}건)";

        public bool HasSelection => _kg.SelectedNode != null;

        // ── Commands ──
        public ICommand EditNodeCommand           { get; }
        public ICommand RefreshCommand            { get; }
        public ICommand ZoomInRequestCommand      { get; }
        public ICommand ZoomOutRequestCommand     { get; }
        public ICommand ExportPngRequestCommand   { get; }
        public ICommand RelayoutRequestCommand    { get; }

        // ── 내부 로직 ──

        private void OnKgPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(KgViewModel.SelectedNode):
                case nameof(KgViewModel.SelectedNodeProps):
                    OnPropertyChanged(nameof(SelectedNode));
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(HeaderTitle));
                    OnPropertyChanged(nameof(HeaderSubtitle));
                    OnPropertyChanged(nameof(DetailLabel));
                    OnPropertyChanged(nameof(DetailDegree));
                    OnPropertyChanged(nameof(DetailPropKey1Name));
                    OnPropertyChanged(nameof(DetailPropKey1Value));
                    OnPropertyChanged(nameof(DetailPropKey2Name));
                    OnPropertyChanged(nameof(DetailPropKey2Value));
                    OnPropertyChanged(nameof(HasProp1));
                    OnPropertyChanged(nameof(HasProp2));
                    break;
                case nameof(KgViewModel.SelectedDomain):
                    OnPropertyChanged(nameof(HeaderSubtitle));
                    SyncDomainFiltersFromKg();
                    break;
            }
        }

        private void RebuildDomainFilters()
        {
            // 기존 도메인 → 단일 선택 모드 (체크된 것 하나가 SelectedDomain)
            DomainFilters.Clear();
            var current = _kg.SelectedDomain ?? "";  // string (도메인 코드)
            foreach (var d in _kg.Domains)
            {
                var f = new DomainFilter(d, d.Code == current);
                f.CheckedChanged += OnDomainFilterChanged;
                DomainFilters.Add(f);
            }
        }

        private void SyncDomainFiltersFromKg()
        {
            var current = _kg.SelectedDomain ?? "";
            foreach (var f in DomainFilters)
                if (f.IsChecked != (f.Code == current))
                    f.IsChecked = (f.Code == current);
        }

        private void OnDomainFilterChanged(object? sender, EventArgs e)
        {
            // 라디오 동작: 가장 최근에 체크된 도메인만 활성
            if (sender is DomainFilter df && df.IsChecked)
            {
                foreach (var other in DomainFilters)
                    if (other != df && other.IsChecked) other.IsChecked = false;
                _kg.SelectedDomain = df.Source.Code;  // KgViewModel.SelectedDomain 은 string
            }
        }

        private void RebuildNodeTypeFilters()
        {
            NodeTypeFilters.Clear();
            foreach (var s in _kg.StatsByType)
            {
                var f = new NodeTypeFilter(s.Key, s.Count, true);
                f.CheckedChanged += OnNodeTypeFilterChanged;
                NodeTypeFilters.Add(f);
            }
        }

        private void OnNodeTypeFilterChanged(object? sender, EventArgs e)
        {
            // 체크된 타입 모음 → KgViewModel.TypeFilter 갱신
            //   v3 는 TypeFilter 가 "전체" 또는 단일 타입 문자열을 기대 (다중 선택 미지원)
            //   다중 체크 시: 모두 체크 → "전체", 하나만 체크 → 그 타입, 그 외 → "전체" 유지
            var checkedTypes = NodeTypeFilters.Where(t => t.IsChecked).Select(t => t.Type).ToList();
            if (checkedTypes.Count == NodeTypeFilters.Count || checkedTypes.Count == 0)
                _kg.TypeFilter = "전체";
            else if (checkedTypes.Count == 1)
                _kg.TypeFilter = checkedTypes[0];
            else
                _kg.TypeFilter = "전체"; // v3 다중 미지원 — 추후 KgViewModel 확장 시 콤마 구분 지원 가능
        }

        private void RebuildLinkedAssets()
        {
            LinkedAssets.Clear();
            foreach (var r in _kg.LinkedAssetsForSelectedNode)
            {
                LinkedAssets.Add(new LinkedAssetItem
                {
                    AssetTypeLabel = r.AssetTypeLabel,
                    AssetName      = r.AssetName,
                    LinkType       = r.LinkType,
                });
            }
            OnPropertyChanged(nameof(LinkedAssetsHeader));
        }

        /// <summary>SelectedNode.PropsJson 에서 N번째 키/값 추출 (간이 — 깊은 객체는 toString).</summary>
        private (string key, string val) ExtractTopProp(int index)
        {
            var raw = _kg.SelectedNode?.PropsJson;
            if (string.IsNullOrWhiteSpace(raw) || raw == "{}") return ("", "");
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return ("", "");
                int i = 0;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (i == index)
                    {
                        var val = prop.Value.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String  => prop.Value.GetString() ?? "",
                            System.Text.Json.JsonValueKind.Number  => prop.Value.GetRawText(),
                            System.Text.Json.JsonValueKind.True    => "true",
                            System.Text.Json.JsonValueKind.False   => "false",
                            System.Text.Json.JsonValueKind.Null    => "null",
                            _ => prop.Value.GetRawText(),
                        };
                        return (prop.Name, val);
                    }
                    i++;
                }
            }
            catch { /* JSON 파싱 실패 시 공백 반환 */ }
            return ("", "");
        }

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
