// ════════════════════════════════════════════════════════════════════
// DashboardView.xaml.cs — v4 Phase 5 (홈 대시보드)
//
// 도넛 차트는 DataBinding 만으로는 Arc 좌표 계산이 까다로워서
// code-behind 에서 ViewModel.AssetSlices 가 갱신될 때마다
// DonutCanvas 에 Path 들을 다시 그린다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded             += (_, _) => RedrawDonut();
            SizeChanged        += (_, _) => RedrawDonut();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DashboardViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnVmPropertyChanged;
                if (oldVm.AssetSlices is INotifyCollectionChanged oldNcc)
                    oldNcc.CollectionChanged -= OnSlicesChanged;
            }
            if (e.NewValue is DashboardViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                if (newVm.AssetSlices is INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += OnSlicesChanged;
                RedrawDonut();
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.AssetSlices))
                RedrawDonut();
        }

        private void OnSlicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RedrawDonut();
        }

        // ── 도넛 차트 ──────────────────────────────────────────────────
        private void RedrawDonut()
        {
            DonutCanvas.Children.Clear();
            if (DataContext is not DashboardViewModel vm) return;
            if (vm.AssetSlices == null || vm.AssetSlices.Count == 0) return;

            double cx = DonutCanvas.Width  / 2.0;
            double cy = DonutCanvas.Height / 2.0;
            double rOuter = Math.Min(cx, cy) - 4;
            double rInner = rOuter * 0.62;  // 도넛 두께

            int totalValue = 0;
            foreach (var s in vm.AssetSlices) totalValue += s.Value;

            // 데이터가 모두 0 이어도 자리표시용 회색 원을 그려서 빈 화면을 피한다
            if (totalValue <= 0)
            {
                DonutCanvas.Children.Add(MakeRing(cx, cy, rOuter, rInner,
                    new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))));
                return;
            }

            double startAngle = -90.0;  // 12시 방향
            foreach (var slice in vm.AssetSlices)
            {
                if (slice.Value <= 0) continue;
                double sweep = (double)slice.Value / totalValue * 360.0;
                double endAngle = startAngle + sweep;

                var path = MakeArc(cx, cy, rOuter, rInner, startAngle, endAngle, slice.Color);
                DonutCanvas.Children.Add(path);

                startAngle = endAngle;
            }
        }

        // 도넛 한 조각 (sweep < 360°)
        private static Path MakeArc(double cx, double cy, double rOuter, double rInner,
                                    double startDeg, double endDeg, Brush fill)
        {
            double startRad = startDeg * Math.PI / 180.0;
            double endRad   = endDeg   * Math.PI / 180.0;

            var p1 = new Point(cx + rOuter * Math.Cos(startRad), cy + rOuter * Math.Sin(startRad));
            var p2 = new Point(cx + rOuter * Math.Cos(endRad),   cy + rOuter * Math.Sin(endRad));
            var p3 = new Point(cx + rInner * Math.Cos(endRad),   cy + rInner * Math.Sin(endRad));
            var p4 = new Point(cx + rInner * Math.Cos(startRad), cy + rInner * Math.Sin(startRad));

            bool isLargeArc = (endDeg - startDeg) > 180.0;

            var fig = new PathFigure { StartPoint = p1, IsClosed = true };
            fig.Segments.Add(new ArcSegment(p2, new Size(rOuter, rOuter), 0,
                                            isLargeArc, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(p3, true));
            fig.Segments.Add(new ArcSegment(p4, new Size(rInner, rInner), 0,
                                            isLargeArc, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(p1, true));

            var geom = new PathGeometry();
            geom.Figures.Add(fig);
            return new Path { Data = geom, Fill = fill, Stroke = null };
        }

        // 데이터 없을 때 표시할 단색 링
        private static Path MakeRing(double cx, double cy, double rOuter, double rInner, Brush fill)
        {
            var outer = new EllipseGeometry(new Point(cx, cy), rOuter, rOuter);
            var inner = new EllipseGeometry(new Point(cx, cy), rInner, rInner);
            var ring  = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            return new Path { Data = ring, Fill = fill };
        }
    }
}
