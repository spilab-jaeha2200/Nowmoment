using System;
using System.Collections.Generic;

namespace SPILab.NowMoment.Models
{
    // ── 프로젝트 ─────────────────────────────────────────
    public class Project
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Client { get; set; } = "";
        public string Type { get; set; } = "internal";   // govt / commercial / internal
        public string Status { get; set; } = "active";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public override string ToString() => $"[{Id}] {Name}";
    }

    // ── 소스코드 / GitHub ────────────────────────────────
    public class AssetCode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string RepoUrl { get; set; } = "";
        public string Language { get; set; } = "Python";
        public string Version { get; set; } = "1.0.0";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";   // JOIN용
        public string Tags { get; set; } = "";          // 콤마 구분
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── AI 모델 가중치 ───────────────────────────────────
    public class AssetModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Framework { get; set; } = "PyTorch";  // PyTorch / sklearn / TF
        public double? Accuracy { get; set; }
        public string FilePath { get; set; } = "";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string BaseModel { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── 논문 / 제안서 / 보고서 ──────────────────────────
    public class AssetDocument
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string DocType { get; set; } = "document"; // paper/proposal/report/manual
        public string FilePath { get; set; } = "";
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public string Summary { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── 특허 / IP ────────────────────────────────────────
    public class AssetPatent
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ApplicationNo { get; set; } = "";
        public string Status { get; set; } = "applied";  // applied/registered/rejected/pending
        public DateTime? FilingDate { get; set; }
        public string Inventors { get; set; } = "";      // 콤마 구분
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── 실험 결과 / 데이터셋 ────────────────────────────
    public class AssetExperiment
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string AssetRef { get; set; } = "";
        public string Params { get; set; } = "{}";    // JSON string
        public string Metrics { get; set; } = "{}";   // JSON string
        public string ResultPath { get; set; } = "";
        public string Status { get; set; } = "completed";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── 검색 결과 통합 DTO ───────────────────────────────
    public class SearchResult
    {
        public int Id { get; set; }
        public string AssetType { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
