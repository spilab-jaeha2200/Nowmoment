// ════════════════════════════════════════════════════════════════════
// DashboardViewModel.cs — v4 Phase 5 (홈 대시보드)
//
// 화면 구성:
//   ① 상단 4개 통계 카드 (기술자산 / KG 노드 / 프로젝트 / 백업)
//   ② 좌측 도넛 차트 — 자산 유형별 분포
//   ③ 우측 최근 활동 리스트 (audit_log 최근 N건)
//
// 데이터 소스:
//   - DatabaseService.GetStats()                → 자산 5종 카운트
//   - KnowledgeGraphService.GetStats()          → KG 노드/엣지 + 도메인 수
//   - DatabaseService.GetProjects()             → 프로젝트 수
//   - DatabaseService.GetAuditLogs(limit:7)     → 최근 활동
//
// 라이트/다크 모드:
//   - 차트의 5색은 고정 팔레트(다크/라이트 양쪽에서 가독성 확보된 색상).
//   - 텍스트·배경은 DynamicResource (Theme.*) 를 XAML 에서 사용.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public class DashboardViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly KnowledgeGraphService? _kg;

        // ── 상단 4개 통계 카드 ───────────────────────────
        private int    _assetTotal;
        private string _assetDelta = "";
        private int    _kgNodes;
        private string _kgDomainText = "";
        private int    _projectsTotal;
        private string _projectsActiveText = "";
        private string _backupAgo = "—";
        private string _backupNext = "";

        public int    AssetTotal         { get => _assetTotal;        set => Set(ref _assetTotal, value); }
        public string AssetDelta         { get => _assetDelta;        set => Set(ref _assetDelta, value); }
        public int    KgNodes            { get => _kgNodes;           set => Set(ref _kgNodes, value); }
        public string KgDomainText       { get => _kgDomainText;      set => Set(ref _kgDomainText, value); }
        public int    ProjectsTotal      { get => _projectsTotal;     set => Set(ref _projectsTotal, value); }
        public string ProjectsActiveText { get => _projectsActiveText;set => Set(ref _projectsActiveText, value); }
        public string BackupAgo          { get => _backupAgo;         set => Set(ref _backupAgo, value); }
        public string BackupNext         { get => _backupNext;        set => Set(ref _backupNext, value); }

        // ── 자산 유형별 분포 (도넛 차트용) ──────────────
        public ObservableCollection<ChartSlice> AssetSlices { get; } = new();

        // ── 최근 활동 ──────────────────────────────────
        public ObservableCollection<RecentItem> RecentItems { get; } = new();

        // ── 명령 ───────────────────────────────────────
        public ICommand RefreshCommand { get; }

        public DashboardViewModel(DatabaseService db, KnowledgeGraphService? kg = null)
        {
            _db = db;
            _kg = kg;
            RefreshCommand = new RelayCommand(_ => Refresh());
            Refresh();
        }

        public void Refresh()
        {
            try { LoadAssetStats(); }      catch { /* 통계 실패해도 화면은 떠야 함 */ }
            try { LoadKgStats(); }         catch { }
            try { LoadProjectStats(); }    catch { }
            try { LoadBackupStatus(); }    catch { }
            try { LoadRecentActivity(); }  catch { }
        }

        // ── ① 자산 통계 + 도넛 차트 ─────────────────────
        private void LoadAssetStats()
        {
            var s = _db.GetStats();  // { "소스코드":N, "모델":N, "문서":N, "특허":N, "실험":N }
            AssetTotal = s.Values.Sum();
            AssetDelta = "+5 이번 주";  // TODO: audit_log 에서 7일 내 create 액션 카운트로 교체

            // 도넛 차트 5조각 — 화면설계서/이미지 색상 팔레트
            AssetSlices.Clear();
            var palette = new (string label, string fillHex)[]
            {
                ("소스코드", "#1F3864"),  // deep navy
                ("AI모델",   "#3D7BCE"),  // mid blue
                ("문서",     "#8B7FD1"),  // purple
                ("특허",     "#3DA48B"),  // teal
                ("실험",     "#E8A838"),  // amber
            };
            var keys = new[] { "소스코드", "모델", "문서", "특허", "실험" };

            int total = Math.Max(1, AssetTotal);
            for (int i = 0; i < 5; i++)
            {
                int v = s.TryGetValue(keys[i], out var n) ? n : 0;
                AssetSlices.Add(new ChartSlice
                {
                    Label = palette[i].label,
                    Value = v,
                    Percent = (double)v / total * 100.0,
                    Color = (SolidColorBrush)(new BrushConverter().ConvertFromString(palette[i].fillHex)!),
                });
            }
            OnPropertyChanged(nameof(AssetSlices));
        }

        // ── ② KG 통계 ──────────────────────────────────
        private void LoadKgStats()
        {
            if (_kg == null)
            {
                KgNodes = 0;
                KgDomainText = "—";
                return;
            }
            var ks = _kg.GetStats();
            KgNodes = ks.Nodes;
            // NodesByType 는 type 단위 — 도메인 수는 별도 의미라 일단 type 개수로 근사 표기
            int domainCount = ks.NodesByType?.Count ?? 0;
            KgDomainText = domainCount > 0 ? $"{domainCount}개 도메인" : "—";
        }

        // ── ③ 프로젝트 통계 ────────────────────────────
        private void LoadProjectStats()
        {
            var projects = _db.GetProjects();
            ProjectsTotal = projects.Count;
            int active = projects.Count(p =>
                string.Equals(p.Status, "active", StringComparison.OrdinalIgnoreCase));
            ProjectsActiveText = $"활성 {active}개";
        }

        // ── ④ 백업 상태 ────────────────────────────────
        private void LoadBackupStatus()
        {
            // audit_log 에서 가장 최근 'backup' 액션을 찾음
            var logs = _db.GetAuditLogs(action: "backup", limit: 1);
            if (logs.Count == 0)
            {
                BackupAgo  = "없음";
                BackupNext = "스케줄 미설정";
                return;
            }
            var ts = logs[0].TimeStamp;
            var span = DateTime.Now - ts;
            BackupAgo = span.TotalDays >= 1
                ? $"{(int)span.TotalDays}일전"
                : span.TotalHours >= 1
                    ? $"{(int)span.TotalHours}시간전"
                    : $"{Math.Max(1,(int)span.TotalMinutes)}분전";
            // 단순 휴리스틱: 7일 주기 자동 백업이 기본
            BackupNext = $"다음 자동: {7 - Math.Min(7,(int)span.TotalDays)}일";
        }

        // ── ⑤ 최근 활동 (audit_log) ────────────────────
        private void LoadRecentActivity()
        {
            RecentItems.Clear();
            var logs = _db.GetAuditLogs(limit: 7);
            foreach (var log in logs)
            {
                RecentItems.Add(new RecentItem
                {
                    TimeLabel   = FormatTime(log.TimeStamp),
                    Glyph       = GlyphFor(log.Action),
                    GlyphColor  = ColorFor(log.Action),
                    Description = DescribeLog(log),
                    Actor       = log.Actor,
                });
            }
        }

        private static string FormatTime(DateTime ts)
        {
            if (ts == DateTime.MinValue) return "";
            var today = DateTime.Today;
            if (ts.Date == today)            return ts.ToString("HH:mm");
            if (ts.Date == today.AddDays(-1)) return "어제";
            return ts.ToString("MM/dd");
        }

        private static string GlyphFor(string action) => action switch
        {
            "create" => "+",
            "update" => "✎",
            "delete" => "X",
            "backup" => "💾",
            "import" => "📥",
            "export" => "📤",
            _        => "•",
        };

        private static SolidColorBrush ColorFor(string action) => action switch
        {
            "create" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3DA48B")),
            "update" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D7BCE")),
            "delete" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E85555")),
            "backup" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8A838")),
            "import" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B7FD1")),
            "export" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B7FD1")),
            _        => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7E8FA0")),
        };

        private static string DescribeLog(AuditLog log)
        {
            // 가장 단순한 설명: "{action} {asset_type} #{id}"
            var typeLabel = log.AssetType switch
            {
                "asset_code"       => "소스코드",
                "asset_model"      => "AI 모델",
                "asset_document"   => "문서",
                "asset_patent"     => "특허",
                "asset_experiment" => "실험",
                "kg_node"          => "KG 노드",
                "kg_edge"          => "KG 엣지",
                "asset_kg_link"    => "자산-KG 링크",
                "user_setting"     => "사용자 설정",
                "project"          => "프로젝트",
                "system"           => "시스템",
                _                  => log.AssetType,
            };
            var verb = log.Action switch
            {
                "create" => "등록",
                "update" => "수정",
                "delete" => "삭제",
                "backup" => "백업",
                "import" => "임포트",
                "export" => "내보내기",
                _        => log.Action,
            };
            return log.AssetId.HasValue
                ? $"{typeLabel} #{log.AssetId} {verb}"
                : $"{typeLabel} {verb}";
        }

        // ── 보조 클래스 ────────────────────────────────
        public class ChartSlice
        {
            public string Label   { get; set; } = "";
            public int    Value   { get; set; }
            public double Percent { get; set; }
            public SolidColorBrush Color { get; set; } = new SolidColorBrush(Colors.Gray);
        }

        public class RecentItem
        {
            public string TimeLabel   { get; set; } = "";
            public string Glyph       { get; set; } = "•";
            public SolidColorBrush GlyphColor { get; set; } = new SolidColorBrush(Colors.Gray);
            public string Description { get; set; } = "";
            public string Actor       { get; set; } = "";
        }
    }
}
