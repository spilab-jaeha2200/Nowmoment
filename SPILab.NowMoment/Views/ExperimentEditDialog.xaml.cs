// ════════════════════════════════════════════════════════════
// ExperimentEditDialog.xaml.cs   (v4 Phase 6)
// ════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class ExperimentEditDialog : Window
    {
        public ExperimentEditDialog(ExperimentEditViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            HeaderTitle.Text = vm.Id == 0
                ? "자산 등록 — 실험 / 측정 데이터"
                : $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "실험 / 측정" : vm.Name)}";
            Title = HeaderTitle.Text;
            Loaded += (_, __) => vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ExperimentEditViewModel.Name) && vm.Id > 0)
                {
                    HeaderTitle.Text = $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Name) ? "실험 / 측정" : vm.Name)}";
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
            var vm = (ExperimentEditViewModel)DataContext;
            return EditDialogHelper.RequireNotEmpty(vm.Name, "실험명");
        }
    }
}
