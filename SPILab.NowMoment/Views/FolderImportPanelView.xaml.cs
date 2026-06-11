// ════════════════════════════════════════════════════════════════════
// FolderImportPanelView.xaml.cs — 폴더 임포트 인라인 화면 code-behind
//
// 단일 책임: InitializeComponent.
// 스캔/커밋은 모두 FolderImportPanelViewModel 이 v3 FolderImportViewModel
// 의 ScanCommand/CommitCommand 로 패스스루.
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;

namespace SPILab.NowMoment.Views
{
    public partial class FolderImportPanelView : UserControl
    {
        public FolderImportPanelView()
        {
            InitializeComponent();
        }

        /// <summary>헤더 체크박스 토글 → 후보 전체 선택/해제.</summary>
        private void OnHeaderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (DataContext is not ViewModels.FolderImportPanelViewModel vm) return;

            var cmd = cb.IsChecked == true ? vm.SelectAllCommand : vm.SelectNoneCommand;
            if (cmd != null && cmd.CanExecute(null))
                cmd.Execute(null);
        }
    }
}
