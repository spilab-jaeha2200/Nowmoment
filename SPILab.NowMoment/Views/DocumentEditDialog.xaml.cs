// ════════════════════════════════════════════════════════════
// DocumentEditDialog.xaml.cs   (v4 Phase 6)
// ════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class DocumentEditDialog : Window
    {
        public DocumentEditDialog(DocumentEditViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            HeaderTitle.Text = vm.Id == 0
                ? "자산 등록 — 문서 / 논문"
                : $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Title) ? "문서 / 논문" : vm.Title)}";
            Title = HeaderTitle.Text;
            Loaded += (_, __) => vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DocumentEditViewModel.Title) && vm.Id > 0)
                {
                    HeaderTitle.Text = $"자산 수정 — {(string.IsNullOrWhiteSpace(vm.Title) ? "문서 / 논문" : vm.Title)}";
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
            var vm = (DocumentEditViewModel)DataContext;
            if (!EditDialogHelper.RequireNotEmpty(vm.Title, "제목")) return false;
            if (!EditDialogHelper.RequireNotNull(vm.SelectedProject, "프로젝트")) return false;
            return true;
        }
    }
}
