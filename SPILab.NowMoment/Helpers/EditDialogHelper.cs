// ════════════════════════════════════════════════════════════
// EditDialogHelper.cs   (v4 Phase 6)
//
// 자산 편집 다이얼로그 5종 공통 헬퍼.  각 Dialog 의 code-behind 가
// ValidateRequired() 와 ShowMissing() 만 호출하여 사용.
// ════════════════════════════════════════════════════════════
using System.Windows;

namespace SPILab.NowMoment.Views
{
    internal static class EditDialogHelper
    {
        /// <summary>지정 문자열이 비어있으면 MessageBox 표시 후 false 반환.</summary>
        public static bool RequireNotEmpty(string? value, string fieldLabel)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show($"{fieldLabel}을(를) 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        /// <summary>지정 객체가 null 이면 MessageBox 표시 후 false 반환.</summary>
        public static bool RequireNotNull(object? value, string fieldLabel)
        {
            if (value == null)
            {
                MessageBox.Show($"{fieldLabel}을(를) 선택하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }
    }
}
