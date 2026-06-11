// ════════════════════════════════════════════════════════════════════
// AssetKgLinkViewerView.xaml.cs — 자산↔KG 링크 종합 화면 code-behind
//
// 좌측 자산 선택 → AssetKgLinkViewerViewModel.LinkPanelVm 갱신 →
// 우측 v3 AssetKgLinkPanelView 가 새 DataContext 로 자동 재바인딩.
//
// v4 버그수정: 메인 네비게이션이 화면을 캐시(_cache)하므로, KG 링크 탭을
//   다시 방문해도 같은 View/VM 인스턴스가 재사용된다. 그 사이 자산 편집
//   다이얼로그에서 링크가 추가/삭제됐어도 우측 패널이 갱신되지 않았다.
//   → 화면이 다시 보일 때(IsVisibleChanged) ViewModel.Refresh() 를 호출해
//     DB 기준으로 링크 목록을 즉시 최신화한다.
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class AssetKgLinkViewerView : UserControl
    {
        public AssetKgLinkViewerView()
        {
            InitializeComponent();
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 화면이 다시 표시되는 순간에만 갱신 (숨겨질 때는 무시)
            if (e.NewValue is bool visible && visible
                && DataContext is AssetKgLinkViewerViewModel vm)
            {
                vm.Refresh();
            }
        }
    }
}
