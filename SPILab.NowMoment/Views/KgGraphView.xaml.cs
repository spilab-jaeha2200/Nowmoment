// ════════════════════════════════════════════════════════════════════
// KgGraphView.xaml.cs (v2.5)
//
// WPF Canvas 위에 KG 노드/엣지를 force-directed 레이아웃으로 그리는
// UserControl.
//
// 주요 기능:
//   * Fruchterman-Reingold 레이아웃 (가벼운 force-directed)
//   * 노드 색상: 타입별 (PhysicsRule/Material/Workspace/Parameter/Spec/Citation)
//   * 줌 (마우스 휠), 팬 (배경 드래그)
//   * 노드 드래그 (개별 노드 위치 수동 조정)
//   * 노드 클릭 → KgViewModel.SelectedNode 동기화
//
// 외부 라이브러리 의존성 없음 — 표준 WPF (.NET 8) 만 사용.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class KgGraphView : UserControl
    {
        // 노드 타입별 색상
        private static readonly Dictionary<string, Brush> TypeBrush = new()
        {
            { "PhysicsRule",  new SolidColorBrush(Color.FromRgb(0xE8, 0x55, 0x77)) },
            { "Material",     new SolidColorBrush(Color.FromRgb(0x2E, 0xA4, 0xC7)) },
            { "ProcessParam", new SolidColorBrush(Color.FromRgb(0x2E, 0xA4, 0xC7)) },
            { "Workspace",    new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x72)) },
            { "Parameter",    new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38)) },
            { "Spec",         new SolidColorBrush(Color.FromRgb(0x8B, 0x6F, 0xBF)) },
            { "Citation",     new SolidColorBrush(Color.FromRgb(0x9D, 0xB4, 0xCC)) },
        };
        private static readonly Brush DefaultNodeBrush = new SolidColorBrush(Color.FromRgb(0x9D, 0xB4, 0xCC));
        private static readonly Brush EdgeBrush = new SolidColorBrush(Color.FromArgb(0xD0, 0xB0, 0xC8, 0xDC));
        private static readonly Brush EdgeLabelBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
        private static readonly Brush SelectedStrokeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

        // v3.0 F-001 Step 1.6: 자산이 링크된 노드에 강조 테두리
        private static readonly Brush AssetLinkedStrokeBrush =
            new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));   // 진한 주황 (Accent #FFB300)
        private HashSet<string> _assetLinkedIds = new();

        // 노드 위치 + 시각적 객체
        private class NodeVisual
        {
            public KgNode Data = null!;
            public double X, Y;       // 캔버스 좌표
            public double Vx, Vy;     // 속도 (시뮬레이션용)
            public Ellipse Shape = null!;
            public TextBlock Label = null!;
        }

        private readonly Dictionary<string, NodeVisual> _nodes = new();
        private readonly List<Line> _edgeLines = new();
        private List<KgEdge> _allEdges = new();   // 전체 엣지 (선택과 무관)
        private List<KgEdge> _visibleEdges = new(); // 현재 화면에 그릴 엣지 (1-hop)
        private HashSet<string> _visibleNodeIds = new();  // 현재 화면에 그릴 노드 ID
        private KgViewModel? _vm;
        private NodeVisual? _draggingNode;
        private Point _dragStartCanvasPos;
        private bool _isPanning;
        private Point _panStart;
        private double _panStartX, _panStartY;

        // 캔버스 가상 크기 (XAML 의 Canvas.Width/Height 와 동기화)
        private const double CanvasW = 2000;
        private const double CanvasH = 1500;

        public KgGraphView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => TryRedraw();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 이전 VM 의 컬렉션 변경 구독 해제
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged -= OnCollectionChanged;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            }
            _vm = DataContext as KgViewModel;
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged += OnCollectionChanged;
                _vm.PropertyChanged += OnVmPropertyChanged;
            }
            TryRedraw();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 노드/엣지 수가 바뀌면 전체 재배치
            Dispatcher.BeginInvoke(new Action(TryRedraw));
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(KgViewModel.SelectedNode))
            {
                // 선택이 바뀌면 1-hop 그래프를 다시 그린다
                Dispatcher.BeginInvoke(new Action(TryRedraw));
            }
        }

        // ── 그리기 ─────────────────────────────────────
        private void TryRedraw()
        {
            if (_vm == null || !IsLoaded) return;
            GraphCanvas.Children.Clear();
            _nodes.Clear();
            _edgeLines.Clear();

            // 전체 엣지 로드 (KgViewModel.Edges 는 선택 노드 인접만 담으므로 사용 불가)
            _allEdges = _vm.GetAllEdges();

            // v3.0 F-001 Step 1.6: 자산-링크된 KG 노드 ID 캐시 갱신
            _assetLinkedIds = _vm.GetLinkedKgNodeIds();

            if (_vm.Nodes.Count == 0)
            {
                ShowEmptyMessage("표시할 KG 노드가 없습니다.\nKG 탭에서 임포트 또는 빌드 후 다시 시도하세요.");
                TxtStatus.Text = "노드 0개";
                return;
            }

            // 범위에 맞는 노드/엣지 부분집합 계산 (선택 노드의 1-hop 이웃)
            ComputeVisibleSet();
            if (_visibleNodeIds.Count == 0)
            {
                ShowEmptyMessage("좌측 목록에서 노드를 선택하세요.\n선택한 노드와 직접 연결된 이웃들이 그래프로 표시됩니다.");
                TxtStatus.Text = "노드 선택 필요";
                return;
            }

            BuildVisuals();
            DrawAll();

            TxtStatus.Text = $"중심 노드 + 1-hop · 노드 {_visibleNodeIds.Count}개 · 엣지 {_visibleEdges.Count}개";
        }

        // 선택 노드 + 1-hop 이웃 만 _visibleNodeIds / _visibleEdges 에 채운다.
        private void ComputeVisibleSet()
        {
            _visibleNodeIds.Clear();
            _visibleEdges.Clear();
            if (_vm == null) return;

            var sel = _vm.SelectedNode?.Id;
            if (string.IsNullOrEmpty(sel)) return;   // 선택 없으면 빈 상태

            _visibleNodeIds.Add(sel);
            // 1-hop 이웃
            foreach (var e in _allEdges)
            {
                if (e.SrcId == sel) _visibleNodeIds.Add(e.DstId);
                else if (e.DstId == sel) _visibleNodeIds.Add(e.SrcId);
            }

            // 가시 노드 사이의 엣지만 (선택 노드와 직접 연결된 엣지만 보임)
            foreach (var e in _allEdges)
            {
                if (_visibleNodeIds.Contains(e.SrcId) && _visibleNodeIds.Contains(e.DstId))
                    _visibleEdges.Add(e);
            }
        }

        private void ShowEmptyMessage(string message)
        {
            var tb = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                FontFamily = new FontFamily("맑은 고딕"),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(tb, CanvasW / 2 - 200);
            Canvas.SetTop(tb, CanvasH / 2 - 30);
            GraphCanvas.Children.Add(tb);
        }

        private void BuildVisuals()
        {
            // 스타 레이아웃 — 중심 노드 + 이웃들을 균등 각도로 배치
            // 라벨이 우측으로 길게 뻗으므로 중심을 약간 좌측에 둬서 잘림 방지.
            // 캔버스 상단 공간을 활용하기 위해 cy 도 약간 위쪽(45%)에 둠.
            double cx = CanvasW * 0.42;
            double cy = CanvasH * 0.42;

            var selId = _vm!.SelectedNode?.Id;
            var visibleNodes = _vm.Nodes.Where(n => _visibleNodeIds.Contains(n.Id)).ToList();

            // 중심 노드를 0번으로 분리, 나머지는 이웃
            var center = visibleNodes.FirstOrDefault(n => n.Id == selId);
            var neighbors = visibleNodes.Where(n => n.Id != selId).ToList();

            // 반지름 — 이웃 수에 따라 동적 조절 (한눈에 들어오게 압축)
            //   1~3개: 매우 가깝게 → 단번에 인식
            //   4~6개: 가까이
            //   7~12개: 적당
            //   13개+: 펼침 (라벨 겹침 회피)
            int n = neighbors.Count;
            double rRatio = n <= 3 ? 0.16
                          : n <= 6 ? 0.20
                          : n <= 12 ? 0.25
                          : 0.30;
            double radius = Math.Min(CanvasW, CanvasH) * rRatio;

            // 1) 중심 노드 — 캔버스 정중앙에 배치
            if (center != null)
                AddNodeVisual(center, cx, cy, isCenter: true);

            // 2) 이웃들 — 원주에 균등 각도로 배치
            //    각도는 -π/2 부터 시작 (위쪽 12시 방향), 시계 방향
            for (int i = 0; i < n; i++)
            {
                double angle = -Math.PI / 2 + (2 * Math.PI * i / Math.Max(1, n));
                double x = cx + radius * Math.Cos(angle);
                double y = cy + radius * Math.Sin(angle);
                AddNodeVisual(neighbors[i], x, y, isCenter: false);
            }
        }

        // 노드의 연결 차수(degree) 계산 — 화면에 보이는 엣지 기준
        private int NodeDegree(string nodeId)
        {
            int d = 0;
            foreach (var e in _visibleEdges)
            {
                if (e.SrcId == nodeId) d++;
                if (e.DstId == nodeId) d++;
            }
            return d;
        }

        private void AddNodeVisual(KgNode node, double x, double y, bool isCenter)
        {
            var brush = TypeBrush.TryGetValue(node.Type, out var b) ? b : DefaultNodeBrush;

            // 노드 크기 — 차수(degree)에 비례. 최소 38, 최대 66.
            //   degree 0~1 → 38, 그 이상은 점진 확대. 중심 노드는 +6 보너스.
            int deg = NodeDegree(node.Id);
            double size = 38 + Math.Min(deg, 12) * 2.3;
            if (isCenter) size += 6;

            var ellipse = new Ellipse
            {
                Width = size, Height = size,
                Fill = brush,
                Stroke = isCenter
                    ? SelectedStrokeBrush
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                StrokeThickness = isCenter ? 3.5 : 2.5,
                Cursor = Cursors.Hand,
                Tag = node.Id,
            };
            ellipse.MouseLeftButtonDown += Node_MouseLeftButtonDown;

            // 라벨 — "이름 (타입)" 형식, 노드 아래 중앙 배치
            var label = new TextBlock
            {
                Text = $"{node.Label}  ({node.Type})",
                FontFamily = new FontFamily("맑은 고딕"),
                FontSize = isCenter ? 13 : 12,
                FontWeight = isCenter ? FontWeights.Bold : FontWeights.SemiBold,
                IsHitTestVisible = false,
            };
            // ★ 다크모드 가독성 수정: 어두운색으로 하드코딩돼 있던 글자색을
            //   테마 리소스 Theme.FgPrimary 동적 참조로 변경 → 라이트/다크
            //   전환 시 글자색이 자동으로 따라가 어두운 캔버스에서도 보인다.
            label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.FgPrimary");

            _nodes[node.Id] = new NodeVisual
            {
                Data = node, X = x, Y = y, Vx = 0, Vy = 0,
                Shape = ellipse, Label = label,
            };
        }

        // ── Force-directed 레이아웃 (Fruchterman-Reingold 간소화) ─
        private void DrawAll()
        {
            // 1) 엣지 (선) — 노드 아래에 그려야 노드가 위에 보임
            foreach (var e in _visibleEdges)
            {
                if (!_nodes.TryGetValue(e.SrcId, out var a)) continue;
                if (!_nodes.TryGetValue(e.DstId, out var b)) continue;
                var line = new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = EdgeBrush, StrokeThickness = 3.5,
                    IsHitTestVisible = false,
                };
                line.Tag = (e.SrcId, e.DstId);
                _edgeLines.Add(line);
                GraphCanvas.Children.Add(line);
            }

            // 2) 엣지 라벨 (USES, CITES 등) — 선 가운데에 작은 텍스트
            foreach (var e in _visibleEdges)
            {
                if (!_nodes.TryGetValue(e.SrcId, out var a)) continue;
                if (!_nodes.TryGetValue(e.DstId, out var b)) continue;
                double mx = (a.X + b.X) / 2;
                double my = (a.Y + b.Y) / 2;

                // 라벨 배경 (반투명 어두운 배경으로 가독성 확보)
                var bg = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xDC, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 1, 3, 1),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = e.Rel,
                        Foreground = EdgeLabelBrush,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                    },
                };
                bg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(bg, mx - bg.DesiredSize.Width / 2);
                Canvas.SetTop(bg, my - bg.DesiredSize.Height / 2);
                GraphCanvas.Children.Add(bg);
            }

            // 3) 노드 + 라벨 — 노드는 원, 라벨은 노드 아래 중앙
            foreach (var v in _nodes.Values)
            {
                Canvas.SetLeft(v.Shape, v.X - v.Shape.Width / 2);
                Canvas.SetTop(v.Shape, v.Y - v.Shape.Height / 2);
                GraphCanvas.Children.Add(v.Shape);

                // 라벨 위치: 노드와 중심 사이의 방향 벡터로 결정
                v.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lw = v.Label.DesiredSize.Width;

                double lx, ly;
                // 모든 노드: 라벨을 노드 바로 아래 중앙에 배치 (목표 이미지 스타일)
                lx = v.X - lw / 2;
                ly = v.Y + v.Shape.Height / 2 + 6;
                Canvas.SetLeft(v.Label, lx);
                Canvas.SetTop(v.Label, ly);
                GraphCanvas.Children.Add(v.Label);
            }

            UpdateSelectionVisual();
        }

        private void UpdateSelectionVisual()
        {
            var selId = _vm?.SelectedNode?.Id;
            var defaultStroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            foreach (var v in _nodes.Values)
            {
                bool selected = (v.Data.Id == selId);
                bool assetLinked = _assetLinkedIds.Contains(v.Data.Id);

                // 우선순위: 선택 > 자산 링크 > 기본
                if (selected)
                {
                    v.Shape.Stroke = SelectedStrokeBrush;
                    v.Shape.StrokeThickness = 3;
                }
                else if (assetLinked)
                {
                    v.Shape.Stroke = AssetLinkedStrokeBrush;
                    v.Shape.StrokeThickness = 2.5;
                    v.Shape.ToolTip = "🔗 자산이 연결된 노드";
                }
                else
                {
                    v.Shape.Stroke = defaultStroke;
                    v.Shape.StrokeThickness = 1.5;
                    v.Shape.ToolTip = null;
                }
            }
        }

        // ── 노드 드래그 ────────────────────────────────
        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse el || el.Tag is not string id) return;
            if (!_nodes.TryGetValue(id, out var nv)) return;

            // VM 의 SelectedNode 설정
            if (_vm != null)
            {
                var match = _vm.Nodes.FirstOrDefault(n => n.Id == id);
                if (match != null) _vm.SelectedNode = match;
            }

            _draggingNode = nv;
            _dragStartCanvasPos = e.GetPosition(GraphCanvas);
            GraphCanvas.CaptureMouse();
            e.Handled = true;
        }

        // ── 캔버스 팬 ──────────────────────────────────
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode != null) return;
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panStartX = GraphPan.X;
            _panStartY = GraphPan.Y;
            GraphCanvas.CaptureMouse();
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggingNode = null;
            _isPanning = false;
            GraphCanvas.ReleaseMouseCapture();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingNode != null)
            {
                var p = e.GetPosition(GraphCanvas);
                _draggingNode.X = p.X;
                _draggingNode.Y = p.Y;
                Canvas.SetLeft(_draggingNode.Shape, p.X - _draggingNode.Shape.Width / 2);
                Canvas.SetTop(_draggingNode.Shape, p.Y - _draggingNode.Shape.Height / 2);
                // 라벨 위치 — 노드 바로 아래 중앙 (정적 배치와 동일)
                double lw = _draggingNode.Label.DesiredSize.Width;
                Canvas.SetLeft(_draggingNode.Label, p.X - lw / 2);
                Canvas.SetTop(_draggingNode.Label, p.Y + _draggingNode.Shape.Height / 2 + 6);
                // 인접 엣지 선 끝점 갱신
                foreach (var line in _edgeLines)
                {
                    if (line.Tag is not ValueTuple<string, string> tup) continue;
                    if (tup.Item1 == _draggingNode.Data.Id) { line.X1 = p.X; line.Y1 = p.Y; }
                    if (tup.Item2 == _draggingNode.Data.Id) { line.X2 = p.X; line.Y2 = p.Y; }
                }
            }
            else if (_isPanning)
            {
                var p = e.GetPosition(this);
                GraphPan.X = _panStartX + (p.X - _panStart.X);
                GraphPan.Y = _panStartY + (p.Y - _panStart.Y);
            }
        }

        // ── 줌 ────────────────────────────────────────
        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : (1.0 / 1.1);
            double newScale = GraphScale.ScaleX * factor;
            newScale = Math.Max(0.2, Math.Min(3.0, newScale));
            GraphScale.ScaleX = newScale;
            GraphScale.ScaleY = newScale;
            TxtZoom.Text = $"{(int)(newScale * 100)}%";
            e.Handled = true;
        }

        // ── 버튼: 재배치 ──────────────────────────────
        private void BtnRelayout_Click(object sender, RoutedEventArgs e) => TryRedraw();

        // ── 버튼: 화면 맞춤 (줌/팬 리셋) ──────────────
        private void BtnFit_Click(object sender, RoutedEventArgs e)
        {
            GraphScale.ScaleX = 1; GraphScale.ScaleY = 1;
            GraphPan.X = 0; GraphPan.Y = 0;
            TxtZoom.Text = "100%";
        }
    }
}
