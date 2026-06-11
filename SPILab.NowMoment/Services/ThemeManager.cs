// ════════════════════════════════════════════════════════════════════
// Services/ThemeManager.cs — 라이트/다크/시스템 테마 전환
//
// 사용:
//   ThemeManager.Apply("Light");   // 라이트 강제
//   ThemeManager.Apply("Dark");    // 다크 강제
//   ThemeManager.Apply("System");  // OS 설정 따라가기
//
// 동작:
//   Application.Current.Resources 의 Theme.* 키들을 현재 테마 팔레트로
//   덮어쓴다. 모든 화면이 DynamicResource Theme.* 로 바인딩되어 있으므로
//   호출 즉시 모든 화면이 자동 갱신된다.
//
// 적용 시점:
//   1) App 시작 시 — App.xaml.cs 의 OnStartup 에서 1회
//   2) 설정 저장 시 — SettingsViewModel.DoSave() 끝부분에서 호출
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SPILab.NowMoment.Services
{
    public static class ThemeManager
    {
        /// <summary>현재 적용된 테마 (Light / Dark). System 은 OS 값으로 해소된 후 저장.</summary>
        public static string Current { get; private set; } = "Light";

        /// <summary>저장된 테마 문자열을 받아 실제 팔레트 적용. "Light" / "Dark" / "System".</summary>
        public static void Apply(string theme)
        {
            string effective = ResolveEffective(theme);
            var palette = effective == "Dark" ? GetDarkPalette() : GetLightPalette();

            var resources = Application.Current?.Resources;
            if (resources == null) return;

            foreach (var kv in palette)
            {
                // 같은 키의 기존 브러시를 새 색상으로 교체
                resources[kv.Key] = new SolidColorBrush(kv.Value);
            }
            Current = effective;
        }

        /// <summary>"System" 이면 OS 설정 읽어 Light/Dark 로 해소.</summary>
        private static string ResolveEffective(string theme)
        {
            if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)) return "Dark";
            if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";

            // System: Windows 의 AppsUseLightTheme 레지스트리 키 확인
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = key?.GetValue("AppsUseLightTheme");
                if (v is int i) return i == 0 ? "Dark" : "Light";
            }
            catch { /* 레지스트리 접근 실패 시 Light fallback */ }
            return "Light";
        }

        // ── 라이트 팔레트 (App.xaml 의 기본값과 일치) ─────────────────
        private static Dictionary<string, Color> GetLightPalette() => new()
        {
            // 배경
            { "Theme.BgMain",        ColorFromHex("#F4F6FB") },
            { "Theme.BgCard",        ColorFromHex("#FFFFFF") },
            { "Theme.BgSidebar",     ColorFromHex("#F9FAFC") },
            { "Theme.BgHeader",      ColorFromHex("#F4F6FB") },  // 회색바 (이미지 매치)
            { "Theme.BgStatus",      ColorFromHex("#F0F1F4") },
            { "Theme.BgGridRowEven", ColorFromHex("#FFFFFF") },
            { "Theme.BgGridRowOdd",  ColorFromHex("#F7F8FA") },
            { "Theme.BgGridHeader",  ColorFromHex("#EEF1F6") },
            { "Theme.BgHover",       ColorFromHex("#E7EDF7") },
            { "Theme.BgSelected",    ColorFromHex("#DBE5F5") },
            { "Theme.BgDisabled",    ColorFromHex("#E5E7EB") },

            // 텍스트
            { "Theme.FgPrimary",     ColorFromHex("#1F2937") },
            { "Theme.FgSecondary",   ColorFromHex("#4B5563") },
            { "Theme.FgMuted",       ColorFromHex("#9CA3AF") },
            { "Theme.FgOnAccent",    ColorFromHex("#FFFFFF") },

            // 강조
            { "Theme.Border",        ColorFromHex("#E5E7EB") },
            { "Theme.Accent",        ColorFromHex("#1F3864") },
            { "Theme.AccentDark",    ColorFromHex("#162845") },
            { "Theme.Cyan",          ColorFromHex("#3D7BCE") },
            { "Theme.Success",       ColorFromHex("#3DA48B") },
            { "Theme.Warning",       ColorFromHex("#E8A838") },
            { "Theme.Error",         ColorFromHex("#E85555") },
        
            // v4 Phase 7: 다이얼로그 다크 호환 키
            { "Theme.BgDialogHeader",             ColorFromHex("#1F3864") },
            { "Theme.BgOnAccentHover",            ColorFromHex("#33FFFFFF") },
            { "Theme.BgErrorSoft",                ColorFromHex("#FDECEC") },
        };

        // ── 다크 팔레트 (Material/Fluent 다크 톤 참조) ───────────────
        // 배경은 어둡고 텍스트는 밝게. 강조색은 라이트보다 약간 밝게 조정.
        private static Dictionary<string, Color> GetDarkPalette() => new()
        {
            // 배경
            { "Theme.BgMain",        ColorFromHex("#1A1D23") },  // 본문 (가장 어두움)
            { "Theme.BgCard",        ColorFromHex("#252932") },  // 카드 (약간 밝음)
            { "Theme.BgSidebar",     ColorFromHex("#1F2229") },  // 사이드바
            { "Theme.BgHeader",      ColorFromHex("#1F2229") },  // 상단바 — 사이드바와 동일 톤
            { "Theme.BgStatus",      ColorFromHex("#16181D") },  // 상태바 (가장 어두움)
            { "Theme.BgGridRowEven", ColorFromHex("#252932") },
            { "Theme.BgGridRowOdd",  ColorFromHex("#2A2E37") },
            { "Theme.BgGridHeader",  ColorFromHex("#2F3441") },
            { "Theme.BgHover",       ColorFromHex("#2F3540") },
            { "Theme.BgSelected",    ColorFromHex("#3A4357") },
            { "Theme.BgDisabled",    ColorFromHex("#2A2E37") },

            // 텍스트 (밝은 톤)
            { "Theme.FgPrimary",     ColorFromHex("#E5E7EB") },  // 주 텍스트
            { "Theme.FgSecondary",   ColorFromHex("#B0B7C3") },
            { "Theme.FgMuted",       ColorFromHex("#7C8593") },
            { "Theme.FgOnAccent",    ColorFromHex("#FFFFFF") },

            // 강조 (다크에서도 식별 가능한 톤)
            { "Theme.Border",        ColorFromHex("#3A4150") },
            { "Theme.Accent",        ColorFromHex("#5B8AD9") },   // 라이트(#1F3864)보다 밝게
            { "Theme.AccentDark",    ColorFromHex("#4070C0") },
            { "Theme.Cyan",          ColorFromHex("#5BA8E8") },
            { "Theme.Success",       ColorFromHex("#5DC4A8") },
            { "Theme.Warning",       ColorFromHex("#F0BC5A") },
            { "Theme.Error",         ColorFromHex("#F07070") },
        
            // v4 Phase 7: 다이얼로그 다크 호환 키
            { "Theme.BgDialogHeader",             ColorFromHex("#1F2A40") },
            { "Theme.BgOnAccentHover",            ColorFromHex("#33FFFFFF") },
            { "Theme.BgErrorSoft",                ColorFromHex("#3A2030") },
        };

        private static Color ColorFromHex(string hex)
        {
            // "#RRGGBB" 만 지원 (단순화)
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex)!;
                return c;
            }
            catch
            {
                return Colors.Gray;
            }
        }
    }
}
