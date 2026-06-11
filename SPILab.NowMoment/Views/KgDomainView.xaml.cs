// ════════════════════════════════════════════════════════════════════
// KgDomainView.xaml.cs — 도메인 관리 화면 code-behind
//
// 단일 책임: InitializeComponent.
// CRUD 는 KgDomainViewModel 이 v3 KgViewModel 의
// AddDomainCommand/DeleteDomainCommand/ClearDomainCommand 로 패스스루.
// ════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace SPILab.NowMoment.Views
{
    public partial class KgDomainView : UserControl
    {
        public KgDomainView()
        {
            InitializeComponent();
        }
    }
}
