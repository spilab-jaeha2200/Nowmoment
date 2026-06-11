// ════════════════════════════════════════════════════════════════════
// KgImportView.xaml.cs — JSON/TTL 임포트 화면 code-behind
//
// 단일 책임: InitializeComponent.
// 모든 동작은 KgImportViewModel 이 v3 KnowledgeGraphService.ImportFromFile
// 을 직접 호출하여 처리.
// ════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace SPILab.NowMoment.Views
{
    public partial class KgImportView : UserControl
    {
        public KgImportView()
        {
            InitializeComponent();
        }
    }
}
