using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Views
{
    /// <summary>
    /// KG 노드 추가/편집. 생성자에 null 을 주면 신규, KgNode 를 주면 편집 모드.
    /// </summary>
    public partial class KgNodeEditDialog : Window
    {
        public KgNode Result { get; private set; } = new KgNode();
        private readonly bool _isEdit;

        public KgNodeEditDialog(KgNode? existing)
        {
            InitializeComponent();

            // 타입 콤보 — 기존 KgViewModel.NodeTypes 와 동일 셋
            CmbType.Items.Add("PhysicsRule");
            CmbType.Items.Add("Material");
            CmbType.Items.Add("ProcessParam");
            CmbType.Items.Add("Workspace");
            CmbType.Items.Add("Parameter");
            CmbType.Items.Add("Spec");
            CmbType.Items.Add("Citation");
            CmbType.Items.Add("Resource");

            if (existing != null)
            {
                _isEdit = true;
                Title = "KG 노드 편집";
                TxtId.Text = existing.Id;
                TxtId.IsEnabled = false;     // ID 변경은 금지 (FK 안전성)
                CmbType.Text = existing.Type;
                TxtLabel.Text = existing.Label;
                TxtProps.Text = PrettyJson(existing.PropsJson);
            }
            else
            {
                _isEdit = false;
                Title = "KG 노드 추가";
                CmbType.SelectedIndex = 0;
                TxtProps.Text = "{}";
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var id = TxtId.Text?.Trim() ?? "";
            var type = CmbType.Text?.Trim() ?? "";
            var label = TxtLabel.Text?.Trim() ?? "";
            var props = string.IsNullOrWhiteSpace(TxtProps.Text) ? "{}" : TxtProps.Text;

            if (string.IsNullOrWhiteSpace(id))   { TxtError.Text = "ID 를 입력하세요."; return; }
            if (string.IsNullOrWhiteSpace(type)) { TxtError.Text = "타입을 입력/선택하세요."; return; }
            if (string.IsNullOrWhiteSpace(label)){ TxtError.Text = "레이블을 입력하세요."; return; }

            // JSON 검증
            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException jex) { TxtError.Text = "props_json 이 올바른 JSON 이 아닙니다: " + jex.Message; return; }

            Result = new KgNode { Id = id, Type = type, Label = label, PropsJson = props };
            DialogResult = true;
            Close();
        }

        private static string PrettyJson(string raw)
        {
            try
            {
                using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                return JsonSerializer.Serialize(d,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch { return raw ?? "{}"; }
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
