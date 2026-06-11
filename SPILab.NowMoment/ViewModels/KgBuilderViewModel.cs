// ════════════════════════════════════════════════════════════════════
// ViewModels/KgBuilderViewModel.cs — v4 KG 빌더 화면 전용 VM
//
// 빌드 실행은 v3.0 KgViewModel.BuildCommand 를 그대로 패스스루:
//   • BuildCommand            → DoBuildAsync() (KgViewModel.Builder.cs)
//   • ChangeBuilderSrcCommand → 소스 파일/폴더 선택 다이얼로그
//   • ImportFileCommand       → 빌더 산출물(JSON·TTL) 임포트
//
// v3 의 BuildStatus 는 매 라인마다 덮어쓰여지는 한 줄 문자열이므로,
// v4 화면에서는 KgViewModel.PropertyChanged 를 구독하여 BuildStatus
// 변동 시마다 LogLines 컬렉션에 누적한다. 이렇게 하면 v3 코드를 한 줄도
// 수정하지 않고도 Image 1 의 "실행 로그" 패널을 구현할 수 있다.
//
// 화면 설계서 SCR-B02 (Image 1) 표시 보강:
//   • 빌더 설정: 도메인 / 스크립트 / 출력 경로 / 추가 인자
//   • 진행 표시: 진행률 바 + ETA + 노드 카운트
//   • 실행 로그: 타임스탬프 + 레벨 + 메시지 (다크 톤 모노스페이스)
//   • 로그 액션: 폴더 열기 / 복사 / 지우기
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>실행 로그 한 줄.</summary>
    public class LogLine
    {
        public string Timestamp { get; set; } = "";
        public string Level     { get; set; } = "INFO"; // INFO / OK / WARN / ERROR
        public string Message   { get; set; } = "";

        public override string ToString() => $"{Timestamp}  {Level,-5}  {Message}";
    }

    public class KgBuilderViewModel : INotifyPropertyChanged
    {
        private readonly KgViewModel _kg;

        public KgBuilderViewModel(KgViewModel kg)
        {
            _kg = kg ?? throw new ArgumentNullException(nameof(kg));

            LogLines = new ObservableCollection<LogLine>();

            // v3 KgViewModel 의 속성 변동을 구독해서 v4 화면 갱신
            _kg.PropertyChanged += OnKgPropertyChanged;

            // v3 명령 패스스루
            BuildCommand            = _kg.BuildCommand;
            ChangeBuilderSrcCommand = _kg.ChangeBuilderSrcCommand;
            ImportFileCommand       = _kg.ImportFileCommand;

            // v4 신규 보조 명령
            OpenOutputFolderCommand = new SimpleCommand(_ => OpenOutputFolder());
            CopyLogCommand          = new SimpleCommand(_ => CopyLog());
            ClearLogCommand         = new SimpleCommand(_ => LogLines.Clear());

            // 일시정지/중지는 v3 빌더가 지원하지 않으므로 비활성 상태로만 노출
            PauseCommand = new SimpleCommand(_ => { /* not supported by v3 */ }, _ => false);
            StopCommand  = new SimpleCommand(_ => { /* not supported by v3 */ }, _ => false);

            // 초기 진행 상태
            UpdateProgressFromStatus();
        }

        // ── 도메인 / 스크립트 / 출력경로 / 추가 인자 ──
        public ObservableCollection<KgDomain> Domains => _kg.Domains;

        // latest KgViewModel.SelectedDomain 은 도메인 코드(string) 를 보관한다.
        // 본 VM 의 XAML 콤보박스는 KgDomain 객체를 바인딩하므로,
        // string ↔ KgDomain 변환 어댑터 역할을 한다.
        public KgDomain? SelectedDomain
        {
            get
            {
                var code = _kg.SelectedDomain;
                if (string.IsNullOrEmpty(code)) return null;
                return _kg.Domains.FirstOrDefault(d => d.Code == code);
            }
            set
            {
                _kg.SelectedDomain = value?.Code ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDomainName));
                OnPropertyChanged(nameof(ScriptName));
                OnPropertyChanged(nameof(OutputPath));
            }
        }

        public string SelectedDomainName
        {
            get
            {
                var code = _kg.SelectedDomain;
                if (string.IsNullOrEmpty(code)) return "(선택)";
                var dom = _kg.Domains.FirstOrDefault(d => d.Code == code);
                return dom != null ? $"{dom.Label} ({dom.Code})" : code;
            }
        }

        /// <summary>
        /// 현재 도메인의 build_kg_*.py 파일명 (실제 경로는 KgBuilderRunner 가 탐색).
        /// ★ 빌더 종류가 'none' 인 도메인은 빌드 스크립트가 없으므로 파일명을 출력하지 않는다.
        ///   (외부 TTL/JSON 을 [KG 임포트] 로만 채우는 도메인)
        /// </summary>
        public string ScriptName
        {
            get
            {
                var code = _kg.SelectedDomain;
                if (string.IsNullOrWhiteSpace(code)) return "(도메인 미선택)";

                // 도메인 메타에서 빌더 종류 확인 — 'none' 이면 스크립트 명 미표시
                var dom = _kg.Domains.FirstOrDefault(d => d.Code == code);
                if (dom != null &&
                    string.Equals(dom.BuilderKind, "none", StringComparison.OrdinalIgnoreCase))
                    return "(해당 없음 — 빌더 종류 '없음')";

                return $"build_kg_{code}.py";
            }
        }

        /// <summary>
        /// 도메인별 출력 JSON 경로 — Image 1 의 "출력 경로" 라인.
        /// ★ 빌더 종류가 'none' 인 도메인은 빌드 산출물이 없으므로 경로를 출력하지 않는다.
        /// </summary>
        public string OutputPath
        {
            get
            {
                var code = _kg.SelectedDomain;
                if (string.IsNullOrWhiteSpace(code)) return "(도메인 미선택)";
                var dom = _kg.Domains.FirstOrDefault(d => d.Code == code);

                // 'none' 빌더 도메인은 빌드 산출 경로 미표시
                if (dom != null &&
                    string.Equals(dom.BuilderKind, "none", StringComparison.OrdinalIgnoreCase))
                    return "(해당 없음 — 빌더 종류 '없음')";

                if (dom == null) return $"%APPDATA%/SPILab/NowMoment/kg_builder/kg_{code}.json";
                try { return KgBuilderRunner.OutputJsonPath(dom); }
                catch { return $"%APPDATA%/SPILab/NowMoment/kg_builder/kg_{code}.json"; }
            }
        }

        private string _extraArgs = "";
        /// <summary>화면 표시 전용 추가 인자 — 실제 적용은 추후 KgBuilderRunner.RunAsync 확장 시 연결.</summary>
        public string ExtraArgs
        {
            get => _extraArgs;
            set { if (_extraArgs != value) { _extraArgs = value; OnPropertyChanged(); } }
        }

        // ── 진행 표시 ──
        public bool IsBuilding => _kg.IsBuilding;
        public string CurrentStatus => _kg.BuildStatus;

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            private set { if (Math.Abs(_progressPercent - value) > 0.01) { _progressPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressLabel)); } }
        }

        public string ProgressLabel => ProgressPercent > 0 ? $"{ProgressPercent:0}%" : (IsBuilding ? "진행 중..." : "준비됨");

        private string _etaText = "";
        public string EtaText
        {
            get => _etaText;
            private set { if (_etaText != value) { _etaText = value; OnPropertyChanged(); } }
        }

        // ── 실행 로그 ──
        public ObservableCollection<LogLine> LogLines { get; }

        // ── Commands ──
        public ICommand BuildCommand            { get; }
        public ICommand ChangeBuilderSrcCommand { get; }
        public ICommand ImportFileCommand       { get; }
        public ICommand PauseCommand            { get; }
        public ICommand StopCommand             { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand CopyLogCommand          { get; }
        public ICommand ClearLogCommand         { get; }

        // ── KgViewModel 속성 변동 핸들러 ──

        private void OnKgPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(KgViewModel.BuildStatus):
                    AppendStatusToLog(_kg.BuildStatus);
                    UpdateProgressFromStatus();
                    OnPropertyChanged(nameof(CurrentStatus));
                    OnPropertyChanged(nameof(ProgressLabel));
                    break;
                case nameof(KgViewModel.IsBuilding):
                    OnPropertyChanged(nameof(IsBuilding));
                    OnPropertyChanged(nameof(ProgressLabel));
                    if (!_kg.IsBuilding)
                    {
                        // 빌드 종료 시 진행률 마무리
                        if (_kg.BuildStatus.Contains("완료", StringComparison.OrdinalIgnoreCase))
                            ProgressPercent = 100;
                        EtaText = "";
                    }
                    break;
                case nameof(KgViewModel.SelectedDomain):
                    OnPropertyChanged(nameof(SelectedDomain));
                    OnPropertyChanged(nameof(SelectedDomainName));
                    OnPropertyChanged(nameof(ScriptName));
                    OnPropertyChanged(nameof(OutputPath));
                    break;
            }
        }

        /// <summary>v3 BuildStatus 한 줄을 LogLines 컬렉션에 누적.</summary>
        private void AppendStatusToLog(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return;

            var (level, msg) = DetectLevel(status);
            LogLines.Add(new LogLine
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Level     = level,
                Message   = msg,
            });

            // 너무 많이 쌓이면 잘라내기 (5000 라인 상한)
            while (LogLines.Count > 5000)
                LogLines.RemoveAt(0);
        }

        /// <summary>라인 내용에서 INFO/OK/WARN/ERROR 추정.</summary>
        private static (string level, string msg) DetectLevel(string line)
        {
            // 명시적 접두어가 있으면 그대로 사용
            var m = Regex.Match(line, @"^\s*(INFO|OK|WARN(?:ING)?|ERROR|FAIL(?:ED)?)\b[:\s]\s*(.*)$",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var lvl = m.Groups[1].Value.ToUpperInvariant();
                if (lvl.StartsWith("WARN")) lvl = "WARN";
                if (lvl.StartsWith("FAIL")) lvl = "ERROR";
                return (lvl, m.Groups[2].Value.Trim());
            }
            // 키워드 휴리스틱
            if (line.Contains("실패", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("예외",  StringComparison.OrdinalIgnoreCase))
                return ("ERROR", line);
            if (line.Contains("완료", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Wrote", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
                return ("OK", line);
            return ("INFO", line);
        }

        /// <summary>
        /// 빌더 출력 라인에서 진행률 추정.
        /// 빌더가 명시적인 % 또는 "n/m" 형태를 출력하면 사용, 아니면 단계별 추정.
        /// </summary>
        private void UpdateProgressFromStatus()
        {
            var s = _kg.BuildStatus ?? "";
            if (string.IsNullOrWhiteSpace(s)) return;

            // 1) 명시적 퍼센트: "65%"
            var pm = Regex.Match(s, @"(\d{1,3})\s*%");
            if (pm.Success && double.TryParse(pm.Groups[1].Value, out var pct))
            {
                ProgressPercent = Math.Min(100, Math.Max(0, pct));
                return;
            }
            // 2) n/m 카운트: "85/130 노드"
            var nm = Regex.Match(s, @"(\d+)\s*/\s*(\d+)");
            if (nm.Success
                && int.TryParse(nm.Groups[1].Value, out var n)
                && int.TryParse(nm.Groups[2].Value, out var m)
                && m > 0)
            {
                ProgressPercent = Math.Min(100, n * 100.0 / m);
                return;
            }
            // 3) 단계 키워드 기반 추정
            //    ★ 종료 상태("완료")를 부분 진행 키워드("임포트","빌드")보다 먼저 검사한다.
            //      "임포트 완료 — 노드 71 · 엣지 107" 같은 최종 상태가
            //      '임포트' 에 먼저 걸려 90% 로 고정되던 버그 수정.
            if (s.Contains("실패") || s.Contains("거부") || s.Contains("예외"))
            {
                // 실패/거부 상태는 진행률을 건드리지 않음 (현재 값 유지)
            }
            else if (s.Contains("완료"))
            {
                // "빌드 완료", "임포트 완료", "KG 빌드 완료" 등 모든 종료 상태 → 100%
                ProgressPercent = 100;
            }
            else if (s.Contains("임포트"))   ProgressPercent = 90;  // "임포트 중..." 등 진행 중
            else if (s.Contains("2/2"))      ProgressPercent = 60;
            else if (s.Contains("1/2"))      ProgressPercent = 25;
            else if (_kg.IsBuilding && ProgressPercent < 5) ProgressPercent = 5;
        }

        // ── v4 신규 보조 명령 구현 ──

        private void OpenOutputFolder()
        {
            try
            {
                var dir = KgBuilderRunner.OutputDir;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("출력 폴더 열기 실패:\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyLog()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var l in LogLines)
                    sb.AppendLine(l.ToString());
                if (sb.Length > 0)
                    Clipboard.SetText(sb.ToString());
            }
            catch { /* 클립보드 실패는 무시 */ }
        }

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
