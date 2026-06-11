// ════════════════════════════════════════════════════════════════════
// KgViewModel.Manage.cs (v2.7)
//
// 1) 노드/엣지 CRUD 명령
// 2) 동적 도메인 목록 + 추가/삭제 명령
// 3) TTL/JSON 통합 임포트 명령
//
// 사용 조건:
//   * 기존 KgViewModel.cs / KgViewModel.Builder.cs 에 이미 partial 선언이 되어 있음.
//   * 본 파일을 ViewModels/ 에 추가.
//   * MainWindow.xaml.cs 에서 KgDomainService 를 생성해 AttachDomains 로 부착:
//        var ds = new KgDomainService(db.DbPath);
//        main.Kg!.AttachDomains(ds);
//   * MainWindow.xaml 의 도메인 콤보박스를 동적 바인딩으로 교체 (가이드 참고).
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;
using SPILab.NowMoment.Views;

namespace SPILab.NowMoment.ViewModels
{
    public partial class KgViewModel
    {
        // ── 도메인 동적 목록 ─────────────────────────────
        private KgDomainService? _domainSvc;

        /// <summary>kg_domain 테이블에서 읽어온 모든 도메인. 콤보박스 바인딩용.</summary>
        public ObservableCollection<KgDomain> Domains { get; } = new();

        public void AttachDomains(KgDomainService svc)
        {
            _domainSvc = svc;
            ReloadDomains();

            // 시드된 cs 도메인이 항상 존재하므로 SelectedDomain 의 기본값 'cs' 가 유효함.
            // 단, 외부에서 SelectedDomain 을 사용자 도메인으로 미리 세팅한 경우는 그대로 유지.
        }

        public void ReloadDomains()
        {
            if (_domainSvc == null) return;
            Domains.Clear();
            foreach (var d in _domainSvc.GetAll()) Domains.Add(d);
        }

        // ── 도메인 추가/삭제 명령 ────────────────────────
        private RelayCommand? _addDomainCmd;
        public ICommand AddDomainCommand =>
            _addDomainCmd ??= new RelayCommand(_ => DoAddDomain());

        private RelayCommand? _delDomainCmd;
        public ICommand DeleteDomainCommand =>
            _delDomainCmd ??= new RelayCommand(_ => DoDeleteDomain(),
                _ => CanDeleteCurrentDomain());

        private bool CanDeleteCurrentDomain()
        {
            if (_domainSvc == null) return false;
            var d = _domainSvc.Get(SelectedDomain);
            return d != null && !d.IsBuiltIn;
        }

        private void DoAddDomain()
        {
            if (_domainSvc == null) return;
            var dlg = new KgDomainEditDialog { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            // ── 1단계: 도메인 등록 ──────────────────────────
            try
            {
                _domainSvc.Add(dlg.Result);
                ReloadDomains();
                SelectedDomain = dlg.Result.Code;
                ImportStatus = $"새 도메인 등록 완료 — {dlg.Result.Label} ({dlg.Result.Code})";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"도메인 등록 실패:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // ── 2단계: 빌더 도메인이면 EngineSrcPath 를 settings 에 저장 ──
            if ((dlg.Result.BuilderKind == "cs_file" || dlg.Result.BuilderKind == "python_engine_folder")
                && !string.IsNullOrEmpty(dlg.EngineSrcPath))
            {
                _settings?.Set(KgSettingsService.KeyForDomainDynamic(dlg.Result.Code), dlg.EngineSrcPath);
                BuilderSrcPath = dlg.EngineSrcPath;
            }

            // ── 3단계: 자동 임포트 ──────────────────────────
            //  v2.7.9: 빌더 도메인은 다이얼로그가 ImportPath 를 빈 값으로 돌려주므로
            //  여기서 자동 종료. 사용자에게 [KG 빌드] 안내.
            if (string.IsNullOrEmpty(dlg.ImportPath))
            {
                if (dlg.Result.BuilderKind == "cs_file" || dlg.Result.BuilderKind == "python_engine_folder")
                {
                    MessageBox.Show(
                        $"[{dlg.Result.Label}] 도메인이 등록되었습니다.\n\n" +
                        $"빌더 종류: {dlg.Result.BuilderKind}\n" +
                        $"엔진 경로: {dlg.EngineSrcPath}\n\n" +
                        "지금은 노드/엣지가 비어 있습니다.\n" +
                        "[KG 빌드] 버튼을 눌러 데이터를 생성하세요.",
                        "도메인 등록 완료",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            // ── 4단계: 'none' 도메인의 자동 임포트 ──────────
            try
            {
                var stats = _kg.ImportFromFile(dlg.ImportPath, dlg.Result.Code);
                Reload();
                _settings?.Set(KgSettingsService.KeyForLastImport(dlg.Result.Code), dlg.ImportPath);

                ImportStatus = $"[{dlg.Result.Label}] 자동 임포트 완료 — 노드 {stats.Nodes}, 엣지 {stats.Edges}";
                MessageBox.Show(
                    $"도메인 등록 + 자동 임포트 완료.\n\n" +
                    $"도메인: {dlg.Result.Label} ({dlg.Result.Code})\n" +
                    $"파일: {dlg.ImportPath}\n\n" +
                    $"노드: {stats.Nodes}개\n엣지: {stats.Edges}개\n\n" +
                    "  ▸ " + string.Join("\n  ▸ ",
                        stats.NodesByType.Select(kv => $"{kv.Key}: {kv.Value}")),
                    "등록 + 임포트 성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var rollback = MessageBox.Show(
                    $"도메인은 등록되었으나 자동 임포트가 실패했습니다.\n\n" +
                    $"도메인: {dlg.Result.Code}\n" +
                    $"파일: {dlg.ImportPath}\n\n" +
                    $"오류: {ex.Message}\n\n" +
                    "방금 만든 빈 도메인을 다시 삭제하시겠습니까?",
                    "자동 임포트 실패",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (rollback == MessageBoxResult.Yes)
                {
                    try
                    {
                        _kg.ClearDomain(dlg.Result.Code);
                        _domainSvc.Delete(dlg.Result.Code);
                        ReloadDomains();
                        SelectedDomain = KnowledgeGraphService.DOMAIN_CS;
                        ImportStatus = $"도메인 롤백 완료 — {dlg.Result.Code}";
                    }
                    catch (Exception rex)
                    {
                        MessageBox.Show($"롤백 실패: {rex.Message}", "오류",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void DoDeleteDomain()
        {
            if (_domainSvc == null) return;
            var d = _domainSvc.Get(SelectedDomain);
            if (d == null) return;
            if (d.IsBuiltIn)
            {
                MessageBox.Show("빌트인 도메인은 삭제할 수 없습니다.", "거부",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 데이터 존재 여부 안내
            var stats = _kg.GetStats("", d.Code);
            var msg = stats.Nodes > 0 || stats.Edges > 0
                ? $"도메인 [{d.Label}] 을(를) 삭제하시겠습니까?\n\n" +
                  $"이 도메인의 KG 노드 {stats.Nodes}개, 엣지 {stats.Edges}개도 함께 삭제됩니다.\n" +
                  $"이 작업은 되돌릴 수 없습니다."
                : $"도메인 [{d.Label}] 을(를) 삭제하시겠습니까?";

            if (MessageBox.Show(msg, "도메인 삭제 확인",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            try
            {
                _kg.ClearDomain(d.Code);     // 노드/엣지/링크 정리
                _domainSvc.Delete(d.Code);   // 도메인 row 자체 삭제
                ReloadDomains();
                SelectedDomain = KnowledgeGraphService.DOMAIN_CS;  // 항상 존재하는 빌트인으로 전환
                ImportStatus = $"도메인 삭제 완료 — {d.Code}";
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"도메인 삭제 실패:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── 통합 임포트 (TTL/JSON 자동 분기) ─────────────
        private RelayCommand? _importFileCmd;
        public ICommand ImportFileCommand =>
            _importFileCmd ??= new RelayCommand(_ => DoImportFile());

        private void DoImportFile()
        {
            var dlg = new OpenFileDialog
            {
                Title  = $"[{SelectedDomainLabel}] KG 파일 선택 — TTL 또는 JSON",
                Filter = "KG 파일 (*.ttl;*.json;*.jsonld;*.rdf;*.nt)|*.ttl;*.json;*.jsonld;*.rdf;*.nt|" +
                         "Turtle (*.ttl)|*.ttl|" +
                         "JSON-LD (*.json;*.jsonld)|*.json;*.jsonld|" +
                         "RDF/XML (*.rdf)|*.rdf|" +
                         "N-Triples (*.nt)|*.nt|" +
                         "모든 파일|*.*",
            };
            // 빌더 출력 폴더에서 시작
            try { dlg.InitialDirectory = KgBuilderRunner.OutputDir; } catch { }

            if (dlg.ShowDialog() != true) return;

            try
            {
                var stats = _kg.ImportFromFile(dlg.FileName, SelectedDomain);
                Reload();
                ImportStatus =
                    $"[{SelectedDomainLabel}] 임포트 완료 — 노드 {stats.Nodes}, 엣지 {stats.Edges}";
                MessageBox.Show(
                    $"임포트 완료.\n\n" +
                    $"파일: {dlg.FileName}\n\n" +
                    $"노드: {stats.Nodes}개\n엣지: {stats.Edges}개\n\n" +
                    "  ▸ " + string.Join("\n  ▸ ",
                        stats.NodesByType.Select(kv => $"{kv.Key}: {kv.Value}")),
                    "KG Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"임포트 실패:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── 도메인 비우기 ────────────────────────────────
        private RelayCommand? _clearDomainCmd;
        public ICommand ClearDomainCommand =>
            _clearDomainCmd ??= new RelayCommand(_ => DoClearDomain());

        private void DoClearDomain()
        {
            var stats = _kg.GetStats("", SelectedDomain);
            if (stats.Nodes == 0 && stats.Edges == 0)
            {
                MessageBox.Show("이미 비어 있습니다.", "안내",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var ans = MessageBox.Show(
                $"[{SelectedDomainLabel}] 도메인의 KG 데이터를 모두 삭제합니다.\n\n" +
                $"노드 {stats.Nodes}개, 엣지 {stats.Edges}개가 사라집니다.\n" +
                $"도메인 자체는 보존됩니다.",
                "도메인 비우기 확인",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.OK) return;

            var (n, e) = _kg.ClearDomain(SelectedDomain);
            Reload();
            ImportStatus = $"비우기 완료 — 노드 {n}, 엣지 {e} 삭제";
        }

        // ── 노드 CRUD 명령 ───────────────────────────────
        private RelayCommand? _addNodeCmd;
        public ICommand AddNodeCommand =>
            _addNodeCmd ??= new RelayCommand(_ => DoAddNode());

        private RelayCommand? _editNodeCmd;
        public ICommand EditNodeCommand =>
            _editNodeCmd ??= new RelayCommand(_ => DoEditNode(), _ => SelectedNode != null);

        private RelayCommand? _delNodeCmd;
        public ICommand DeleteNodeCommand =>
            _delNodeCmd ??= new RelayCommand(_ => DoDeleteNode(), _ => SelectedNode != null);

        private void DoAddNode()
        {
            var dlg = new KgNodeEditDialog(null) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _kg.UpsertNode(dlg.Result, SelectedDomain);
                Reload();
                ImportStatus = $"노드 추가 — [{dlg.Result.Type}] {dlg.Result.Label}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("노드 추가 실패:\n" + ex.Message, "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoEditNode()
        {
            if (SelectedNode == null) return;
            var dlg = new KgNodeEditDialog(SelectedNode) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _kg.UpsertNode(dlg.Result, SelectedDomain);
                Reload();
                ImportStatus = $"노드 수정 — [{dlg.Result.Type}] {dlg.Result.Label}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("노드 수정 실패:\n" + ex.Message, "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoDeleteNode()
        {
            if (SelectedNode == null) return;
            var n = SelectedNode;
            var ans = MessageBox.Show(
                $"노드 [{n.Type}] {n.Label} ({n.Id}) 를 삭제합니다.\n" +
                "인접한 엣지와 자산-KG 링크도 함께 삭제됩니다.\n계속하시겠습니까?",
                "노드 삭제 확인",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.OK) return;
            _kg.DeleteNode(n.Id);
            Reload();
            ImportStatus = $"노드 삭제 — {n.Id}";
        }

        // ── 엣지 CRUD 명령 ───────────────────────────────
        private RelayCommand? _addEdgeCmd;
        public ICommand AddEdgeCommand =>
            _addEdgeCmd ??= new RelayCommand(_ => DoAddEdge(), _ => SelectedNode != null);

        private RelayCommand? _delEdgeCmd;
        public ICommand DeleteEdgeCommand =>
            _delEdgeCmd ??= new RelayCommand(p => DoDeleteEdge(p as KgEdge),
                                             p => p is KgEdge);

        private void DoAddEdge()
        {
            if (SelectedNode == null) return;
            var dlg = new KgEdgeEditDialog(SelectedNode.Id, _kg, SelectedDomain)
                      { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _kg.AddEdge(dlg.Result.SrcId, dlg.Result.DstId,
                            dlg.Result.Rel, dlg.Result.PropsJson, SelectedDomain);
                LoadEdgesForSelected();
                Reload();
                ImportStatus = $"엣지 추가 — {dlg.Result.SrcId} -[{dlg.Result.Rel}]-> {dlg.Result.DstId}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("엣지 추가 실패:\n" + ex.Message, "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoDeleteEdge(KgEdge? e)
        {
            if (e == null) return;
            var ans = MessageBox.Show(
                $"엣지 삭제:\n  {e.SrcLabel} -[{e.Rel}]-> {e.DstLabel}",
                "엣지 삭제 확인",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.OK) return;
            _kg.DeleteEdge(e.Id);
            LoadEdgesForSelected();
            Reload();
        }
    }
}
