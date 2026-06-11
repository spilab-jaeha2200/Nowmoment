// ════════════════════════════════════════════════════════════
// AssetKgLinkPanelViewModel.cs   (v4 Phase 6 — 화면설계서 §4.3 적용)
//
// v3.0 F-001 Step 1.5 의 기존 동작을 유지하면서, 라이트 테마 편집
// 다이얼로그가 요구하는 표 형태(컬럼: KG 노드 ID / 라벨 / 도메인 /
// 링크 타입 / X) 와 헤더 우측의 도메인 필터·노드 검색을 지원한다.
//
// 추가/변경:
//   • AssetKgLinkRow.NodeDomain  — NodeId prefix 에서 파생한 도메인
//   • Domains                    — 헤더 우측 ComboBox "전체/cs/photo/..."
//   • SelectedDomain             — 현재 필터 도메인 (변경 시 후보 재조회)
//   • OpenNodePickerCommand      — 헤더 우측 [🔍 노드] 버튼용 (검색 키워드 비우기 + 후보 재조회)
//   • 기존 AddLinkCommand / DeleteLinkCommand 시그니처는 그대로
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>EditDialog의 DataGrid 한 행을 표시하기 위한 결합 데이터 클래스.</summary>
    public class AssetKgLinkRow
    {
        public int      LinkId     { get; set; }
        public string   NodeId     { get; set; } = "";
        public string   NodeType   { get; set; } = "";
        public string   NodeLabel  { get; set; } = "";
        public string   NodeDomain { get; set; } = "";   // v4 Phase 6: 신설
        public string   LinkType   { get; set; } = "";
        public string   Note       { get; set; } = "";
        public DateTime CreatedAt  { get; set; }

        // (구) 그리드 한 줄 요약 — 호환을 위해 유지
        public string Display => $"[{NodeType}] {NodeLabel}";
    }

    public class AssetKgLinkPanelViewModel : BaseViewModel
    {
        private readonly KnowledgeGraphService? _kg;
        private readonly string _assetType;
        private int _assetId;

        public ObservableCollection<AssetKgLinkRow> Links { get; } = new();
        public ObservableCollection<string> LinkTypes { get; } = new()
            { "implements", "uses", "derived_from", "cites", "validates" };

        // ── v4 Phase 6: 도메인 필터 ───────────────────────────
        /// <summary>드롭다운 첫 항목 "전체" 의 표시값. 빈 문자열과 동치 처리.</summary>
        public const string DomainAll = "전체";

        public ObservableCollection<string> Domains { get; } = new() { DomainAll };

        private string _selectedDomain = DomainAll;
        public string SelectedDomain
        {
            get => _selectedDomain;
            set { if (Set(ref _selectedDomain, value)) ReloadKgNodes(); }
        }

        // ── 추가 폼 입력값 ─────────────────────────────────────
        private KgNode? _selectedKgNode;
        public KgNode? SelectedKgNode
        {
            get => _selectedKgNode;
            set => Set(ref _selectedKgNode, value);
        }

        private string _addLinkType = "implements";
        public string AddLinkType
        {
            get => _addLinkType;
            set => Set(ref _addLinkType, value);
        }

        // ── KG 노드 검색 ───────────────────────────────────────
        private string _kgSearchKeyword = "";
        public string KgSearchKeyword
        {
            get => _kgSearchKeyword;
            set { if (Set(ref _kgSearchKeyword, value)) ReloadKgNodes(); }
        }

        public ObservableCollection<KgNode> KgNodeCandidates { get; } = new();

        // ── 가용성 (미저장 자산일 때 표 비활성화) ────────────────
        public bool IsAvailable => _assetId > 0 && _kg != null;
        public string UnavailableMessage
        {
            get
            {
                if (_kg == null)
                    return "KG 서비스가 초기화되지 않았습니다.\n"
                         + "앱을 재시작하거나, 시작 시 KG 모듈 로드 실패 여부(crash.log)를 확인하세요.";
                if (_assetId <= 0)
                    return "자산이 아직 저장되지 않았습니다.\n"
                         + "먼저 자산을 [저장] 한 뒤 다시 열어 KG 노드를 연결하세요.";
                return "KG 노드를 연결할 수 없는 상태입니다.";
            }
        }

        public ICommand AddLinkCommand        { get; }
        public ICommand DeleteLinkCommand     { get; }
        public ICommand OpenNodePickerCommand { get; }  // v4 Phase 6: [🔍 노드]

        public AssetKgLinkPanelViewModel(KnowledgeGraphService? kg, string assetType, int assetId)
        {
            _kg        = kg;
            _assetType = assetType;
            _assetId   = assetId;

            AddLinkCommand        = new RelayCommand(_ => AddLink(),         _ => CanAddLink());
            DeleteLinkCommand     = new RelayCommand(p => DeleteLink(p as AssetKgLinkRow));
            OpenNodePickerCommand = new RelayCommand(_ => OpenNodePicker(), _ => IsAvailable);

            ReloadDomains();
            if (IsAvailable)
            {
                ReloadLinks();
                ReloadKgNodes();
            }
        }

        /// <summary>자산이 INSERT 직후, 외부에서 신규 자산 ID 를 알려주면 패널 활성화.</summary>
        public void AttachAssetId(int newAssetId)
        {
            if (_assetId != 0 || newAssetId <= 0) return;
            _assetId = newAssetId;
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(UnavailableMessage));
            if (IsAvailable)
            {
                ReloadLinks();
                ReloadKgNodes();
            }
        }

        /// <summary>
        /// 외부(다른 화면/다이얼로그)에서 링크가 변경됐을 수 있을 때 호출하는 갱신 진입점.
        /// DB 를 다시 읽어 링크 목록·노드 후보를 최신화한다.
        /// </summary>
        public void Refresh()
        {
            if (!IsAvailable) return;
            ReloadLinks();
            ReloadKgNodes();
        }

        // ──────────────────────────────────────────────────────
        // 도메인 목록 채우기: 노드 ID prefix 의 distinct 집합을 사용
        //  - kg_domain 테이블이 비어 있어도 안전하게 동작
        //  - "전체" + 정렬된 도메인 코드
        // ──────────────────────────────────────────────────────
        private void ReloadDomains()
        {
            Domains.Clear();
            Domains.Add(DomainAll);
            if (_kg == null) return;

            try
            {
                var prefixes = _kg.GetNodes()
                    .Select(n => KgNodeDomainExtensions.DomainOf(n.Id))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .OrderBy(d => d);
                foreach (var d in prefixes) Domains.Add(d);
            }
            catch
            {
                // 서비스 미초기화 시 무시 (전체만 노출)
            }
        }

        private void ReloadLinks()
        {
            Links.Clear();
            if (_kg == null || _assetId <= 0) return;

            var raws = _kg.GetLinksForAsset(_assetType, _assetId);
            foreach (var l in raws)
            {
                var node = _kg.GetNodeById(l.KgNodeId);
                Links.Add(new AssetKgLinkRow
                {
                    LinkId     = l.Id,
                    NodeId     = l.KgNodeId,
                    NodeType   = node?.Type  ?? "(deleted)",
                    NodeLabel  = node?.Label ?? l.KgNodeId,
                    NodeDomain = KgNodeDomainExtensions.DomainOf(l.KgNodeId),
                    LinkType   = l.LinkType,
                    Note       = l.Note,
                    CreatedAt  = l.CreatedAt,
                });
            }
        }

        private void ReloadKgNodes()
        {
            KgNodeCandidates.Clear();
            if (_kg == null) return;

            var domainFilter = (_selectedDomain == DomainAll) ? "" : _selectedDomain;
            var all = _kg.GetNodes(typeFilter: "", keyword: _kgSearchKeyword, domain: domainFilter);
            foreach (var n in all.Take(100))
                KgNodeCandidates.Add(n);
        }

        private void OpenNodePicker()
        {
            // [🔍 노드] 버튼: 검색어를 비워 전체 목록을 다시 보여줌
            // (별도 모달 검색창을 띄우지 않고, 헤더 검색 UX 를 단순화)
            KgSearchKeyword = "";
            ReloadKgNodes();
        }

        private bool CanAddLink() => IsAvailable && _selectedKgNode != null;

        private void AddLink()
        {
            if (_kg == null || _selectedKgNode == null || _assetId <= 0) return;

            try
            {
                _kg.LinkAsset(new AssetKgLink
                {
                    AssetType = _assetType, AssetId = _assetId,
                    KgNodeId  = _selectedKgNode.Id, LinkType = _addLinkType,
                    Note = $"linked from edit dialog ({DateTime.Now:yyyy-MM-dd HH:mm})",
                });
                ReloadLinks();
                SelectedKgNode = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"링크 추가 실패: {ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteLink(AssetKgLinkRow? row)
        {
            if (_kg == null || row == null) return;

            var r = MessageBox.Show(
                $"다음 링크를 삭제합니까?\n\n  관계: {row.LinkType}\n  KG 노드: [{row.NodeType}] {row.NodeLabel}",
                "링크 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                _kg.UnlinkAsset(row.LinkId);
                ReloadLinks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"링크 삭제 실패: {ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
