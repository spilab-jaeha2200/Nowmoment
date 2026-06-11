using System.Windows;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog(SettingsViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.RequestClose += ok =>
            {
                DialogResult = ok;
                Close();
            };
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
