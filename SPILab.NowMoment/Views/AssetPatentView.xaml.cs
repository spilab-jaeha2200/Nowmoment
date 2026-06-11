// ════════════════════════════════════════════════════════════════════
// AssetPatentView.xaml.cs — 특허/IP 화면 code-behind
//
// 단일 책임: DataGrid 행 더블클릭을 ViewModel 의 EditCommand 로 라우팅.
//
// CRUD 자체는 모두 AssetPatentViewModel 을 통해 v3 MainViewModel 의
// AddPatentCommand / EditPatentCommand / DeletePatentCommand 로 패스스루
// (= v3 PatentEditDialog 그대로 호출).
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class AssetPatentView : UserControl
    {
        public AssetPatentView()
        {
            InitializeComponent();
        }

        /// <summary>DataGrid 행 더블클릭 → v3 EditPatentCommand 호출 (= PatentEditDialog).</summary>
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridCell && dep is not DataGridRow)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            if (dep == null) return;

            if (DataContext is AssetPatentViewModel vm
                && vm.EditCommand.CanExecute(null))
            {
                vm.EditCommand.Execute(null);
            }
        }
    
        /// <summary>헤더 체크박스 토글 → 전체 행 선택/해제.</summary>
        private void OnHeaderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb
                && DataContext is AssetPatentViewModel vm)
            {
                vm.SetAllSelected(cb.IsChecked == true);
            }
        }
}
}
