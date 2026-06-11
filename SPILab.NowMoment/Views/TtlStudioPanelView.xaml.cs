// ════════════════════════════════════════════════════════════════════
// TtlStudioPanelView.xaml.cs — TTL Studio 화면 (SCR-B06) code-behind
//
// 단일 책임: WPF Initialize.
// CRUD/SPARQL 실행/파일 입출력은 모두 TtlStudioPanelViewModel 을 통해
// v3 TtlStudioViewModel 명령으로 패스스루.
// ════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace SPILab.NowMoment.Views
{
    public partial class TtlStudioPanelView : UserControl
    {
        public TtlStudioPanelView()
        {
            InitializeComponent();
        }
    }
}
