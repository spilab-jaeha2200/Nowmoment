using System;
using System.Collections.Generic;

namespace SPILab.NowMoment.Models
{
    // ════════════════════════════════════════════════════════════
    // 지식그래프(KG) 데이터 모델 — 레이판 Sim CS Edition 임베딩용
    // 이 파일을 Models/ 디렉토리에 추가하세요.
    // ════════════════════════════════════════════════════════════

    /// <summary>KG 노드 — Workspace / PhysicsRule / Material / Parameter / Spec</summary>
    public class KgNode
    {
        public string Id      { get; set; } = "";   // 예: "rule:R6"
        public string Type    { get; set; } = "";   // PhysicsRule / Material / Workspace / Parameter / Spec
        public string Label   { get; set; } = "";
        public string PropsJson { get; set; } = "{}";   // 원본 props (JSON 문자열로 보관)

        public override string ToString() => $"[{Type}] {Label}";
    }

    /// <summary>KG 엣지 — USES / GOVERNS / DERIVES_FROM / CITES / BELONGS_TO / REQUIRES</summary>
    public class KgEdge
    {
        public int Id       { get; set; }
        public string SrcId { get; set; } = "";
        public string DstId { get; set; } = "";
        public string Rel   { get; set; } = "";
        public string PropsJson { get; set; } = "{}";

        // JOIN 결과 표시용
        public string SrcLabel { get; set; } = "";
        public string DstLabel { get; set; } = "";
        public string SrcType  { get; set; } = "";
        public string DstType  { get; set; } = "";
    }

    /// <summary>자산 ↔ KG 노드 양방향 링크 (예: "레이판Sim CS Edition" 코드 자산이 R6 룰을 구현)</summary>
    public class AssetKgLink
    {
        public int    Id        { get; set; }
        public string AssetType { get; set; } = "";   // asset_code / asset_model / asset_document / asset_patent / asset_experiment
        public int    AssetId   { get; set; }
        public string KgNodeId  { get; set; } = "";
        public string LinkType  { get; set; } = "implements"; // implements / cites / validates / generates
        public string Note      { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>v3.0 F-001 Step 1.7: KG 노드 선택 시 우측 패널에 표시할 "연결된 자산" 행.</summary>
    public class LinkedAssetRow
    {
        public int      LinkId    { get; set; }    // asset_kg_link.id
        public string   AssetType { get; set; } = "";  // asset_code / model / document / patent / experiment
        public int      AssetId   { get; set; }
        public string   AssetName { get; set; } = "";  // COALESCE(name, title)
        public string   LinkType  { get; set; } = "";
        public string   Note      { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        // 자산 종류를 한글 라벨로
        public string AssetTypeLabel => AssetType switch
        {
            "asset_code"       => "소스코드",
            "asset_model"      => "AI 모델",
            "asset_document"   => "문서",
            "asset_patent"     => "특허",
            "asset_experiment" => "실험",
            _                  => AssetType,
        };
    }

    /// <summary>KG 통계 (대시보드용)</summary>
    public class KgStats
    {
        public int    Nodes { get; set; }
        public int    Edges { get; set; }
        public Dictionary<string, int> NodesByType { get; set; } = new();
        public Dictionary<string, int> EdgesByRel  { get; set; } = new();
        public DateTime ImportedAt { get; set; }
        public string  SourceFile  { get; set; } = "";
    }

    /// <summary>그래프 캔버스 렌더링용 경량 노드/엣지</summary>
    public class GraphViewNode
    {
        public string Id    { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type  { get; set; } = "";
        public double X     { get; set; }
        public double Y     { get; set; }
        public string Color { get; set; } = "#5BA3D9";
    }

    public class GraphViewEdge
    {
        public string SrcId { get; set; } = "";
        public string DstId { get; set; } = "";
        public string Rel   { get; set; } = "";
    }
}
