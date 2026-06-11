// FolderImportDialog.xaml.cs — v3.0 F-002 폴더 임포트 (Step 2.3)
//
// View ↔ ViewModel 연결 + DataGrid 강제 갱신 핫픽스.
//
// [모두 선택]/[모두 해제] 클릭 시 ViewModel 이 SetAllSelection 으로 IsSelected
// 를 일괄 변경한 뒤 SelectionBulkChanged 이벤트를 raise. 이때 활성 셀의
// CheckBox 가 갱신 누락되는 WPF 알려진 이슈를 막기 위해 코드비하인드에서
// CommitEdit + Items.Refresh 를 수동 호출.
using System;
using System.Windows;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class FolderImportDialog : Window
    {
        private FolderImportViewModel? _vm;

        public FolderImportDialog(FolderImportViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // 일괄 선택 변경 이벤트 구독
            vm.SelectionBulkChanged += OnSelectionBulkChanged;

            // 다이얼로그 닫힐 때 이벤트 해제 (메모리 누수 방지)
            Closed += (_, __) =>
            {
                if (_vm != null)
                {
                    _vm.SelectionBulkChanged -= OnSelectionBulkChanged;
                    _vm = null;
                }
            };
        }

        /// <summary>모두 선택/해제 직후 DataGrid 갱신 누락을 강제 보정.</summary>
        private void OnSelectionBulkChanged(object? sender, EventArgs e)
        {
            // 활성 셀이 편집 중이면 편집 종료 → 외부 setter 변경이 셀에 반영되도록
            CandidatesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            CandidatesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row,  true);

            // 전체 그리드 다시 그리기 — INotifyPropertyChanged 가 닿지 못한 행도 강제 새로고침
            CandidatesGrid.Items.Refresh();
        }
    }
}
