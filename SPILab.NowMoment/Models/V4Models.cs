using System;

namespace SPILab.NowMoment.Models
{
    // ── v4: 이력/변경 로그 (audit_log) ─────────────────
    public class AuditLog
    {
        public long   Id        { get; set; }
        public string Ts        { get; set; } = "";
        public string Actor     { get; set; } = "local";
        public string Action    { get; set; } = ""; // create / update / delete / backup / import / core.*
        public string AssetType { get; set; } = "";
        public long?  AssetId   { get; set; }
        public string DiffJson  { get; set; } = "{}";

        // v4.1 — Core 접근 이벤트 전용 (계획서 5.3). 자산 CRUD 행에서는 빈 문자열.
        public string ActorRole   { get; set; } = ""; // CoreOwner / CoreDeveloper / CoreRunner / ShellOnly
        public string CoreSession { get; set; } = ""; // Secure-Verify 세션 ID

        public DateTime TimeStamp => DateTime.TryParse(Ts, out var t) ? t : DateTime.MinValue;

        /// <summary>Core 접근 이벤트인지 (action 이 'core.' 로 시작).</summary>
        public bool IsCoreEvent => Action.StartsWith("core.", StringComparison.Ordinal);
    }

    // ── v4: 태그 정규화 ──────────────────────────────
    public class Tag
    {
        public int    Id    { get; set; }
        public string Name  { get; set; } = "";
        public string Color { get; set; } = ""; // #RRGGBB (optional)
        public int    UseCount { get; set; }    // 조회 시 채워짐
    }

    // ── v4: TTL Studio 영속화 (단일행 KV) ─────────────
    public class TtlOntologyRecord
    {
        public string BaseUri     { get; set; } = "http://spilab.ai/onto#";
        public string BasePrefix  { get; set; } = "spi";
        public string JsonPayload { get; set; } = "{}";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    // ── v4: 사용자 설정 KV ───────────────────────────
    public class UserSetting
    {
        public string Group { get; set; } = "general";
        public string Key   { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
