// ════════════════════════════════════════════════════════════════════
// KgBuilderView.xaml.cs — KG 빌더 화면 code-behind
//
// 단일 책임: 로그 라인이 추가될 때 ListBox 를 맨 아래로 자동 스크롤.
//
// 빌드/임포트 자체는 모두 KgBuilderViewModel 을 통해 v3 KgViewModel 의
// BuildCommand / ChangeBuilderSrcCommand / ImportFileCommand 로 패스스루.
// ════════════════════════════════════════════════════════════════════
using System.Collections.Specialized;
using System.Windows.Controls;
using SPILab.NowMoment.ViewModels;

namespace SPILab.NowMoment.Views
{
    public partial class KgBuilderView : UserControl
    {
        private INotifyCollectionChanged? _logCollection;

        public KgBuilderView()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
            this.Unloaded += (_, __) => DetachLogHandler();
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            DetachLogHandler();
            if (DataContext is KgBuilderViewModel vm)
            {
                _logCollection = vm.LogLines;
                _logCollection.CollectionChanged += OnLogChanged;
            }
        }

        private void DetachLogHandler()
        {
            if (_logCollection != null)
            {
                _logCollection.CollectionChanged -= OnLogChanged;
                _logCollection = null;
            }
        }

        /// <summary>새 로그가 추가될 때 ListBox 를 맨 아래로 스크롤.</summary>
        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            if (LogList?.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    var last = LogList.Items[LogList.Items.Count - 1];
                    if (last != null) LogList.ScrollIntoView(last);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}
