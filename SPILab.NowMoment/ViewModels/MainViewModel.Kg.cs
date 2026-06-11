// ════════════════════════════════════════════════════════════
// MainViewModel.Kg.cs — 기존 MainViewModel 에 KG 프로퍼티 추가
//
// 사용 조건:
//   1) 기존 MainViewModel.cs 의 클래스 선언을 다음과 같이 수정해야 합니다:
//        public class MainViewModel : BaseViewModel
//        →
//        public partial class MainViewModel : BaseViewModel
//
//   2) 본 파일을 ViewModels/ 디렉토리에 추가합니다.
//
//   3) App.xaml.cs 또는 MainWindow.xaml.cs 에서 wire 합니다:
//        var db   = new DatabaseService();
//        var main = new MainViewModel(db);
//        main.AttachKg(new KnowledgeGraphService(db.DbPath));
//        DataContext = main;
// ════════════════════════════════════════════════════════════
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public partial class MainViewModel
    {
        private KgViewModel? _kg;

        // ── v3.0 F-001 Step 1.5: EditDialog가 KG 패널 합성에 사용 ─
        // 자산 편집 다이얼로그(5종)에 "연결된 KG 노드" 섹션을 띄우려면
        // 서비스 인스턴스가 필요하므로 보관해둔다.
        public KnowledgeGraphService? KgService { get; private set; }

        /// <summary>KG 탭 ViewModel — XAML 바인딩: {Binding Kg.*}</summary>
        public KgViewModel? Kg
        {
            get => _kg;
            set => Set(ref _kg, value);
        }

        public void AttachKg(KnowledgeGraphService service)
        {
            KgService = service;
            Kg = new KgViewModel(service, this);
        }
    }
}
