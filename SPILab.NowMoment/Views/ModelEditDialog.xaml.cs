// ════════════════════════════════════════════════════════════
// ModelEditDialog.xaml.cs   (v4 Phase 6)
// ════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class ModelEditDialog : Window
    {
        public ModelEditDialog(ModelEditViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            HeaderTitle.Text = vm.Id == 0
                ? "자산 등록 — AI 모델 / 학습데이터"
                : $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "AI 모델" : vm.Name)}";
            Title = HeaderTitle.Text;
            Loaded += (_, __) => vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ModelEditViewModel.Name) && vm.Id > 0)
                {
                    HeaderTitle.Text = $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "AI 모델" : vm.Name)}";
                    Title = HeaderTitle.Text;
                }
            };
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;
            Tag = "save";
            DialogResult = true;
        }

        private void SaveAndContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;
            Tag = "continue";
            DialogResult = true;
        }

        private bool ValidateInput()
        {
            var vm = (ModelEditViewModel)DataContext;
            if (!EditDialogHelper.RequireNotEmpty(vm.Name, "모델명")) return false;
            if (!EditDialogHelper.RequireNotNull(vm.SelectedProject, "프로젝트")) return false;
            return true;
        }
    }
}
