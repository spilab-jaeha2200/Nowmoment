// ════════════════════════════════════════════════════════════════════
// ViewModels/AssetKgLinkViewerViewModel.cs — v4 자산↔KG 링크 종합 화면 VM
//
// 좌측: 자산 5종 (Code/Model/Document/Patent/Experiment) 트리 선택.
// 우측: 선택된 자산의 링크를 관리하는 v3 AssetKgLinkPanelViewModel 을 동적 생성하여
//       AssetKgLinkPanelView 에 주입.
//
// v3 자산:
//   • MainViewModel.Codes/Models/Documents/Patents/Experiments — 자산 5종 컬렉션
//   • AssetKgLinkPanelViewModel(kg, assetType, assetId) — 단일 자산의 링크 패널
//   • KnowledgeGraphService.GetLinksForAsset / LinkAsset / UnlinkAsset
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    /// <summary>좌측 자산 트리의 한 행 (유형 + ID + 이름).</summary>
    public class AssetTreeRow
    {
        public string AssetType  { get; init; } = "";  // "asset_code" 등
        public string TypeLabel  { get; init; } = "";  // "코드"
        public string Icon       { get; init; } = "";
        public int    AssetId    { get; init; }
        public string AssetName  { get; init; } = "";
        public string Display    => string.IsNullOrEmpty(AssetName)
            ? $"#{AssetId}"
            : $"#{AssetId} · {AssetName}";
    }

    public class AssetKgLinkViewerViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;
        private readonly KnowledgeGraphService? _kgService;

        public AssetKgLinkViewerViewModel(MainViewModel main, KnowledgeGraphService? kgService)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));
            _kgService = kgService;

            AssetTypes = new ObservableCollection<string>
            {
                "📄 코드 / 모듈",
                "🤖 AI 모델 / 데이터",
                "📜 문서 / 논문",
                "📑 특허 / IP",
                "🔬 실험 / 측정",
            };
            AssetRows = new ObservableCollection<AssetTreeRow>();

            // 자산 컬렉션 변동 구독 → 좌측 트리 자동 갱신
            _main.Codes.CollectionChanged       += (_, __) => RebuildAssetRows();
            _main.Models.CollectionChanged      += (_, __) => RebuildAssetRows();
            _main.Documents.CollectionChanged   += (_, __) => RebuildAssetRows();
            _main.Patents.CollectionChanged     += (_, __) => RebuildAssetRows();
            _main.Experiments.CollectionChanged += (_, __) => RebuildAssetRows();

            RebuildAssetRows();
        }

        // ── 좌측: 자산 유형 + 자산 목록 ──
        public ObservableCollection<string>        AssetTypes { get; }
        public ObservableCollection<AssetTreeRow>  AssetRows  { get; }

        private string _selectedAssetType = "📄 코드 / 모듈";
        public string SelectedAssetType
        {
            get => _selectedAssetType;
            set
            {
                if (_selectedAssetType != value)
                {
                    _selectedAssetType = value;
                    OnPropertyChanged();
                    RebuildAssetRows();
                }
            }
        }

        private AssetTreeRow? _selectedAsset;
        public AssetTreeRow? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (_selectedAsset == value) return;
                _selectedAsset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionHeader));
                // 우측 패널 갱신 — v3 AssetKgLinkPanelViewModel 새 인스턴스 생성
                RebuildLinkPanel();
            }
        }

        public bool HasSelection => SelectedAsset != null;

        public string SelectionHeader
            => SelectedAsset == null
                ? "(좌측에서 자산을 선택하세요)"
                : $"{SelectedAsset.Icon}  {SelectedAsset.TypeLabel}  ·  {SelectedAsset.Display}";

        // ── 우측: v3 AssetKgLinkPanelViewModel 인스턴스 ──
        private AssetKgLinkPanelViewModel? _linkPanelVm;
        public AssetKgLinkPanelViewModel? LinkPanelVm
        {
            get => _linkPanelVm;
            private set
            {
                _linkPanelVm = value;
                OnPropertyChanged();
            }
        }

        // ── 내부 로직 ──

        /// <summary>현재 선택된 자산 유형에 따라 좌측 행 컬렉션 재구성.</summary>
        private void RebuildAssetRows()
        {
            AssetRows.Clear();
            if (_selectedAssetType.Contains("코드"))
            {
                foreach (var x in _main.Codes)
                    AssetRows.Add(new AssetTreeRow {
                        AssetType = "asset_code", TypeLabel = "코드", Icon = "📄",
                        AssetId = x.Id, AssetName = x.Name });
            }
            else if (_selectedAssetType.Contains("AI 모델"))
            {
                foreach (var x in _main.Models)
                    AssetRows.Add(new AssetTreeRow {
                        AssetType = "asset_model", TypeLabel = "모델", Icon = "🤖",
                        AssetId = x.Id, AssetName = x.Name });
            }
            else if (_selectedAssetType.Contains("문서") || _selectedAssetType.Contains("논문"))
            {
                foreach (var x in _main.Documents)
                    AssetRows.Add(new AssetTreeRow {
                        AssetType = "asset_document", TypeLabel = "문서", Icon = "📜",
                        AssetId = x.Id, AssetName = x.Title });
            }
            else if (_selectedAssetType.Contains("특허"))
            {
                foreach (var x in _main.Patents)
                    AssetRows.Add(new AssetTreeRow {
                        AssetType = "asset_patent", TypeLabel = "특허", Icon = "📑",
                        AssetId = x.Id, AssetName = x.Title });
            }
            else if (_selectedAssetType.Contains("실험") || _selectedAssetType.Contains("측정"))
            {
                foreach (var x in _main.Experiments)
                    AssetRows.Add(new AssetTreeRow {
                        AssetType = "asset_experiment", TypeLabel = "실험", Icon = "🔬",
                        AssetId = x.Id, AssetName = x.Name });
            }
            // 선택은 클리어 (유형 바뀜)
            SelectedAsset = null;
            OnPropertyChanged(nameof(AssetCountLabel));
        }

        public string AssetCountLabel => $"총 {AssetRows.Count}개";

        /// <summary>선택 자산이 바뀔 때 v3 AssetKgLinkPanelViewModel 인스턴스를 새로 생성.</summary>
        private void RebuildLinkPanel()
        {
            if (SelectedAsset == null)
            {
                LinkPanelVm = null;
                return;
            }
            LinkPanelVm = new AssetKgLinkPanelViewModel(
                _kgService,
                SelectedAsset.AssetType,
                SelectedAsset.AssetId);
        }

        /// <summary>
        /// KG 링크 화면이 다시 보일 때 호출 — 자산 편집 다이얼로그 등에서
        /// 링크가 추가/삭제됐을 수 있으므로 우측 링크 패널을 DB 기준으로 갱신한다.
        /// (화면 캐시 때문에 SelectedAsset 이 그대로라 RebuildLinkPanel 이
        ///  자동 호출되지 않던 문제를 보완)
        /// </summary>
        public void Refresh()
        {
            // 우측 링크 패널이 이미 있으면 그 자리에서 DB 재조회.
            // (RebuildLinkPanel 로 새 인스턴스를 만들면 도메인 필터/검색어 등
            //  사용자가 설정해 둔 상태가 초기화되므로, 기존 패널의 Refresh 를 호출)
            LinkPanelVm?.Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
