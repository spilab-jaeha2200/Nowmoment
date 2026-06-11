// ════════════════════════════════════════════════════════════════════
// SettingsView.xaml.cs — 설정 화면 (본문 임베드 UserControl)
//
// 기존 SettingsDialog(Window) 를 메인 창 본문에 임베드하는 UserControl 버전.
// 좌측 카테고리 + 우측 패널 구조는 SettingsDialog 와 동일하며,
// 모든 동작은 SettingsViewModel 의 SaveCommand / CancelCommand /
// RestoreDefaultsCommand 로 처리된다 (Window 전용 DialogResult 로직 제거).
// ════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace SPILab.NowMoment.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }
    }
}
