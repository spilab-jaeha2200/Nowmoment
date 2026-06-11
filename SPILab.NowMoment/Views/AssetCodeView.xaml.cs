// ════════════════════════════════════════════════════════════════════
// AssetCodeView.xaml.cs — 소스코드/모듈 화면 code-behind
//
// 단일 책임: DataGrid 행 더블클릭을 ViewModel 의 EditCommand 로 라우팅.
// (XAML 만으로 더블클릭→Command 매핑이 어려워 최소 핸들러 1개만 추가)
//
// CRUD 자체는 모두 AssetCodeViewModel 을 통해 v3 MainViewModel 의
// AddCodeCommand / EditCodeCommand / DeleteCodeCommand 로 패스스루.
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class AssetCodeView : UserControl
    {
        public AssetCodeView()
        {
            InitializeComponent();
        }

        /// <summary>DataGrid 행 더블클릭 → v3 EditCodeCommand 호출 (= CodeEditDialog).</summary>
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 헤더/스크롤바/체크박스 더블클릭 무시 — DataGridCell/Row 안에서만 처리
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridCell && dep is not DataGridRow)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            if (dep == null) return;

            if (DataContext is AssetCodeViewModel vm
                && vm.EditCommand.CanExecute(null))
            {
                vm.EditCommand.Execute(null);
            }
        }
    
        /// <summary>헤더 체크박스 토글 → 전체 행 선택/해제.</summary>
        private void OnHeaderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb
                && DataContext is AssetCodeViewModel vm)
            {
                vm.SetAllSelected(cb.IsChecked == true);
            }
        }
}
}
