// ════════════════════════════════════════════════════════════════════
// KgNodesEdgesView.xaml.cs — KG 노드/엣지 편집 화면 code-behind
//
// 단일 책임: 노드 DataGrid 행 더블클릭을 v3 EditNodeCommand 로 라우팅.
//
// CRUD 자체는 모두 KgNodesEdgesViewModel 을 통해 v3 KgViewModel 의
// AddNodeCommand / EditNodeCommand / DeleteNodeCommand /
// AddEdgeCommand / DeleteEdgeCommand 로 패스스루
// (= v3 KgNodeEditDialog / KgEdgeEditDialog 그대로 호출).
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class KgNodesEdgesView : UserControl
    {
        public KgNodesEdgesView()
        {
            InitializeComponent();
        }

        /// <summary>노드 그리드 행 더블클릭 → v3 EditNodeCommand (= KgNodeEditDialog).</summary>
        private void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridCell && dep is not DataGridRow)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            if (dep == null) return;

            if (DataContext is KgNodesEdgesViewModel vm
                && vm.EditNodeCommand.CanExecute(null))
            {
                vm.EditNodeCommand.Execute(null);
            }
        }

        /// <summary>노드 그리드 헤더 체크박스 → 전체 노드 행 선택/해제.</summary>
        private void OnNodeHeaderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && DataContext is KgNodesEdgesViewModel vm)
                vm.SetAllNodesSelected(cb.IsChecked == true);
        }

        /// <summary>엣지 그리드 헤더 체크박스 → 전체 엣지 행 선택/해제.</summary>
        private void OnEdgeHeaderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && DataContext is KgNodesEdgesViewModel vm)
                vm.SetAllEdgesSelected(cb.IsChecked == true);
        }
    }
}
