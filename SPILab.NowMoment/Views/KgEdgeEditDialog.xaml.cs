using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.Views
{
    /// <summary>
    /// KG 엣지 추가 다이얼로그.
    /// 기본 시작 노드 ID 와, 노드 목록을 채우기 위한 KG 서비스 / 도메인을 받는다.
    /// </summary>
    public partial class KgEdgeEditDialog : Window
    {
        public class EdgeResult
        {
            public string SrcId { get; set; } = "";
            public string DstId { get; set; } = "";
            public string Rel   { get; set; } = "";
            public string PropsJson { get; set; } = "{}";
        }

        public EdgeResult Result { get; private set; } = new();

        public KgEdgeEditDialog(string srcDefaultId, KnowledgeGraphService kg, string domain)
        {
            InitializeComponent();

            // 노드 목록 — 현재 도메인의 모든 노드
            var nodes = kg.GetNodes("", "", domain)
                         .Select(n => new NodeOption {
                             Id = n.Id,
                             Display = $"[{n.Type}] {n.Label}  ({n.Id})"
                         })
                         .ToList();
            // ★ 버그수정: CmbSrc 와 CmbDst 가 같은 List 인스턴스를 공유하면
            //   WPF 가 동일한 ICollectionView(현재항목·필터 상태)를 공유하게 되어,
            //   IsEditable 콤보박스인 CmbDst 의 텍스트 편집 영역이 포커스/커서를
            //   받지 못하고 드롭다운도 열리지 않는다.
            //   → 각 콤보박스에 독립된 리스트 인스턴스를 할당한다.
            CmbSrc.ItemsSource = nodes;
            CmbDst.ItemsSource = nodes.ToList();   // 별도 인스턴스
            CmbSrc.SelectedValue = srcDefaultId;

            // rel 콤보 — 자주 쓰는 표준 관계
            foreach (var r in new[] { "USES", "GOVERNS", "DERIVES_FROM", "CITES",
                                      "BELONGS_TO", "REQUIRES", "PRODUCES", "DEPENDS_ON" })
                CmbRel.Items.Add(r);
            CmbRel.SelectedIndex = 0;

            TxtProps.Text = "{}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // ComboBox 가 IsEditable=true 라 SelectedValue 가 비어도 Text 사용
            string src = (CmbSrc.SelectedValue as string) ?? CmbSrc.Text?.Trim() ?? "";
            string dst = (CmbDst.SelectedValue as string) ?? CmbDst.Text?.Trim() ?? "";
            string rel = CmbRel.Text?.Trim() ?? "";
            string props = string.IsNullOrWhiteSpace(TxtProps.Text) ? "{}" : TxtProps.Text;

            // src/dst 가 콤보 아이템 디스플레이로 잘못 들어왔을 수 있으니 ID 만 추출
            src = ExtractIdIfDisplay(src);
            dst = ExtractIdIfDisplay(dst);

            if (string.IsNullOrWhiteSpace(src)) { TxtError.Text = "시작 노드 ID 를 선택/입력하세요."; return; }
            if (string.IsNullOrWhiteSpace(dst)) { TxtError.Text = "종료 노드 ID 를 선택/입력하세요."; return; }
            if (string.IsNullOrWhiteSpace(rel)) { TxtError.Text = "관계명을 입력하세요."; return; }
            if (src == dst) { TxtError.Text = "자기참조 엣지는 허용되지 않습니다."; return; }

            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException jex) { TxtError.Text = "props_json: " + jex.Message; return; }

            Result = new EdgeResult { SrcId = src, DstId = dst, Rel = rel, PropsJson = props };
            DialogResult = true;
            Close();
        }

        /// <summary>"[Type] Label  (id:value)" 형태로 들어와도 id 만 추출.</summary>
        private static string ExtractIdIfDisplay(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            int p = s.LastIndexOf('(');
            int q = s.LastIndexOf(')');
            if (p >= 0 && q > p) return s.Substring(p + 1, q - p - 1).Trim();
            return s;
        }

        private class NodeOption
        {
            public string Id { get; set; } = "";
            public string Display { get; set; } = "";
        }

        /// <summary>
        /// 편집형 ComboBox 가 로드되면 템플릿 내부 PART_EditableTextBox 를 찾아
        /// 클릭(Up) 이벤트를 코드로 연결한다. ControlTemplate 내부 명명 요소에
        /// XAML 이벤트를 직접 거는 방식은 환경에 따라 불안정하므로,
        /// 로드 후 코드에서 확실하게 후킹한다.
        /// </summary>
        private void EditableCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox combo) return;
            combo.ApplyTemplate();

            if (combo.Template?.FindName("PART_EditableTextBox", combo)
                is TextBox tb)
            {
                // 중복 연결 방지 후 재연결
                tb.PreviewMouseLeftButtonUp -= EditableCombo_TextBoxClick;
                tb.PreviewMouseLeftButtonUp += EditableCombo_TextBoxClick;
            }
        }

        /// <summary>
        /// 편집형 ComboBox 의 텍스트 영역 클릭(버튼 업) 시 드롭다운을 연다.
        /// 버튼 업 시점은 포커스/캐럿 배치가 끝난 뒤라 안정적으로 열리며,
        /// e.Handled 를 설정하지 않아 텍스트 입력/검색은 그대로 동작한다.
        /// </summary>
        private void EditableCombo_TextBoxClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DependencyObject dep) return;
            var combo = FindParentComboBox(dep);
            if (combo == null || !combo.IsEnabled || combo.IsDropDownOpen) return;

            combo.IsDropDownOpen = true;
        }

        /// <summary>비주얼 트리 상위에서 ComboBox 를 찾는다.</summary>
        private static ComboBox? FindParentComboBox(DependencyObject child)
        {
            var p = child;
            while (p != null && p is not ComboBox)
                p = VisualTreeHelper.GetParent(p);
            return p as ComboBox;
        }
    }
}
