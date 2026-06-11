// ════════════════════════════════════════════════════════════════════
// KnowledgeGraphService.Crud.cs (v2.7)
//
// 단일 노드/엣지 CRUD + 도메인 단위 일괄 삭제 — 빌더-임포트가 아닌
// "수동 편집" 경로 전용. 기존 ImportFromJson 흐름은 그대로 유지된다.
//
// 사용 조건:
//   1) 기존 KnowledgeGraphService.cs 의 클래스 선언을 partial 로 변경:
//        public class KnowledgeGraphService
//        →
//        public partial class KnowledgeGraphService
//
//   2) 본 파일을 Services/ 에 추가.
//
// 모든 메서드는 내부에서 SqliteConnection 을 새로 열고 닫는다.
// 트랜잭션이 필요 없는 단건 작업이라 멀티스레드 안전.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    public partial class KnowledgeGraphService
    {
        // ── 노드 CRUD ────────────────────────────────────

        /// <summary>id 가 같으면 UPDATE, 없으면 INSERT (UPSERT). 도메인은 보존.</summary>
        public void UpsertNode(KgNode n, string domain = DOMAIN_CS)
        {
            if (string.IsNullOrWhiteSpace(n.Id))
                throw new ArgumentException("KgNode.Id is required");
            if (string.IsNullOrWhiteSpace(n.Type))
                throw new ArgumentException("KgNode.Type is required");
            if (string.IsNullOrWhiteSpace(domain)) domain = DOMAIN_CS;

            // props_json 검증 — 비어있으면 "{}" 로 정규화, 잘못된 JSON 은 거부
            string props = string.IsNullOrWhiteSpace(n.PropsJson) ? "{}" : n.PropsJson;
            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"PropsJson is not valid JSON: {ex.Message}");
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
INSERT INTO kg_node(id,type,label,props_json,domain)
VALUES(@i,@t,@l,@p,@d)
ON CONFLICT(id) DO UPDATE SET
  type=excluded.type,
  label=excluded.label,
  props_json=excluded.props_json,
  domain=excluded.domain", conn);
            cmd.Parameters.AddWithValue("@i", n.Id);
            cmd.Parameters.AddWithValue("@t", n.Type);
            cmd.Parameters.AddWithValue("@l", n.Label ?? "");
            cmd.Parameters.AddWithValue("@p", props);
            cmd.Parameters.AddWithValue("@d", domain);
            cmd.ExecuteNonQuery();
        }

        /// <summary>props_json 만 갱신 — 그래프 뷰의 메타 편집용.</summary>
        public void UpdateNodeProps(string id, string propsJson)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id is required");
            string props = string.IsNullOrWhiteSpace(propsJson) ? "{}" : propsJson;
            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"PropsJson is not valid JSON: {ex.Message}");
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "UPDATE kg_node SET props_json=@p WHERE id=@i", conn);
            cmd.Parameters.AddWithValue("@p", props);
            cmd.Parameters.AddWithValue("@i", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>노드 1개 삭제. FK CASCADE 로 인접 엣지도 함께 사라진다.</summary>
        public int DeleteNode(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return 0;
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            // SQLite 의 ON DELETE CASCADE 는 PRAGMA foreign_keys=ON 일 때만 동작.
            // 안전하게 트랜잭션으로 엣지부터 명시적 삭제.
            using var tx = conn.BeginTransaction();
            int n;
            using (var de = new SqliteCommand(
                "DELETE FROM kg_edge WHERE src_id=@i OR dst_id=@i", conn, tx))
            {
                de.Parameters.AddWithValue("@i", id);
                de.ExecuteNonQuery();
            }
            using (var dn = new SqliteCommand("DELETE FROM kg_node WHERE id=@i", conn, tx))
            {
                dn.Parameters.AddWithValue("@i", id);
                n = dn.ExecuteNonQuery();
            }
            // 자산 ↔ KG 링크도 정리
            using (var dl = new SqliteCommand(
                "DELETE FROM asset_kg_link WHERE kg_node_id=@i", conn, tx))
            {
                dl.Parameters.AddWithValue("@i", id);
                dl.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }

        // ── 엣지 CRUD ────────────────────────────────────

        /// <summary>새 엣지 추가. AUTOINCREMENT id 반환.</summary>
        public int AddEdge(string srcId, string dstId, string rel,
                           string propsJson = "{}", string domain = DOMAIN_CS)
        {
            if (string.IsNullOrWhiteSpace(srcId) || string.IsNullOrWhiteSpace(dstId))
                throw new ArgumentException("srcId / dstId required");
            if (string.IsNullOrWhiteSpace(rel))
                throw new ArgumentException("rel required");
            if (string.IsNullOrWhiteSpace(domain)) domain = DOMAIN_CS;

            string props = string.IsNullOrWhiteSpace(propsJson) ? "{}" : propsJson;
            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"PropsJson is not valid JSON: {ex.Message}");
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            // FK 검증 — 양 끝 노드가 실제로 존재해야 함
            if (!NodeExists(conn, srcId)) throw new InvalidOperationException($"src_id not found: {srcId}");
            if (!NodeExists(conn, dstId)) throw new InvalidOperationException($"dst_id not found: {dstId}");

            using var cmd = new SqliteCommand(@"
INSERT INTO kg_edge(src_id,dst_id,rel,props_json,domain)
VALUES(@s,@dst,@r,@p,@d);
SELECT last_insert_rowid();", conn);
            cmd.Parameters.AddWithValue("@s",   srcId);
            cmd.Parameters.AddWithValue("@dst", dstId);
            cmd.Parameters.AddWithValue("@r",   rel);
            cmd.Parameters.AddWithValue("@p",   props);
            cmd.Parameters.AddWithValue("@d",   domain);
            return (int)(long)(cmd.ExecuteScalar() ?? 0L);
        }

        /// <summary>엣지의 관계명 / props 갱신. (src/dst 변경은 지원하지 않음 — 삭제 후 재생성 권장)</summary>
        public void UpdateEdge(int id, string rel, string propsJson)
        {
            if (id <= 0) throw new ArgumentException("id required");
            if (string.IsNullOrWhiteSpace(rel)) throw new ArgumentException("rel required");
            string props = string.IsNullOrWhiteSpace(propsJson) ? "{}" : propsJson;
            try { using var _ = JsonDocument.Parse(props); }
            catch (JsonException ex)
            {
                throw new ArgumentException($"PropsJson is not valid JSON: {ex.Message}");
            }

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "UPDATE kg_edge SET rel=@r, props_json=@p WHERE id=@i", conn);
            cmd.Parameters.AddWithValue("@r", rel);
            cmd.Parameters.AddWithValue("@p", props);
            cmd.Parameters.AddWithValue("@i", id);
            cmd.ExecuteNonQuery();
        }

        public int DeleteEdge(int id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM kg_edge WHERE id=@i", conn);
            cmd.Parameters.AddWithValue("@i", id);
            return cmd.ExecuteNonQuery();
        }

        // ── 도메인 일괄 작업 ─────────────────────────────

        /// <summary>해당 도메인의 모든 노드/엣지/자산링크를 제거. 도메인 자체(kg_domain row) 는 보존.</summary>
        public (int Nodes, int Edges) ClearDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return (0, 0);
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            int en, nn;
            using (var de = new SqliteCommand("DELETE FROM kg_edge WHERE domain=@d", conn, tx))
            {
                de.Parameters.AddWithValue("@d", domain);
                en = de.ExecuteNonQuery();
            }
            // 자산 링크는 노드가 사라지면 어차피 외래키 위반 — 먼저 정리
            using (var dl = new SqliteCommand(@"
DELETE FROM asset_kg_link
 WHERE kg_node_id IN (SELECT id FROM kg_node WHERE domain=@d)", conn, tx))
            {
                dl.Parameters.AddWithValue("@d", domain);
                dl.ExecuteNonQuery();
            }
            using (var dn = new SqliteCommand("DELETE FROM kg_node WHERE domain=@d", conn, tx))
            {
                dn.Parameters.AddWithValue("@d", domain);
                nn = dn.ExecuteNonQuery();
            }
            tx.Commit();
            return (nn, en);
        }

        // ── helpers ──────────────────────────────────────

        private static bool NodeExists(SqliteConnection conn, string id)
        {
            using var cmd = new SqliteCommand(
                "SELECT 1 FROM kg_node WHERE id=@i LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@i", id);
            using var r = cmd.ExecuteReader();
            return r.Read();
        }

        /// <summary>단일 노드 조회 — 편집 다이얼로그 진입 시 props 로드용.</summary>
        public KgNode? GetNodeById(string id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT id,type,label,props_json FROM kg_node WHERE id=@i", conn);
            cmd.Parameters.AddWithValue("@i", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new KgNode
            {
                Id = r.GetString(0), Type = r.GetString(1),
                Label = r.GetString(2), PropsJson = r.GetString(3),
            };
        }

        /// <summary>전체 도메인 목록 조회 — 노드 통계로부터 (kg_domain 테이블 없을 때의 폴백).</summary>
        public List<string> GetDomainsInUse()
        {
            var list = new List<string>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT DISTINCT domain FROM kg_node ORDER BY domain", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }
    }
}
