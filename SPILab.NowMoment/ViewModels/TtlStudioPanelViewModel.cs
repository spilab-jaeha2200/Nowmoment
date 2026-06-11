// ════════════════════════════════════════════════════════════════════
// ViewModels/TtlStudioPanelViewModel.cs — v4 TTL Studio 화면 전용 VM
//
// 모든 동작은 v3.0 TtlStudioViewModel 의 명령을 그대로 패스스루:
//   • OpenFileCommand / SaveFileCommand / SaveAsCommand     → TTL 파일 입출력
//   • RunSparqlCommand / ClearSparqlCommand                 → SPARQL 실행
//   • AddClass/Property/Instance/Triple + Remove*           → 컬렉션 CRUD
//
// v4 화면 특화 표시:
//   • 자동저장 상태 라벨 (v3 TtlStudioViewModel.V4Persist 가 이미 1초 디바운스로 작동)
//   • 표준 RDF prefix 5개 정적 표시 (spi/rdf/rdfs/owl/xsd) — Image 1
//   • Classes 수 / Properties 수 카드 (Ontology.Classes.Count, Properties.Count)
//   • SPARQL 결과 행수 헤더
//   • CSV/JSON 내보내기 (v4 신규 보조 명령)
//
// CRUD 자체는 v3 가 처리하므로 v4 는 표시 보강만 담당.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Services.Ttl;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>좌측 패널의 표준 RDF prefix 한 줄.</summary>
    public class PrefixRow
    {
        public string Prefix { get; init; } = "";
        public string Iri    { get; init; } = "";
    }

    public class TtlStudioPanelViewModel : INotifyPropertyChanged
    {
        private readonly TtlStudioViewModel _ttl;

        public TtlStudioPanelViewModel(TtlStudioViewModel ttl)
        {
            _ttl = ttl ?? throw new ArgumentNullException(nameof(ttl));

            // 표준 RDF prefix (Image 1)
            //   spi 는 ontology base prefix 와 일치하도록 v3 BasePrefix/BaseUri 사용
            Prefixes = new ObservableCollection<PrefixRow>
            {
                new() { Prefix = "spi",  Iri = $"<{_ttl.BaseUri}>" },
                new() { Prefix = "rdf",  Iri = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#>" },
                new() { Prefix = "rdfs", Iri = "<http://www.w3.org/2000/01/rdf-schema#>" },
                new() { Prefix = "owl",  Iri = "<http://www.w3.org/2002/07/owl#>" },
                new() { Prefix = "xsd",  Iri = "<http://www.w3.org/2001/XMLSchema#>" },
            };

            // v3 변경 구독
            _ttl.PropertyChanged += OnTtlPropertyChanged;
            _ttl.Classes.CollectionChanged    += (_, __) => OnPropertyChanged(nameof(ClassesHeader));
            _ttl.Properties.CollectionChanged += (_, __) => OnPropertyChanged(nameof(PropertiesHeader));
            _ttl.SparqlResults.CollectionChanged += (_, __) => OnPropertyChanged(nameof(SparqlResultsHeader));

            // v3 명령 패스스루
            OpenFileCommand    = _ttl.OpenFileCommand;
            SaveFileCommand    = _ttl.SaveFileCommand;
            SaveAsCommand      = _ttl.SaveAsCommand;
            NewOntologyCommand = _ttl.NewOntologyCommand;

            RunSparqlCommand   = _ttl.RunSparqlCommand;
            ClearSparqlCommand = _ttl.ClearSparqlCommand;

            AddClassCommand       = _ttl.AddClassCommand;
            RemoveClassCommand    = _ttl.RemoveClassCommand;
            AddPropertyCommand    = _ttl.AddPropertyCommand;
            RemovePropertyCommand = _ttl.RemovePropertyCommand;
            AddInstanceCommand    = _ttl.AddInstanceCommand;
            RemoveInstanceCommand = _ttl.RemoveInstanceCommand;
            AddTripleCommand      = _ttl.AddTripleCommand;
            RemoveTripleCommand   = _ttl.RemoveTripleCommand;

            // v4 신규 — 결과 내보내기 / SPARQL 저장
            ExportCsvCommand   = new SimpleCommand(_ => ExportResults("csv"),  _ => _ttl.SparqlResults.Count > 0);
            ExportJsonCommand  = new SimpleCommand(_ => ExportResults("json"), _ => _ttl.SparqlResults.Count > 0);
            CopyResultsCommand = new SimpleCommand(_ => CopyResults(),         _ => _ttl.SparqlResults.Count > 0);
            ShowTreeCommand    = new SimpleCommand(_ => ShowFullTree());
            SaveSparqlCommand  = new SimpleCommand(_ => SaveSparqlText(), _ => !string.IsNullOrWhiteSpace(_ttl.SparqlText));
        }

        // ── 좌측: Prefixes / Classes / Properties ──
        public ObservableCollection<PrefixRow> Prefixes { get; }
        public string PrefixesHeader => $"📚 Prefixes ({Prefixes.Count})  ·  Classes ({_ttl.Classes.Count})";
        public string ClassesHeader  => $"📁 owl:Class ({_ttl.Classes.Count})";
        public string PropertiesHeader => $"📁 owl:ObjectProperty ({_ttl.Properties.Count})";

        // ── 중앙 SPARQL ──
        public string SparqlText
        {
            get => _ttl.SparqlText;
            set { _ttl.SparqlText = value; OnPropertyChanged(); (SaveSparqlCommand as SimpleCommand)?.RaiseCanExecuteChanged(); }
        }

        // ── 하단 결과 ──
        public ObservableCollection<SparqlResultRow> SparqlResults => _ttl.SparqlResults;
        public DataView? SparqlResultsView => _ttl.SparqlResultsView;
        public string SparqlResultsHeader => $"📊 결과  ({_ttl.SparqlResults.Count}행)";
        public string SparqlStatus => _ttl.SparqlStatus;

        // ── 상단 상태 ──
        public string StatusMessage => _ttl.StatusMessage;
        public string WindowTitle   => _ttl.WindowTitle;

        /// <summary>Image 1 우상단: "● 동기화됨 · ttl_ontology #1 · 2026-05-19 14:30"</summary>
        public string SyncStatusText
        {
            get
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                return $"동기화됨  ·  ttl_ontology #1  ·  {ts}";
            }
        }

        public string AutosaveLabel => "☑ 자동저장 (1초 디바운스)";

        // ── Commands ──
        public ICommand OpenFileCommand        { get; }
        public ICommand SaveFileCommand        { get; }
        public ICommand SaveAsCommand          { get; }
        public ICommand NewOntologyCommand     { get; }

        public ICommand RunSparqlCommand       { get; }
        public ICommand ClearSparqlCommand     { get; }
        public ICommand SaveSparqlCommand      { get; }

        public ICommand AddClassCommand        { get; }
        public ICommand RemoveClassCommand     { get; }
        public ICommand AddPropertyCommand     { get; }
        public ICommand RemovePropertyCommand  { get; }
        public ICommand AddInstanceCommand     { get; }
        public ICommand RemoveInstanceCommand  { get; }
        public ICommand AddTripleCommand       { get; }
        public ICommand RemoveTripleCommand    { get; }

        public ICommand ExportCsvCommand       { get; }
        public ICommand ExportJsonCommand      { get; }
        public ICommand CopyResultsCommand     { get; }
        public ICommand ShowTreeCommand        { get; }

        // ── 내부 ──

        private void OnTtlPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TtlStudioViewModel.StatusMessage):
                    OnPropertyChanged(nameof(StatusMessage)); break;
                case nameof(TtlStudioViewModel.WindowTitle):
                case nameof(TtlStudioViewModel.CurrentFilePath):
                    OnPropertyChanged(nameof(WindowTitle)); break;
                case nameof(TtlStudioViewModel.SparqlText):
                    OnPropertyChanged(nameof(SparqlText));
                    (SaveSparqlCommand as SimpleCommand)?.RaiseCanExecuteChanged();
                    break;
                case nameof(TtlStudioViewModel.SparqlStatus):
                    OnPropertyChanged(nameof(SparqlStatus)); break;
                case nameof(TtlStudioViewModel.SparqlResultsView):
                    OnPropertyChanged(nameof(SparqlResultsView)); break;
                case nameof(TtlStudioViewModel.BaseUri):
                    // spi: prefix 의 IRI 갱신
                    if (Prefixes.Count > 0) Prefixes[0] = new PrefixRow { Prefix = "spi", Iri = $"<{_ttl.BaseUri}>" };
                    break;
            }
        }

        private void ExportResults(string format)
        {
            if (_ttl.SparqlResults.Count == 0) return;
            var defaultName = format == "csv"
                ? $"sparql_result_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                : $"sparql_result_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filter = format == "csv" ? "CSV 파일 (*.csv)|*.csv" : "JSON 파일 (*.json)|*.json";
            var dlg = new SaveFileDialog { FileName = defaultName, Filter = filter };
            if (dlg.ShowDialog() != true) return;

            try
            {
                if (format == "csv") WriteCsv(dlg.FileName);
                else                 WriteJson(dlg.FileName);

                MessageBox.Show($"{format.ToUpper()} 저장 완료:\n{dlg.FileName}",
                    "내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{format.ToUpper()} 내보내기 실패:\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WriteCsv(string path)
        {
            // SparqlResults 는 동적 칼럼이므로 SparqlResultsView 가 우선
            var rows = _ttl.SparqlResults;
            var keys = CollectColumnKeys(rows);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", keys.Select(EscapeCsv)));
            foreach (var row in rows)
            {
                var values = keys.Select(k => EscapeCsv(GetRowValue(row, k)));
                sb.AppendLine(string.Join(",", values));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private void WriteJson(string path)
        {
            var rows = _ttl.SparqlResults;
            var keys = CollectColumnKeys(rows);
            var list = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var dict = new Dictionary<string, string>();
                foreach (var k in keys) dict[k] = GetRowValue(row, k);
                list.Add(dict);
            }
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private void CopyResults()
        {
            try
            {
                var rows = _ttl.SparqlResults;
                var keys = CollectColumnKeys(rows);
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\t", keys));
                foreach (var row in rows)
                    sb.AppendLine(string.Join("\t", keys.Select(k => GetRowValue(row, k))));
                Clipboard.SetText(sb.ToString());
            }
            catch { /* 무시 */ }
        }

        /// <summary>SparqlResultRow.Values 에서 결과 컬럼 키 수집 (등장 순서 보존).</summary>
        private static List<string> CollectColumnKeys(IEnumerable<SparqlResultRow> rows)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>();
            foreach (var r in rows.Take(50))   // 첫 50개 행에서 충분히 키 수집
            {
                if (r?.Values == null) continue;
                foreach (var k in r.Values.Keys)
                    if (seen.Add(k)) keys.Add(k);
            }
            return keys;
        }

        private static string GetRowValue(SparqlResultRow row, string key)
        {
            if (row?.Values == null) return "";
            return row.Values.TryGetValue(key, out var v) ? (v ?? "") : "";
        }

        private static string EscapeCsv(string? v)
        {
            v ??= "";
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        private void ShowFullTree()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📚 Ontology Summary ({_ttl.Summary})");
            sb.AppendLine();
            sb.AppendLine($"owl:Class ({_ttl.Classes.Count})");
            foreach (var c in _ttl.Classes)
                sb.AppendLine($"  • {c.LocalName}" + (string.IsNullOrEmpty(c.Label) ? "" : $"  ({c.Label})"));
            sb.AppendLine();
            sb.AppendLine($"owl:ObjectProperty ({_ttl.Properties.Count})");
            foreach (var p in _ttl.Properties)
                sb.AppendLine($"  • {p.LocalName}  ({p.KindLabel})  {p.Domain} → {p.Range}");
            sb.AppendLine();
            sb.AppendLine($"Instances ({_ttl.Instances.Count})");
            foreach (var i in _ttl.Instances)
                sb.AppendLine($"  • {i.LocalName}  ({i.ClassOf})");

            MessageBox.Show(sb.ToString(), "전체 트리 보기",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveSparqlText()
        {
            if (string.IsNullOrWhiteSpace(_ttl.SparqlText)) return;
            var dlg = new SaveFileDialog
            {
                FileName = $"query_{DateTime.Now:yyyyMMdd_HHmmss}.rq",
                Filter   = "SPARQL 쿼리 (*.rq;*.sparql)|*.rq;*.sparql|모든 파일 (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, _ttl.SparqlText, Encoding.UTF8);
                MessageBox.Show($"쿼리 저장 완료:\n{dlg.FileName}",
                    "SPARQL 저장", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("쿼리 저장 실패:\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── INotifyPropertyChanged ──
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
