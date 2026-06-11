// ════════════════════════════════════════════════════════════════════
// ViewModels/ToolActionViewModel.cs — v4 통합 도구 액션 화면 공용 VM
//
// 폴더 임포트 / DB 백업 / PDF 카탈로그 화면이 공유하는 단순 액션 VM.
// 모든 실행은 v3 MainViewModel 의 기존 명령을 그대로 호출하므로,
// SaveFileDialog / OpenFileDialog / 결과 MessageBox 까지 v3 가 처리.
//
// 화면 구성:
//   • 헤더 + 아이콘 + 부제
//   • 본문: 설명 단락 + 옵션/항목 리스트 + Hint (선택)
//   • 액션 버튼 (큰 PrimaryButton)
//   • 마지막 실행 결과 (있다면) — 단순 텍스트 표시
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SPILab.NowMoment.ViewModels
{
    public class ToolActionViewModel : INotifyPropertyChanged
    {
        public ToolActionViewModel(
            string icon,
            string title,
            string subtitle,
            string description,
            string buttonLabel,
            Action onClick,
            params string[] bullets)
        {
            Icon         = icon;
            Title        = title;
            Subtitle     = subtitle;
            Description  = description;
            ButtonLabel  = buttonLabel;

            Bullets = new ObservableCollection<string>(bullets ?? Array.Empty<string>());

            ActionCommand = new SimpleCommand(_ =>
            {
                try
                {
                    onClick?.Invoke();
                    LastResult = $"✅ 실행됨: {Title}  ({DateTime.Now:HH:mm:ss})";
                    IsResultError = false;
                }
                catch (Exception ex)
                {
                    LastResult = "❌ 실행 실패: " + ex.Message;
                    IsResultError = true;
                }
            });
        }

        public string Icon        { get; }
        public string Title       { get; }
        public string Subtitle    { get; }
        public string Description { get; }
        public string ButtonLabel { get; }
        public ObservableCollection<string> Bullets { get; }
        public bool HasBullets => Bullets.Count > 0;

        public ICommand ActionCommand { get; }

        private string _lastResult = "";
        public string LastResult
        {
            get => _lastResult;
            private set { if (_lastResult != value) { _lastResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); } }
        }

        public bool HasResult => !string.IsNullOrEmpty(_lastResult);

        private bool _isResultError;
        public bool IsResultError
        {
            get => _isResultError;
            private set { if (_isResultError != value) { _isResultError = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
