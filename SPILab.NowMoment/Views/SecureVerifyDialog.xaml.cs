// ════════════════════════════════════════════════════════════════════
// SecureVerifyDialog.xaml.cs — NowMoment v4.1 Phase 3 (작업 3)
//
// 개선 개발계획서 5.1 단계 2 — 인증 대화상자 코드비하인드.
//
//   ICredentialPrompt 를 구현하여 LocalSecureVerifyBackend 에 주입된다.
//   Prompt() 가 호출되면 모달로 표시되고, 사용자가 입력한 사번·
//   비밀번호(·2FA)를 CoreCredential 로 돌려준다. 취소 시 null.
//
//   이 클래스는 UI 수집만 담당한다 — 인증·인가·키 발급은 백엔드
//   (LocalSecureVerifyBackend)의 책임이다. 책임 분리.
// ════════════════════════════════════════════════════════════════════
using System.Windows;
using SPILab.NowMoment.Core.Security;

namespace SPILab.NowMoment.Views
{
    public partial class SecureVerifyDialog : Window, ICredentialPrompt
    {
        private CoreCredential? _result;
        private bool _requireSecondFactor;

        public SecureVerifyDialog()
        {
            InitializeComponent();
        }

        // ── ICredentialPrompt ──
        // 백엔드가 호출. UI 스레드에서 모달 표시 후 자격증명을 반환한다.
        public CoreCredential? Prompt(bool requireSecondFactor)
        {
            // UI 스레드 보장 — 백엔드가 다른 스레드에서 호출할 수 있다.
            if (!Dispatcher.CheckAccess())
                return Dispatcher.Invoke(() => Prompt(requireSecondFactor));

            _requireSecondFactor = requireSecondFactor;
            SecondFactorPanel.Visibility = requireSecondFactor
                ? Visibility.Visible
                : Visibility.Collapsed;

            _result = null;
            EmployeeIdBox.Clear();
            PasswordBox.Clear();
            SecondFactorBox.Clear();
            HideError();

            // 소유자 창이 있으면 가운데 정렬
            if (Application.Current?.MainWindow != null
                && Application.Current.MainWindow != this)
                Owner = Application.Current.MainWindow;

            EmployeeIdBox.Focus();
            ShowDialog();   // 모달 — 닫힐 때까지 블록
            return _result;
        }

        private void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            string empId = EmployeeIdBox.Text.Trim();
            string pwd   = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(empId))
            {
                ShowError("사번을 입력하세요.");
                EmployeeIdBox.Focus();
                return;
            }
            if (string.IsNullOrEmpty(pwd))
            {
                ShowError("비밀번호를 입력하세요.");
                PasswordBox.Focus();
                return;
            }

            string? second = null;
            if (_requireSecondFactor)
            {
                second = SecondFactorBox.Text.Trim();
                if (string.IsNullOrEmpty(second))
                {
                    ShowError("2차 인증 코드를 입력하세요.");
                    SecondFactorBox.Focus();
                    return;
                }
            }

            _result = new CoreCredential
            {
                EmployeeId   = empId,
                Password     = pwd,
                SecondFactor = second,
            };
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = null;
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Text = "";
            ErrorBox.Visibility = Visibility.Collapsed;
        }
    }
}
