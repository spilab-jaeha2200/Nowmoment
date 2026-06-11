using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    // ════════════════════════════════════════════════════════════
    // KgViewModel — KG 탭 전용 VM
    //
    // MainWindow.xaml.cs 또는 App.xaml.cs 에서 다음과 같이 wire 한다:
    //
    //   var db   = new DatabaseService();
    //   var kg   = new KnowledgeGraphService(db.DbPath);
    //   var main = new MainViewModel(db);
    //   main.Kg  = new KgViewModel(kg, main);          // 아래 partial 확장
    //   DataContext = main;
    // ════════════════════════════════════════════════════════════
    public partial class KgViewModel : BaseViewModel
    {
        private readonly KnowledgeGraphService _kg;
        private readonly MainViewModel _main;

        public ObservableCollection<KgNode> Nodes { get; } = new();
        public ObservableCollection<KgEdge> Edges { get; } = new();
        public ObservableCollection<string> NodeTypes { get; } = new()
            { "", "PhysicsRule", "Material", "ProcessParam", "Workspace", "Parameter", "Spec", "Citation" };

        // 통계
        public ObservableCollection<KgStatItem> StatsByType { get; } = new();
        public ObservableCollection<KgStatItem> StatsByRel  { get; } = new();

        // 필터
        private string _typeFilter = "";
        public string TypeFilter
        {
            get => _typeFilter;
            set { if (Set(ref _typeFilter, value)) LoadNodes(); }
        }

        private string _keyword = "";
        public string Keyword
        {
            get => _keyword;
            set { if (Set(ref _keyword, value)) LoadNodes(); }
        }

        // 선택된 노드 (디테일 뷰)
        private KgNode? _selectedNode;
        public KgNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (!Set(ref _selectedNode, value)) return;
                LoadEdgesForSelected();
                LoadLinkedAssetsForSelected();   // v3.0 F-001 Step 1.7
                OnPropertyChanged(nameof(SelectedNodeProps));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
        public bool HasSelection => _selectedNode != null;
        public string SelectedNodeProps =>
            _selectedNode == null ? "{}" : PrettyJson(_selectedNode.PropsJson);

        // ── v3.0 F-001 Step 1.7: 선택된 KG 노드에 연결된 자산 목록 ─
        public ObservableCollection<LinkedAssetRow> LinkedAssetsForSelectedNode { get; } = new();
        public bool HasLinkedAssets => LinkedAssetsForSelectedNode.Count > 0;

        private void LoadLinkedAssetsForSelected()
        {
            LinkedAssetsForSelectedNode.Clear();
            if (_selectedNode == null) { OnPropertyChanged(nameof(HasLinkedAssets)); return; }
            foreach (var row in _kg.GetAssetsLinkedToNode(_selectedNode.Id))
                LinkedAssetsForSelectedNode.Add(row);
            OnPropertyChanged(nameof(HasLinkedAssets));
        }

        /// <summary>v3.0 F-001 Step 1.6: 자산이 1개 이상 연결된 KG 노드 ID 셋 (그래프 강조용).</summary>
        public System.Collections.Generic.HashSet<string> GetLinkedKgNodeIds()
            => _kg.GetLinkedKgNodeIds();

        // 임포트 상태
        private string _importStatus = "KG 미적재 — '레이판 KG 임포트' 버튼을 눌러 JSON 을 불러오세요.";
        public string ImportStatus { get => _importStatus; set => Set(ref _importStatus, value); }

        // Commands
        public ICommand ImportCommand        { get; }
        public ICommand RefreshCommand       { get; }
        public ICommand LinkSelectedAssetCmd { get; }

        public KgViewModel(KnowledgeGraphService kg, MainViewModel main)
        {
            _kg = kg;
            _main = main;
            ImportCommand        = new RelayCommand(_ => DoImport());
            RefreshCommand       = new RelayCommand(_ => Reload());
            LinkSelectedAssetCmd = new RelayCommand(_ => LinkSelectedAsset());
            Reload();

            // ★ v2.6 추가: 도메인 변경 시 화면 갱신
            //    DomainChanged 는 KgViewModel.Builder.cs (partial 확장) 에 정의된 이벤트
            this.DomainChanged += (_, _) => Reload();
        }

        public void Reload()
        {
            LoadNodes();
            LoadStats();
            // ★ v2.6: 도메인별 통계 + 라벨 prefix
            var s = _kg.GetStats("", SelectedDomain);
            ImportStatus = $"[{SelectedDomainLabel}] 노드 {s.Nodes} · 엣지 {s.Edges}";
        }

        private void LoadNodes()
        {
            Nodes.Clear();
            // ★ v2.6: SelectedDomain 으로 필터 — cs / photo 분리 표시
            foreach (var n in _kg.GetNodes(_typeFilter, _keyword, SelectedDomain))
                Nodes.Add(n);
        }

        private void LoadEdgesForSelected()
        {
            Edges.Clear();
            if (_selectedNode == null) return;
            foreach (var e in _kg.GetEdges(_selectedNode.Id)) Edges.Add(e);
        }

// ════════════════════════════════════════════════════════════════════
// 기존 KgViewModel.cs 에 다음 메서드 한 개를 추가하세요.
//
// 위치: 클래스 내부의 적당한 위치 (예: LoadEdgesForSelected 메서드 바로 아래)
//
// 용도: KG 그래프 뷰가 전체 엣지를 한 번에 가져갈 수 있도록 노출.
//       Edges 컬렉션은 "선택된 노드의 인접 엣지"만 담는 용도라 그대로 사용 불가.
// ════════════════════════════════════════════════════════════════════

        /// <summary>전체 KG 엣지를 새 리스트로 반환 (그래프 시각화용).</summary>
        public System.Collections.Generic.List<Models.KgEdge> GetAllEdges()
            => _kg.GetEdges("", "", SelectedDomain);   // ★ v2.6: 도메인 필터 추가


        private void LoadStats()
        {
            // ★ v2.6: 첫 인자는 sourceFile (안 씀), 두 번째가 domain
            var s = _kg.GetStats("", SelectedDomain);
            StatsByType.Clear();
            foreach (var kv in s.NodesByType.OrderByDescending(x => x.Value))
                StatsByType.Add(new KgStatItem { Key = kv.Key, Count = kv.Value });
            StatsByRel.Clear();
            foreach (var kv in s.EdgesByRel.OrderByDescending(x => x.Value))
                StatsByRel.Add(new KgStatItem { Key = kv.Key, Count = kv.Value });
        }

        private void DoImport()
        {
            Models.KgDomain? domain = _domainSvc?.Get(SelectedDomain);
            string kind = domain?.BuilderKind ?? "";

            // ════════════════════════════════════════════
            //  Case A: 사용자 도메인 (none / cs_file / python_engine_folder)
            // ════════════════════════════════════════════
            if (domain != null && (kind == "none" || kind == "cs_file" || kind == "python_engine_folder"))
            {
                string? path = null;

                // ★ v2.7.23: 'none' 도메인 분기 추가 — 빌드를 안 하므로 canonical 경로(kg_<code>.json) 가
                //   존재할 일 없음. settings 에 저장된 사용자 입력 .ttl/.json 경로를 우선 사용.
                //   다이얼로그는 settings 도 부재하거나 파일이 사라진 경우에만 띄움.
                //
                //   cs_file / python_engine_folder 의 동작은 v2.7.21 그대로 유지 (변경 없음).
                if (kind == "none")
                {
                    var savedKeyNone = Services.KgSettingsService.KeyForLastImport(SelectedDomain);
                    var savedValueNone = _settings?.Get(savedKeyNone);

                    if (!string.IsNullOrEmpty(savedValueNone) && System.IO.File.Exists(savedValueNone))
                    {
                        path = savedValueNone;
                    }
                    else
                    {
                        // settings 부재 또는 파일 사라짐 — 다이얼로그 폴백
                        var ofd = new Microsoft.Win32.OpenFileDialog
                        {
                            Title  = $"[{SelectedDomainLabel}] 임포트할 KG 파일 선택",
                            Filter = "KG 파일 (*.ttl;*.json;*.jsonld;*.rdf;*.nt)|*.ttl;*.json;*.jsonld;*.rdf;*.nt|" +
                                    "Turtle (*.ttl)|*.ttl|" +
                                    "JSON-LD (*.json;*.jsonld)|*.json;*.jsonld|" +
                                    "모든 파일|*.*",
                        };
                        try
                        {
                            // savedValue 의 폴더부터 시작하면 자연스러움
                            if (!string.IsNullOrEmpty(savedValueNone))
                            {
                                var dir = System.IO.Path.GetDirectoryName(savedValueNone);
                                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                                    ofd.InitialDirectory = dir;
                            }
                            if (string.IsNullOrEmpty(ofd.InitialDirectory))
                            {
                                var bd = Services.KgBuilderRunner.OutputDir;
                                if (System.IO.Directory.Exists(bd)) ofd.InitialDirectory = bd;
                            }
                        }
                        catch { }
                        if (ofd.ShowDialog() != true) return;
                        path = ofd.FileName;
                    }

                    // none 도메인 임포트 실행 (cs_file/python_engine_folder 의 임포트 실행과 동일 코드)
                    try
                    {
                        var stats = _kg.ImportFromFile(path, SelectedDomain);
                        _settings?.Set(savedKeyNone, path);
                        ImportStatus = $"[{SelectedDomainLabel}] 임포트 완료 — 노드 {stats.Nodes}, 엣지 {stats.Edges}";
                        Reload();
                        MessageBox.Show(
                            $"{SelectedDomainLabel} 지식그래프 임포트 완료.\n\n" +
                            $"파일: {path}\n\n" +
                            $"노드: {stats.Nodes}개\n엣지: {stats.Edges}개\n\n" +
                            "  ▸ " + string.Join("\n  ▸ ", stats.NodesByType.Select(kv => $"{kv.Key}: {kv.Value}")),
                            "KG Import",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"임포트 실패: {ex.Message}", "오류",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }

                // ───────────────────────────────────────────────────
                //  cs_file / python_engine_folder 분기 (v2.7.21 그대로 유지)
                // ───────────────────────────────────────────────────

                // ★ v2.7.21: 표준 빌드 출력 경로 = 도메인 코드 기반 (OutputBasename 무시)
                //   빌드 (KgBuilderRunner.RunAsync 의 explicitDomain) 도 같은 규칙으로 산출 →
                //   빌드와 임포트가 항상 같은 1개 파일을 가리킴.
                //   사용자가 OutputBasename 에 어떤 값을 입력해도 영향 없음.
                string canonicalPath = Services.KgBuilderRunner.OutputJsonPath(SelectedDomain);

                // 1) settings 의 마지막 임포트 경로 — canonical 과 같고 파일이 존재하면 사용
                var savedKey   = Services.KgSettingsService.KeyForLastImport(SelectedDomain);
                var savedValue = _settings?.Get(savedKey);

                bool savedIsCanonical = !string.IsNullOrEmpty(savedValue)
                                        && string.Equals(savedValue, canonicalPath,
                                            System.StringComparison.OrdinalIgnoreCase);

                if (savedIsCanonical && System.IO.File.Exists(savedValue))
                {
                    path = savedValue;
                }
                // 2) settings 가 어긋나거나 없으면 canonical 직접 검사
                else if (!string.IsNullOrEmpty(canonicalPath) && System.IO.File.Exists(canonicalPath))
                {
                    path = canonicalPath;
                    // settings 도 canonical 로 동기화 (옛 값 덮어쓰기)
                    _settings?.Set(savedKey, canonicalPath);
                }

                // 3) 못 찾음 — cs_file / python_engine_folder 안내 (다이얼로그 안 띄움)
                if (path == null)
                {
                    // ★ v2.7.21: canonical 경로 1개만 표시 (settings stale 노출 안 함)
                    MessageBox.Show(
                        $"[{SelectedDomainLabel}] 임포트할 KG 파일을 찾을 수 없습니다.\n\n" +
                        $"기대 위치:\n  {canonicalPath}\n\n" +
                        "[KG 빌드] 버튼을 먼저 실행하여 파일을 생성하거나,\n" +
                        "도메인을 다시 등록해 임포트 파일을 지정하세요.",
                        "KG 임포트 — 파일 없음",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 임포트 실행
                try
                {
                    var stats = _kg.ImportFromFile(path, SelectedDomain);
                    _settings?.Set(savedKey, path);
                    ImportStatus = $"[{SelectedDomainLabel}] 임포트 완료 — 노드 {stats.Nodes}, 엣지 {stats.Edges}";
                    Reload();
                    MessageBox.Show(
                        $"{SelectedDomainLabel} 지식그래프 임포트 완료.\n\n" +
                        $"파일: {path}\n\n" +
                        $"노드: {stats.Nodes}개\n엣지: {stats.Edges}개\n\n" +
                        "  ▸ " + string.Join("\n  ▸ ", stats.NodesByType.Select(kv => $"{kv.Key}: {kv.Value}")),
                        "KG Import",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"임포트 실패: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            // ════════════════════════════════════════════
            //  Case B: 빌트인 5종 — 기존 로직 유지
            // ════════════════════════════════════════════
            var jsonPath = KgBuilderRunner.LocateJson(SelectedDomain);
            if (jsonPath == null)
            {
                var expectedPath = KgBuilderRunner.OutputJsonPath(SelectedDomain);
                MessageBox.Show(
                    "임포트할 JSON 파일을 찾을 수 없습니다.\n\n" +
                    $"기대 위치:\n  {expectedPath}\n\n" +
                    "[KG 빌드] 버튼을 먼저 실행하여 파일을 생성해 주세요.",
                    "KG 임포트 — 파일 없음",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var stats = _kg.ImportFromJson(jsonPath, SelectedDomain);
                _settings?.Set(Services.KgSettingsService.KeyForLastImport(SelectedDomain), jsonPath);
                ImportStatus = $"[{SelectedDomainLabel}] 임포트 완료 — 노드 {stats.Nodes}, 엣지 {stats.Edges}";
                Reload();
                MessageBox.Show(
                    $"{SelectedDomainLabel} 지식그래프 임포트 완료.\n\n" +
                    $"파일: {jsonPath}\n\n" +
                    $"노드: {stats.Nodes}개\n엣지: {stats.Edges}개\n\n" +
                    "  ▸ " + string.Join("\n  ▸ ", stats.NodesByType.Select(kv => $"{kv.Key}: {kv.Value}")),
                    "KG Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"임포트 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── v3.0 F-001: KG 탭 자산 링크 link_type 선택 ─────────────
        // 사용자가 KG 노드 선택 후 [현재 자산에 링크] 버튼을 누를 때 적용되는 관계 종류.
        // MainWindow.xaml 의 link_type 콤보박스가 이 속성에 양방향 바인딩.
        public ObservableCollection<string> LinkTypes { get; } = new()
            { "implements", "uses", "derived_from", "cites", "validates" };

        private string _selectedLinkType = "implements";
        public string SelectedLinkType
        {
            get => _selectedLinkType;
            set => Set(ref _selectedLinkType, value);
        }

        /// <summary>선택된 KG 노드를, 자산 탭에서 선택해 둔 자산에 링크.
        /// 사용자가 KG 탭으로 이동한 시점에 SelectedTab은 KG 탭이므로,
        /// 탭 인덱스가 아니라 5종 SelectedXxx 자체를 검사한다.</summary>
        private void LinkSelectedAsset()
        {
            if (_selectedNode == null)
            {
                MessageBox.Show("먼저 KG 노드를 선택하세요.",
                    "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // v3.0 F-001 hotfix: 사용자가 KG 탭에서 버튼을 누를 때 SelectedTab은 항상 6(KG).
            // 따라서 SelectedTab 으로 자산 종류를 결정하면 안 되며, 5종 SelectedXxx 를 직접 본다.
            // 둘 이상이 동시에 선택돼 있으면 명확화 다이얼로그를 표시한다.
            string? at = null; int aid = 0; string name = "";
            int selectedCount = 0;
            string? lastTabHint = null;

            if (_main.SelectedCode is { } c)
            {
                at = "asset_code";       aid = c.Id; name = c.Name;
                selectedCount++; lastTabHint = "소스코드";
            }
            if (_main.SelectedModel is { } m)
            {
                at = "asset_model";      aid = m.Id; name = m.Name;
                selectedCount++; lastTabHint = "AI 모델";
            }
            if (_main.SelectedDocument is { } d)
            {
                at = "asset_document";   aid = d.Id; name = d.Title;
                selectedCount++; lastTabHint = "문서·논문";
            }
            if (_main.SelectedPatent is { } p)
            {
                at = "asset_patent";     aid = p.Id; name = p.Title;
                selectedCount++; lastTabHint = "특허·IP";
            }
            if (_main.SelectedExperiment is { } e)
            {
                at = "asset_experiment"; aid = e.Id; name = e.Name;
                selectedCount++; lastTabHint = "실험결과";
            }

            if (at == null)
            {
                MessageBox.Show(
                    "먼저 자산 탭(소스코드/AI 모델/문서·논문/특허·IP/실험결과)에서 " +
                    "하나의 자산을 선택한 뒤, 이 KG 탭으로 돌아와 노드를 선택하고 [현재 자산에 링크] 를 눌러주세요.",
                    "자산 선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selectedCount > 1)
            {
                var r = MessageBox.Show(
                    $"여러 자산 탭에서 자산이 선택되어 있습니다.\n" +
                    $"가장 마지막에 선택된 [{lastTabHint}] 의 \"{name}\" 와(과) 링크를 진행할까요?\n\n" +
                    "원하는 자산이 아니면 [아니오] 후 자산 탭에서 다시 정확한 자산 1건만 선택해 주세요.",
                    "자산 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            try
            {
                _kg.LinkAsset(new AssetKgLink
                {
                    AssetType = at, AssetId = aid,
                    KgNodeId = _selectedNode.Id, LinkType = _selectedLinkType,
                    Note = $"linked from KG tab ({DateTime.Now:yyyy-MM-dd HH:mm})",
                });
                // v3.0 F-001 Step 1.7: 우측 "연결된 자산" 패널 + 그래프 강조 갱신
                LoadLinkedAssetsForSelected();
                OnPropertyChanged(nameof(SelectedNode));   // KgGraphView 의 PropertyChanged 핸들러 트리거 → 재렌더

                MessageBox.Show(
                    $"링크 추가 완료\n\n자산: {name}\n관계: {_selectedLinkType}\nKG 노드: [{_selectedNode.Type}] {_selectedNode.Label}",
                    "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"링크 추가 실패: {ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string PrettyJson(string raw)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(raw);
                return System.Text.Json.JsonSerializer.Serialize(d,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch { return raw; }
        }
    }

    public class KgStatItem
    {
        public string Key { get; set; } = "";
        public int Count { get; set; }
    }
}
