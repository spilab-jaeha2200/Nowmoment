using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    /// <summary>
    /// v4 신설. 자산 객체의 변경분을 diff JSON 으로 만들어 audit_log 에 적재.
    /// 호출 예시:
    ///   _audit.LogCreate("asset_code", code.Id, code);
    ///   _audit.LogUpdate("asset_code", code.Id, oldCode, newCode);
    ///   _audit.LogDelete("asset_code", code.Id, code);
    /// </summary>
    public partial class AuditService
    {
        private readonly DatabaseService _db;
        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public AuditService(DatabaseService db) { _db = db; }

        public void LogCreate(string assetType, long assetId, object snapshot, string actor = "local")
            => _db.WriteAudit("create", assetType, assetId, Serialize(new { @new = snapshot }), actor);

        public void LogDelete(string assetType, long assetId, object? snapshot = null, string actor = "local")
            => _db.WriteAudit("delete", assetType, assetId,
                snapshot == null ? "{}" : Serialize(new { old = snapshot }), actor);

        public void LogUpdate(string assetType, long assetId, object before, object after, string actor = "local")
        {
            var diff = Diff(before, after);
            if (diff.Count == 0) return;       // 실제 변경 없으면 적재 생략
            _db.WriteAudit("update", assetType, assetId, Serialize(diff), actor);
        }

        public void LogAction(string action, string assetType, long? assetId, object? payload = null, string actor = "local")
            => _db.WriteAudit(action, assetType, assetId,
                payload == null ? "{}" : Serialize(payload), actor);

        // ── 직렬화 / Diff 계산 ─────────────────────────
        private static string Serialize(object o)
        {
            try { return JsonSerializer.Serialize(o, JsonOpt); }
            catch { return "{}"; }
        }

        /// <summary>두 객체의 public 프로퍼티를 비교해 변경된 것만 { name: { from, to } } 형식으로 반환.</summary>
        private static Dictionary<string, object> Diff(object a, object b)
        {
            var result = new Dictionary<string, object>();
            if (a == null || b == null || a.GetType() != b.GetType()) return result;
            var props = a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimple(p.PropertyType));
            foreach (var p in props)
            {
                if (p.Name == "CreatedAt" || p.Name == "UpdatedAt") continue;
                var va = p.GetValue(a);
                var vb = p.GetValue(b);
                if (!Equals(va, vb))
                    result[p.Name] = new { from = va, to = vb };
            }
            return result;
        }

        private static bool IsSimple(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t.IsPrimitive || t.IsEnum
                || t == typeof(string) || t == typeof(decimal)
                || t == typeof(DateTime) || t == typeof(DateTimeOffset)
                || t == typeof(Guid) || t == typeof(TimeSpan);
        }
    }
}
