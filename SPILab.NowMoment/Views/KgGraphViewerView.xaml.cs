// ════════════════════════════════════════════════════════════════════
// KgGraphViewerView.xaml.cs — 그래프 시각화 화면 (SCR-B04) code-behind
//
// 단일 책임: VM 이벤트(ZoomIn/Out, ExportPng, Relayout) 를 받아
// 임베드한 v3 KgGraphView 내부 컨트롤(GraphScale, GraphCanvas) 을 조작.
//
// v3 KgGraphView 코드는 수정하지 않고, 이름이 노출된 x:Name 컨트롤
// (GraphScale, GraphCanvas) 을 FindName 으로 찾아 직접 조작한다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class KgGraphViewerView : UserControl
    {
        private KgGraphViewerViewModel? _vm;

        public KgGraphViewerView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, __) => DetachVm();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachVm();
            if (DataContext is KgGraphViewerViewModel vm)
            {
                _vm = vm;
                _vm.ZoomInRequested      += OnZoomInRequested;
                _vm.ZoomOutRequested     += OnZoomOutRequested;
                _vm.ExportPngRequested   += OnExportPngRequested;
                _vm.RelayoutRequested    += OnRelayoutRequested;
            }
        }

        private void DetachVm()
        {
            if (_vm == null) return;
            _vm.ZoomInRequested    -= OnZoomInRequested;
            _vm.ZoomOutRequested   -= OnZoomOutRequested;
            _vm.ExportPngRequested -= OnExportPngRequested;
            _vm.RelayoutRequested  -= OnRelayoutRequested;
            _vm = null;
        }

        // ── 줌 ──────────────────────────────────────────

        private void OnZoomInRequested(object? sender, EventArgs e)  => ApplyZoom(1.2);
        private void OnZoomOutRequested(object? sender, EventArgs e) => ApplyZoom(1 / 1.2);

        private void ApplyZoom(double factor)
        {
            var scale = FindGraphScale();
            if (scale == null) return;
            double newScale = scale.ScaleX * factor;
            // 제한: 0.2 ~ 4.0 (v3 Canvas_MouseWheel 과 동일 범위 가정)
            newScale = Math.Max(0.2, Math.Min(4.0, newScale));
            scale.ScaleX = newScale;
            scale.ScaleY = newScale;
        }

        /// <summary>v3 KgGraphView 내부의 x:Name="GraphScale" ScaleTransform 인스턴스 찾기.</summary>
        private ScaleTransform? FindGraphScale()
        {
            if (EmbeddedGraph?.FindName("GraphScale") is ScaleTransform s) return s;
            return null;
        }

        private Canvas? FindGraphCanvas()
        {
            if (EmbeddedGraph?.FindName("GraphCanvas") is Canvas c) return c;
            return null;
        }

        // ── PNG 내보내기 ─────────────────────────────────

        private void OnExportPngRequested(object? sender, EventArgs e)
        {
            var canvas = FindGraphCanvas();
            if (canvas == null)
            {
                MessageBox.Show("그래프 캔버스를 찾을 수 없습니다.",
                    "PNG 내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                FileName = $"kg_graph_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Filter   = "PNG 이미지 (*.png)|*.png",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // 캔버스 크기에 맞춰 비트맵 렌더링
                double w = canvas.ActualWidth  > 0 ? canvas.ActualWidth  : canvas.Width;
                double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
                if (w <= 0 || h <= 0)
                {
                    MessageBox.Show("그래프 크기를 계산할 수 없습니다.",
                        "PNG 내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(w),
                    (int)Math.Ceiling(h),
                    96, 96, PixelFormats.Pbgra32);

                // RenderTransform 을 잠시 제거하고 원본 좌표계로 렌더
                var origTransform = canvas.RenderTransform;
                try
                {
                    canvas.RenderTransform = Transform.Identity;
                    canvas.UpdateLayout();
                    rtb.Render(canvas);
                }
                finally
                {
                    canvas.RenderTransform = origTransform;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(dlg.FileName);
                encoder.Save(fs);

                MessageBox.Show($"PNG 저장 완료:\n{dlg.FileName}",
                    "PNG 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("PNG 내보내기 실패:\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── 레이아웃 리셋 ────────────────────────────────

        /// <summary>
        /// v3 KgGraphView 의 BtnRelayout_Click → TryRedraw 와 동등한 동작.
        /// private 이므로 직접 호출 불가 → 버튼 자동 클릭 시뮬레이션.
        /// </summary>
        private void OnRelayoutRequested(object? sender, EventArgs e)
        {
            if (EmbeddedGraph == null) return;
            // v3 내부 버튼을 visual tree 에서 찾아 RaiseEvent
            if (EmbeddedGraph.FindName("BtnRelayout") is Button btn)
            {
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(btn);
                var iip  = peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                            as System.Windows.Automation.Provider.IInvokeProvider;
                iip?.Invoke();
            }
        }
    }
}
