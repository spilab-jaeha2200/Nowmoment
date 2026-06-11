// ════════════════════════════════════════════════════════════
// TtlStudioViewModel.cs — v3.0 F-005 TTL Studio (Step 5.2)
//
// 4개 서브탭 (Class / Property / Instance / Triple) + SPARQL 쿼리 패널을
// 한 다이얼로그에서 통합 제어하는 ViewModel.
//
// 주요 책임:
//   1) TtlOntology 인스턴스 보유 + ObservableCollection 직접 노출
//   2) 각 탭의 [+ 추가] [✗ 삭제] 명령 — 5종(클래스/속성/인스턴스/트리플) × 2개
//   3) 파일 I/O — .ttl 열기 / 저장 / 새로 시작
//   4) SPARQL 쿼리 실행 + 결과 표 노출
//   5) 자동완성 후보 — 클래스/속성/인스턴스 LocalName 콤보박스용
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models.Ttl;
using SPILab.NowMoment.Services.Ttl;

namespace SPILab.NowMoment.ViewModels
{
    public partial class TtlStudioViewModel : BaseViewModel
    {
        private readonly TtlIOService _io;
        private readonly TtlSparqlEngine _sparql;

        public TtlOntology Ontology { get; private set; } = new();

        // 4종 컬렉션을 그대로 노출 (DataGrid 가 직접 바인딩)
        public ObservableCollection<TtlClass>    Classes    => Ontology.Classes;
        public ObservableCollection<TtlProperty> Properties => Ontology.Properties;
        public ObservableCollection<TtlInstance> Instances  => Ontology.Instances;
        public ObservableCollection<TtlTriple>   Triples    => Ontology.Triples;

        // 콤보박스 자동완성용 — Classes/Instances 의 LocalName 만 모은 리스트
        public IEnumerable<string> ClassNames => Classes.Select(c => c.LocalName);
        public IEnumerable<string> InstanceNames => Instances.Select(i => i.LocalName);

        // ── 선택 항목 ──────────────────────────────────
        private TtlClass?    _selectedClass;
        private TtlProperty? _selectedProperty;
        private TtlInstance? _selectedInstance;
        private TtlTriple?   _selectedTriple;

        public TtlClass?    SelectedClass    { get => _selectedClass;    set => Set(ref _selectedClass, value); }
        public TtlProperty? SelectedProperty { get => _selectedProperty; set => Set(ref _selectedProperty, value); }
        public TtlInstance? SelectedInstance { get => _selectedInstance; set => Set(ref _selectedInstance, value); }
        public TtlTriple?   SelectedTriple   { get => _selectedTriple;   set => Set(ref _selectedTriple, value); }

        // ── 헤더 정보 ──────────────────────────────────
        private string _baseUri = "http://spilab.ai/ontology#";
        public string BaseUri
        {
            get => _baseUri;
            set
            {
                if (Set(ref _baseUri, value))
                {
                    Ontology.BaseUri = value;
                }
            }
        }

        private string _basePrefix = "spilab";
        public string BasePrefix
        {
            get => _basePrefix;
            set
            {
                if (Set(ref _basePrefix, value))
                {
                    Ontology.BasePrefix = value;
                }
            }
        }

        public string Summary => Ontology.Summary;

        private string _statusMessage = "TTL 파일을 열거나 새로 작성을 시작하세요.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private string _currentFilePath = "";
        public string CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (Set(ref _currentFilePath, value))
                    OnPropertyChanged(nameof(WindowTitle));
            }
        }

        public string WindowTitle => string.IsNullOrEmpty(_currentFilePath)
            ? "🛠️ TTL Studio — (새 온톨로지)"
            : $"🛠️ TTL Studio — {System.IO.Path.GetFileName(_currentFilePath)}";

        // ── SPARQL 패널 ────────────────────────────────
        private string _sparqlText = "SELECT ?cls ?label\nWHERE { ?cls a owl:Class .\n        OPTIONAL { ?cls rdfs:label ?label }\n}\nORDER BY ?cls\nLIMIT 50";
        public string SparqlText
        {
            get => _sparqlText;
            set => Set(ref _sparqlText, value);
        }

        public ObservableCollection<SparqlResultRow> SparqlResults { get; } = new();

        private DataView? _sparqlResultsView;
        /// <summary>SPARQL 결과 — 동적 컬럼이라 DataTable 기반 DataView 사용.</summary>
        public DataView? SparqlResultsView
        {
            get => _sparqlResultsView;
            set => Set(ref _sparqlResultsView, value);
        }

        private string _sparqlStatus = "";
        public string SparqlStatus
        {
            get => _sparqlStatus;
            set => Set(ref _sparqlStatus, value);
        }

        // ── 명령 ───────────────────────────────────────
        public ICommand NewOntologyCommand   { get; }
        public ICommand OpenFileCommand      { get; }
        public ICommand SaveFileCommand      { get; }
        public ICommand SaveAsCommand        { get; }

        public ICommand AddClassCommand      { get; }
        public ICommand RemoveClassCommand   { get; }
        public ICommand AddPropertyCommand   { get; }
        public ICommand RemovePropertyCommand{ get; }
        public ICommand AddInstanceCommand   { get; }
        public ICommand RemoveInstanceCommand{ get; }
        public ICommand AddTripleCommand     { get; }
        public ICommand RemoveTripleCommand  { get; }

        public ICommand RunSparqlCommand     { get; }
        public ICommand ClearSparqlCommand   { get; }

        public TtlStudioViewModel()
        {
            _io     = new TtlIOService();
            _sparql = new TtlSparqlEngine();

            // 컬렉션 변경 시 Summary 갱신
            Classes.CollectionChanged    += (_, _) => OnPropertyChanged(nameof(Summary));
            Properties.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
            Instances.CollectionChanged  += (_, _) => { OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(InstanceNames)); };
            Triples.CollectionChanged    += (_, _) => OnPropertyChanged(nameof(Summary));
            Classes.CollectionChanged    += (_, _) => OnPropertyChanged(nameof(ClassNames));

            NewOntologyCommand = new RelayCommand(_ => DoNew());
            OpenFileCommand    = new RelayCommand(_ => DoOpenFile());
            SaveFileCommand    = new RelayCommand(_ => DoSaveFile(false));
            SaveAsCommand      = new RelayCommand(_ => DoSaveFile(true));

            AddClassCommand       = new RelayCommand(_ => AddClass());
            RemoveClassCommand    = new RelayCommand(_ => RemoveSelectedClass(),  _ => SelectedClass != null);
            AddPropertyCommand    = new RelayCommand(_ => AddProperty());
            RemovePropertyCommand = new RelayCommand(_ => RemoveSelectedProperty(), _ => SelectedProperty != null);
            AddInstanceCommand    = new RelayCommand(_ => AddInstance());
            RemoveInstanceCommand = new RelayCommand(_ => RemoveSelectedInstance(), _ => SelectedInstance != null);
            AddTripleCommand      = new RelayCommand(_ => AddTriple());
            RemoveTripleCommand   = new RelayCommand(_ => RemoveSelectedTriple(), _ => SelectedTriple != null);

            RunSparqlCommand   = new RelayCommand(_ => DoRunSparql(),  _ => !string.IsNullOrWhiteSpace(_sparqlText));
            ClearSparqlCommand = new RelayCommand(_ => DoClearSparql());
        }

        // ── 파일 I/O ──────────────────────────────────
        private void DoNew()
        {
            if (Classes.Count + Properties.Count + Instances.Count + Triples.Count > 0)
            {
                var r = MessageBox.Show(
                    "현재 작업을 버리고 새 온톨로지를 시작하시겠습니까?\n저장되지 않은 변경 사항은 사라집니다.",
                    "새로 시작", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }
            Ontology.Clear();
            CurrentFilePath = "";
            BaseUri    = "http://spilab.ai/ontology#";
            BasePrefix = "spilab";
            ClearSparqlResults();
            StatusMessage = "새 온톨로지를 시작했습니다.";
            OnPropertyChanged(nameof(Summary));
        }

        private void DoOpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "TTL 파일 열기",
                Filter = "Turtle 파일 (*.ttl)|*.ttl|모든 파일 (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var loaded = _io.Import(dlg.FileName);
                // 기존 컬렉션을 비우고 새 데이터로 채움
                Ontology.Clear();
                foreach (var c in loaded.Classes)    Ontology.Classes.Add(c);
                foreach (var p in loaded.Properties) Ontology.Properties.Add(p);
                foreach (var i in loaded.Instances)  Ontology.Instances.Add(i);
                foreach (var t in loaded.Triples)    Ontology.Triples.Add(t);
                Ontology.BaseUri    = loaded.BaseUri;
                Ontology.BasePrefix = loaded.BasePrefix;
                _baseUri    = loaded.BaseUri;    OnPropertyChanged(nameof(BaseUri));
                _basePrefix = loaded.BasePrefix; OnPropertyChanged(nameof(BasePrefix));

                CurrentFilePath = dlg.FileName;
                StatusMessage = $"열림: {System.IO.Path.GetFileName(dlg.FileName)} — {Summary}";
                ClearSparqlResults();
                OnPropertyChanged(nameof(Summary));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"TTL 파일 열기 실패:\n\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoSaveFile(bool forceSaveAs)
        {
            string path = CurrentFilePath;
            if (forceSaveAs || string.IsNullOrEmpty(path))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title  = "TTL 파일 저장",
                    Filter = "Turtle 파일 (*.ttl)|*.ttl|모든 파일 (*.*)|*.*",
                    DefaultExt = "ttl",
                    AddExtension = true,
                    FileName = string.IsNullOrEmpty(path)
                        ? $"{BasePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.ttl"
                        : System.IO.Path.GetFileName(path),
                };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }

            try
            {
                _io.Export(Ontology, path);
                CurrentFilePath = path;
                StatusMessage = $"저장됨: {System.IO.Path.GetFileName(path)} — {Summary}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"TTL 파일 저장 실패:\n\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── 항목 추가/삭제 ────────────────────────────
        private void AddClass()
        {
            var c = new TtlClass { LocalName = NextLocalName("NewClass", ClassNames) };
            Classes.Add(c);
            SelectedClass = c;
            StatusMessage = $"클래스 추가: {c.LocalName}";
        }

        private void RemoveSelectedClass()
        {
            if (SelectedClass == null) return;
            var name = SelectedClass.LocalName;
            Classes.Remove(SelectedClass);
            StatusMessage = $"클래스 삭제: {name}";
        }

        private void AddProperty()
        {
            var p = new TtlProperty
            {
                LocalName = NextLocalName("newProperty", Properties.Select(x => x.LocalName)),
                Kind = TtlPropertyKind.ObjectProperty,
            };
            Properties.Add(p);
            SelectedProperty = p;
            StatusMessage = $"속성 추가: {p.LocalName}";
        }

        private void RemoveSelectedProperty()
        {
            if (SelectedProperty == null) return;
            var name = SelectedProperty.LocalName;
            Properties.Remove(SelectedProperty);
            StatusMessage = $"속성 삭제: {name}";
        }

        private void AddInstance()
        {
            var i = new TtlInstance { LocalName = NextLocalName("instance", InstanceNames) };
            Instances.Add(i);
            SelectedInstance = i;
            StatusMessage = $"인스턴스 추가: {i.LocalName}";
        }

        private void RemoveSelectedInstance()
        {
            if (SelectedInstance == null) return;
            var name = SelectedInstance.LocalName;
            Instances.Remove(SelectedInstance);
            StatusMessage = $"인스턴스 삭제: {name}";
        }

        private void AddTriple()
        {
            var t = new TtlTriple();
            Triples.Add(t);
            SelectedTriple = t;
            StatusMessage = "트리플 행 추가됨 (s/p/o 입력하세요)";
        }

        private void RemoveSelectedTriple()
        {
            if (SelectedTriple == null) return;
            Triples.Remove(SelectedTriple);
            StatusMessage = "트리플 삭제";
        }

        // ── SPARQL ────────────────────────────────────
        private void DoRunSparql()
        {
            try
            {
                var rows = _sparql.ExecuteSelect(Ontology, _sparqlText, out var columns);

                // (1) DataView — DataGrid 동적 컬럼 표시용
                var table = new DataTable();
                foreach (var col in columns)
                    table.Columns.Add(col, typeof(string));
                foreach (var r in rows)
                {
                    var row = table.NewRow();
                    foreach (var col in columns)
                        row[col] = r.Values.TryGetValue(col, out var v) ? v : "";
                    table.Rows.Add(row);
                }
                SparqlResultsView = table.DefaultView;

                // (2) SparqlResults — 결과 행수 헤더 / CSV·JSON·복사 명령이 참조.
                //     이 컬렉션을 채우지 않으면 "결과 (0행)" 으로 표시되고
                //     내보내기 버튼이 비활성화된다.
                SparqlResults.Clear();
                foreach (var r in rows)
                    SparqlResults.Add(r);

                SparqlStatus = $"실행 완료 — {rows.Count} 행 × {columns.Count} 열";
                StatusMessage = $"SPARQL: {rows.Count} 행 반환";
            }
            catch (Exception ex)
            {
                SparqlStatus = $"❌ 쿼리 오류: {ex.Message}";
                StatusMessage = "SPARQL 실행 실패";
            }
        }

        private void DoClearSparql()
        {
            ClearSparqlResults();
            SparqlStatus = "";
        }

        private void ClearSparqlResults()
        {
            SparqlResults.Clear();
            SparqlResultsView = null;
        }

        // ── 헬퍼 ─────────────────────────────────────
        private static string NextLocalName(string baseName, IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            if (!set.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName}{i}";
                if (!set.Contains(candidate)) return candidate;
            }
            return baseName + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
