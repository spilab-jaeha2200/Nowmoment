// ════════════════════════════════════════════════════════════════════
// AuditLogViewModel.cs — v4 Phase 4 (SCR-A07)
//
// audit_log 테이블 read-only 뷰어. 필터(자산 유형/액션/기간) + 통계 카드.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public class AuditLogViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;

        public ObservableCollection<AuditLog> Logs { get; } = new();

        // 필터
        public ObservableCollection<string> AssetTypeOptions { get; } = new() {
            "(전체)", "asset_code", "asset_model", "asset_document", "asset_patent", "asset_experiment",
            "kg_node", "kg_edge", "asset_kg_link", "user_setting", "system"
        };
        public ObservableCollection<string> ActionOptions { get; } = new() {
            "(전체)", "create", "update", "delete", "backup", "import", "export"
        };

        private string _selectedAssetType = "(전체)";
        public string SelectedAssetType { get => _selectedAssetType; set { if (Set(ref _selectedAssetType, value)) Reload(); } }

        private string _selectedAction = "(전체)";
        public string SelectedAction { get => _selectedAction; set { if (Set(ref _selectedAction, value)) Reload(); } }

        private DateTime? _fromDate = DateTime.Today.AddDays(-7);
        private DateTime? _toDate   = DateTime.Today;
        public DateTime? FromDate { get => _fromDate; set { if (Set(ref _fromDate, value)) Reload(); } }
        public DateTime? ToDate   { get => _toDate;   set { if (Set(ref _toDate,   value)) Reload(); } }

        // 통계 (대시보드 카드)
        private int _statTotal, _statCreate, _statUpdate, _statDelete;
        public int StatTotal  { get => _statTotal;  set => Set(ref _statTotal,  value); }
        public int StatCreate { get => _statCreate; set => Set(ref _statCreate, value); }
        public int StatUpdate { get => _statUpdate; set => Set(ref _statUpdate, value); }
        public int StatDelete { get => _statDelete; set => Set(ref _statDelete, value); }

        // 선택 항목 → diff 패널
        private AuditLog? _selected;
        public AuditLog? SelectedLog
        {
            get => _selected;
            set { if (Set(ref _selected, value)) OnPropertyChanged(nameof(SelectedDiffPretty)); }
        }

        public string SelectedDiffPretty
        {
            get
            {
                if (_selected == null) return "(로그를 선택하면 변경 내역이 표시됩니다)";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(_selected.DiffJson);
                    return System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    });
                }
                catch { return _selected.DiffJson ?? "{}"; }
            }
        }

        // 명령
        public ICommand RefreshCommand   { get; }
        public ICommand ExportCsvCommand { get; }

        public AuditLogViewModel(DatabaseService db)
        {
            _db = db;
            RefreshCommand   = new RelayCommand(_ => Reload());
            ExportCsvCommand = new RelayCommand(_ => DoExportCsv());
            Reload();
        }

        public void Reload()
        {
            try
            {
                var assetType = SelectedAssetType == "(전체)" ? null : SelectedAssetType;
                var action    = SelectedAction    == "(전체)" ? null : SelectedAction;
                var rows = _db.GetAuditLogs(assetType, action, FromDate, ToDate, 500);

                Logs.Clear();
                foreach (var r in rows) Logs.Add(r);

                var counts = _db.GetAuditCounts(FromDate, ToDate);
                StatTotal  = counts.TryGetValue("total",  out var t) ? t : 0;
                StatCreate = counts.TryGetValue("create", out var c) ? c : 0;
                StatUpdate = counts.TryGetValue("update", out var u) ? u : 0;
                StatDelete = counts.TryGetValue("delete", out var d) ? d : 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"이력 조회 실패: {ex.Message}",
                    "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void DoExportCsv()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "이력 CSV 내보내기",
                Filter   = "CSV 파일 (*.csv)|*.csv",
                FileName = $"NowMoment_AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = "csv",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("id,ts,actor,action,asset_type,asset_id,diff_json");
                foreach (var l in Logs)
                {
                    sb.Append(l.Id).Append(',');
                    sb.Append(l.Ts).Append(',');
                    sb.Append(Csv(l.Actor)).Append(',');
                    sb.Append(Csv(l.Action)).Append(',');
                    sb.Append(Csv(l.AssetType)).Append(',');
                    sb.Append(l.AssetId?.ToString() ?? "").Append(',');
                    sb.AppendLine(Csv(l.DiffJson));
                }
                // UTF-8 BOM (Excel 한글 호환)
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(),
                    new System.Text.UTF8Encoding(true));
                System.Windows.MessageBox.Show("✅ CSV 내보내기 완료",
                    "이력 내보내기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"CSV 내보내기 실패: {ex.Message}", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static string Csv(string? s)
        {
            s ??= "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
