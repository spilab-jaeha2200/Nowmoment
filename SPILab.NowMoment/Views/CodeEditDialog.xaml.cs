// ════════════════════════════════════════════════════════════
// CodeEditDialog.xaml.cs   (v4 Phase 6)
//
// 변경 사항:
//   • Header 드래그 이동 (WindowStyle=None 이므로 직접 처리)
//   • 헤더 제목을 "자산 등록 — ..." / "자산 수정 — <자산명>" 으로 동적 변경
//   • [저장+계속] 버튼: DialogResult=true + Window.Tag="continue"
//     호출자(MainViewModel) 가 Tag 를 보고 다음 신규 다이얼로그를 다시 띄움
// ════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class CodeEditDialog : Window
    {
        public CodeEditDialog(CodeEditViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            // 등록(Id=0) / 수정(Id>0) 에 따라 헤더 제목 갱신
            HeaderTitle.Text = vm.Id == 0
                ? "자산 등록 — 소스코드 / 모듈"
                : $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "소스코드 / 모듈" : vm.Name)}";
            Title = HeaderTitle.Text;
            Loaded += (_, __) => vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CodeEditViewModel.Name) && vm.Id > 0)
                {
                    HeaderTitle.Text = $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "소스코드 / 모듈" : vm.Name)}";
                    Title = HeaderTitle.Text;
                }
            };
        }

        // ── 헤더 드래그로 창 이동 ──────────────────────────
        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        // ── X / 취소 ──────────────────────────────────────
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // ── 저장 ──────────────────────────────────────────
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;
            Tag = "save";
            DialogResult = true;
        }

        // ── 저장+계속 ─────────────────────────────────────
        private void SaveAndContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;
            Tag = "continue";
            DialogResult = true;
        }

        private bool ValidateInput()
        {
            var vm = (CodeEditViewModel)DataContext;
            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                MessageBox.Show("자산명은 필수입니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (vm.SelectedProject == null)
            {
                MessageBox.Show("프로젝트를 선택하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }
    }
}
