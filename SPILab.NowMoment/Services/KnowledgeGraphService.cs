using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    // ════════════════════════════════════════════════════════════
    // KnowledgeGraphService — 다중 도메인 KG 임포트 / 질의 / 그래프 뷰 (v2.6)
    //
    // v2.6 변경: 도메인 컬럼(domain) 추가 — "cs" / "photo" 등 분리 보관
    //   * kg_node.domain    TEXT NOT NULL DEFAULT 'cs'
    //   * kg_edge.domain    TEXT NOT NULL DEFAULT 'cs'
    //   * 모든 조회 메서드에 domain 선택 인자 추가 (빈값="" 이면 전체)
    //   * ImportFromJson(jsonPath, domain) — 해당 도메인의 노드/엣지만 교체
    //
    // 동일 SQLite 파일(nowmoment.db)에 KG 테이블 3개:
    //   kg_node, kg_edge, asset_kg_link
    // ════════════════════════════════════════════════════════════
    public partial class KnowledgeGraphService
    {
        private readonly string _dbPath;
        private string ConnStr => $"Data Source={_dbPath}";

        // 도메인 상수
        public const string DOMAIN_CS       = "cs";
        public const string DOMAIN_PHOTO    = "photo";
        public const string DOMAIN_CMP      = "cmp";
        public const string DOMAIN_ETCH     = "etch";
        public const string DOMAIN_THINFILM = "thinfilm";

        public KnowledgeGraphService(string dbPath)
        {
            _dbPath = dbPath;
            EnsureKgTables();
            EnsureDomainColumns();   // v2.6 마이그레이션 — 기존 DB 호환
        }

        // ── 스키마 ───────────────────────────────────────
        public void EnsureKgTables()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            const string sql = @"
CREATE TABLE IF NOT EXISTS kg_node (
    id          TEXT PRIMARY KEY,
    type        TEXT NOT NULL,
    label       TEXT NOT NULL,
    props_json  TEXT NOT NULL DEFAULT '{}',
    domain      TEXT NOT NULL DEFAULT 'cs',
    imported_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS idx_kg_node_type   ON kg_node(type);
CREATE INDEX IF NOT EXISTS idx_kg_node_label  ON kg_node(label);
CREATE INDEX IF NOT EXISTS idx_kg_node_domain ON kg_node(domain);

CREATE TABLE IF NOT EXISTS kg_edge (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    src_id      TEXT NOT NULL REFERENCES kg_node(id) ON DELETE CASCADE,
    dst_id      TEXT NOT NULL REFERENCES kg_node(id) ON DELETE CASCADE,
    rel         TEXT NOT NULL,
    props_json  TEXT NOT NULL DEFAULT '{}',
    domain      TEXT NOT NULL DEFAULT 'cs'
);
CREATE INDEX IF NOT EXISTS idx_kg_edge_src    ON kg_edge(src_id);
CREATE INDEX IF NOT EXISTS idx_kg_edge_dst    ON kg_edge(dst_id);
CREATE INDEX IF NOT EXISTS idx_kg_edge_rel    ON kg_edge(rel);
CREATE INDEX IF NOT EXISTS idx_kg_edge_domain ON kg_edge(domain);

CREATE TABLE IF NOT EXISTS asset_kg_link (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_type  TEXT NOT NULL,
    asset_id    INTEGER NOT NULL,
    kg_node_id  TEXT NOT NULL REFERENCES kg_node(id) ON DELETE CASCADE,
    link_type   TEXT NOT NULL DEFAULT 'implements',
    note        TEXT DEFAULT '',
    created_at  TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE(asset_type, asset_id, kg_node_id, link_type)
);
CREATE INDEX IF NOT EXISTS idx_link_asset ON asset_kg_link(asset_type, asset_id);
CREATE INDEX IF NOT EXISTS idx_link_node  ON asset_kg_link(kg_node_id);
";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// v2.5 이전 DB 에 domain 컬럼이 없으면 ALTER 로 추가.
        /// 새 DB(EnsureKgTables 가 처음부터 만든)는 이미 컬럼이 있어 무시됨.
        ///
        /// 주의: SQLite 의 ALTER TABLE ... ADD COLUMN 은 NOT NULL DEFAULT 함께
        ///       지정 가능하지만, 일부 빌드 환경에서 거부되는 경우가 있다.
        ///       그래서 (1) 컬럼만 추가 → (2) 기존 행에 UPDATE → (3) 컬럼 인덱스 생성 순으로
        ///       단계 분리해 안전하게 처리한다.
        /// </summary>
        private void EnsureDomainColumns()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            AddDomainColumnIfMissing(conn, "kg_node");
            AddDomainColumnIfMissing(conn, "kg_edge");
        }

        private static void AddDomainColumnIfMissing(SqliteConnection conn, string table)
        {
            if (HasColumn(conn, table, "domain")) return;

            // 1) NULL 허용 + DEFAULT 'cs' 로 추가 (SQLite 가장 호환성 높은 형태)
            using (var add = new SqliteCommand(
                $"ALTER TABLE {table} ADD COLUMN domain TEXT DEFAULT 'cs'", conn))
            {
                add.ExecuteNonQuery();
            }
            // 2) 혹시 기존 행이 NULL 인 게 있으면 명시적으로 'cs' 채우기
            using (var fill = new SqliteCommand(
                $"UPDATE {table} SET domain='cs' WHERE domain IS NULL", conn))
            {
                fill.ExecuteNonQuery();
            }
            // 3) 인덱스
            using (var ix = new SqliteCommand(
                $"CREATE INDEX IF NOT EXISTS idx_{table}_domain ON {table}(domain)", conn))
            {
                ix.ExecuteNonQuery();
            }
        }

        private static bool HasColumn(SqliteConnection conn, string table, string col)
        {
            using var cmd = new SqliteCommand($"PRAGMA table_info({table})", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), col, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── 임포트 ───────────────────────────────────────
        /// <summary>
        /// build_kg_*.py 가 만든 JSON-LD 파일을 적재.
        /// 해당 도메인의 기존 KG 데이터만 교체하고 다른 도메인은 보존.
        /// </summary>
        public KgStats ImportFromJson(string jsonPath, string domain = DOMAIN_CS)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"KG JSON not found: {jsonPath}");
            if (string.IsNullOrWhiteSpace(domain))
                domain = DOMAIN_CS;

            var raw = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // JSON 안에 domain 필드가 있으면 우선 사용 (build_kg_photo.py 가 "raypann_photo" 등을 넣을 수 있음)
            // 하지만 SQLite 에는 사용자가 호출한 domain 인자를 그대로 사용 (UI 일관성 우선)

            var nodes = root.GetProperty("nodes");
            var edges = root.GetProperty("edges");

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            // 해당 도메인의 기존 KG 만 제거 (다른 도메인 보존)
            using (var d1 = new SqliteCommand("DELETE FROM kg_edge WHERE domain=@d", conn, tx))
            { d1.Parameters.AddWithValue("@d", domain); d1.ExecuteNonQuery(); }
            using (var d2 = new SqliteCommand("DELETE FROM kg_node WHERE domain=@d", conn, tx))
            { d2.Parameters.AddWithValue("@d", domain); d2.ExecuteNonQuery(); }

            // 노드
            using (var insN = new SqliteCommand(
                "INSERT OR REPLACE INTO kg_node(id,type,label,props_json,domain) VALUES(@i,@t,@l,@p,@d)", conn, tx))
            {
                insN.Parameters.Add("@i", SqliteType.Text);
                insN.Parameters.Add("@t", SqliteType.Text);
                insN.Parameters.Add("@l", SqliteType.Text);
                insN.Parameters.Add("@p", SqliteType.Text);
                insN.Parameters.Add("@d", SqliteType.Text);
                foreach (var n in nodes.EnumerateArray())
                {
                    insN.Parameters["@i"].Value = n.GetProperty("id").GetString() ?? "";
                    insN.Parameters["@t"].Value = n.GetProperty("type").GetString() ?? "";
                    insN.Parameters["@l"].Value = n.GetProperty("label").GetString() ?? "";
                    insN.Parameters["@p"].Value = n.TryGetProperty("props", out var pe)
                        ? pe.GetRawText() : "{}";
                    insN.Parameters["@d"].Value = domain;
                    insN.ExecuteNonQuery();
                }
            }

            // 엣지
            using (var insE = new SqliteCommand(
                "INSERT INTO kg_edge(src_id,dst_id,rel,props_json,domain) VALUES(@s,@dst,@r,@p,@d)", conn, tx))
            {
                insE.Parameters.Add("@s",   SqliteType.Text);
                insE.Parameters.Add("@dst", SqliteType.Text);
                insE.Parameters.Add("@r",   SqliteType.Text);
                insE.Parameters.Add("@p",   SqliteType.Text);
                insE.Parameters.Add("@d",   SqliteType.Text);
                foreach (var e in edges.EnumerateArray())
                {
                    var src = e.GetProperty("src").GetString() ?? "";
                    var dst = e.GetProperty("dst").GetString() ?? "";
                    var rel = e.GetProperty("rel").GetString() ?? "";

                    // CITES 관계만 인용 노드 자동 생성 (기타 관계는 노드 INSERT 단계에서 이미 처리됨).
                    // INSERT OR IGNORE 로 처리하여 같은 ID 가 이미 있어도 충돌하지 않음.
                    if (rel == "CITES")
                    {
                        EnsureNodeIfMissing(conn, tx, dst, "Citation",
                            e.TryGetProperty("props", out var ep) && ep.TryGetProperty("text", out var tx_)
                                ? tx_.GetString() ?? dst : dst,
                            domain);
                    }
                    insE.Parameters["@s"].Value   = src;
                    insE.Parameters["@dst"].Value = dst;
                    insE.Parameters["@r"].Value   = rel;
                    insE.Parameters["@p"].Value   = e.TryGetProperty("props", out var pe)
                        ? pe.GetRawText() : "{}";
                    insE.Parameters["@d"].Value   = domain;
                    insE.ExecuteNonQuery();
                }
            }
            tx.Commit();

            return GetStats(jsonPath, domain);
        }

        private static void EnsureNodeIfMissing(
            SqliteConnection conn, SqliteTransaction tx,
            string id, string type, string label, string domain)
        {
            // INSERT OR IGNORE 로 처리하면 SELECT 체크 없이 안전하게 멱등 동작.
            // 같은 트랜잭션 내 가시성 이슈도 회피.
            using var ins = new SqliteCommand(
                "INSERT OR IGNORE INTO kg_node(id,type,label,props_json,domain) VALUES(@i,@t,@l,'{}',@d)", conn, tx);
            ins.Parameters.AddWithValue("@i", id);
            ins.Parameters.AddWithValue("@t", type);
            ins.Parameters.AddWithValue("@l", label);
            ins.Parameters.AddWithValue("@d", domain);
            ins.ExecuteNonQuery();
        }

        // ── 노드/엣지 조회 (도메인 인자 추가) ───────────
        public List<KgNode> GetNodes(string typeFilter = "", string keyword = "", string domain = "")
        {
            var list = new List<KgNode>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"SELECT id,type,label,props_json FROM kg_node
                        WHERE (@t='' OR type=@t)
                          AND (@k='' OR label LIKE @k OR id LIKE @k OR props_json LIKE @k)
                          AND (@d='' OR domain=@d)
                        ORDER BY type, id";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", typeFilter ?? "");
            cmd.Parameters.AddWithValue("@k",
                string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            cmd.Parameters.AddWithValue("@d", domain ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new KgNode
                {
                    Id = r.GetString(0), Type = r.GetString(1),
                    Label = r.GetString(2), PropsJson = r.GetString(3),
                });
            return list;
        }

        public List<KgEdge> GetEdges(string nodeId = "", string relFilter = "", string domain = "")
        {
            var list = new List<KgEdge>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"SELECT e.id,e.src_id,e.dst_id,e.rel,e.props_json,
                               ns.label, nd.label, ns.type, nd.type
                        FROM kg_edge e
                        JOIN kg_node ns ON ns.id=e.src_id
                        JOIN kg_node nd ON nd.id=e.dst_id
                        WHERE (@n='' OR e.src_id=@n OR e.dst_id=@n)
                          AND (@r='' OR e.rel=@r)
                          AND (@d='' OR e.domain=@d)
                        ORDER BY e.rel, ns.label";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@n", nodeId ?? "");
            cmd.Parameters.AddWithValue("@r", relFilter ?? "");
            cmd.Parameters.AddWithValue("@d", domain ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new KgEdge
                {
                    Id = r.GetInt32(0), SrcId = r.GetString(1), DstId = r.GetString(2),
                    Rel = r.GetString(3), PropsJson = r.GetString(4),
                    SrcLabel = r.GetString(5), DstLabel = r.GetString(6),
                    SrcType = r.GetString(7), DstType = r.GetString(8),
                });
            return list;
        }

        // ── 링크 (자산 ↔ KG) — 도메인 영향 없음 ─────────
        public void LinkAsset(AssetKgLink l)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT OR IGNORE INTO asset_kg_link(asset_type,asset_id,kg_node_id,link_type,note)
                VALUES(@at,@ai,@n,@lt,@no)", conn);
            cmd.Parameters.AddWithValue("@at", l.AssetType);
            cmd.Parameters.AddWithValue("@ai", l.AssetId);
            cmd.Parameters.AddWithValue("@n",  l.KgNodeId);
            cmd.Parameters.AddWithValue("@lt", l.LinkType);
            cmd.Parameters.AddWithValue("@no", l.Note ?? "");
            cmd.ExecuteNonQuery();
        }

        public void UnlinkAsset(int linkId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM asset_kg_link WHERE id=@i", conn);
            cmd.Parameters.AddWithValue("@i", linkId);
            cmd.ExecuteNonQuery();
        }

        public List<AssetKgLink> GetLinksForAsset(string assetType, int assetId)
        {
            var list = new List<AssetKgLink>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT id, asset_type, asset_id, kg_node_id, link_type, note, created_at
                FROM asset_kg_link
                WHERE asset_type=@at AND asset_id=@ai
                ORDER BY created_at DESC", conn);
            cmd.Parameters.AddWithValue("@at", assetType);
            cmd.Parameters.AddWithValue("@ai", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetKgLink
                {
                    Id = r.GetInt32(0), AssetType = r.GetString(1), AssetId = r.GetInt32(2),
                    KgNodeId = r.GetString(3), LinkType = r.GetString(4), Note = r.GetString(5),
                    CreatedAt = DateTime.Parse(r.GetString(6)),
                });
            return list;
        }

        public List<KgNode> GetLinkedNodesForAsset(string assetType, int assetId)
        {
            var list = new List<KgNode>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT n.id, n.type, n.label, n.props_json
                FROM asset_kg_link l JOIN kg_node n ON n.id=l.kg_node_id
                WHERE l.asset_type=@at AND l.asset_id=@ai", conn);
            cmd.Parameters.AddWithValue("@at", assetType);
            cmd.Parameters.AddWithValue("@ai", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new KgNode
                {
                    Id = r.GetString(0), Type = r.GetString(1),
                    Label = r.GetString(2), PropsJson = r.GetString(3),
                });
            return list;
        }

        // ── v3.0 F-001 Step 1.6/1.7 헬퍼 ─────────────────
        /// <summary>자산이 1개 이상 연결된 KG 노드 ID 셋 (그래프 강조용).</summary>
        public HashSet<string> GetLinkedKgNodeIds()
        {
            var set = new HashSet<string>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT DISTINCT kg_node_id FROM asset_kg_link", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(0));
            return set;
        }

        /// <summary>특정 KG 노드에 연결된 자산 목록 (KG 탭 우측 패널용).</summary>
        public List<LinkedAssetRow> GetAssetsLinkedToNode(string kgNodeId)
        {
            var list = new List<LinkedAssetRow>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = new SqliteCommand(@"
                SELECT l.id, l.asset_type, l.asset_id, l.link_type, l.note, l.created_at,
                       COALESCE(c.name, m.name, e.name, d.title, p.title, '(unknown)') AS asset_name
                FROM asset_kg_link l
                LEFT JOIN asset_code       c ON l.asset_type='asset_code'       AND c.id = l.asset_id
                LEFT JOIN asset_model      m ON l.asset_type='asset_model'      AND m.id = l.asset_id
                LEFT JOIN asset_experiment e ON l.asset_type='asset_experiment' AND e.id = l.asset_id
                LEFT JOIN asset_document   d ON l.asset_type='asset_document'   AND d.id = l.asset_id
                LEFT JOIN asset_patent     p ON l.asset_type='asset_patent'     AND p.id = l.asset_id
                WHERE l.kg_node_id = @id
                ORDER BY l.created_at DESC", conn);
            cmd.Parameters.AddWithValue("@id", kgNodeId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LinkedAssetRow
                {
                    LinkId    = r.GetInt32(0),
                    AssetType = r.GetString(1),
                    AssetId   = r.GetInt32(2),
                    LinkType  = r.GetString(3),
                    Note      = r.GetString(4),
                    CreatedAt = DateTime.Parse(r.GetString(5)),
                    AssetName = r.GetString(6),
                });
            }
            return list;
        }

        // ── 통계 (도메인 인자) ───────────────────────────
        public KgStats GetStats(string sourceFile = "", string domain = "")
        {
            var s = new KgStats { SourceFile = sourceFile };
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            string nFilter = string.IsNullOrEmpty(domain) ? "" : "WHERE domain=@d";
            string eFilter = string.IsNullOrEmpty(domain) ? "" : "WHERE domain=@d";

            using (var c = new SqliteCommand($"SELECT COUNT(*) FROM kg_node {nFilter}", conn))
            {
                if (!string.IsNullOrEmpty(domain)) c.Parameters.AddWithValue("@d", domain);
                s.Nodes = (int)(long)(c.ExecuteScalar() ?? 0L);
            }
            using (var c = new SqliteCommand($"SELECT COUNT(*) FROM kg_edge {eFilter}", conn))
            {
                if (!string.IsNullOrEmpty(domain)) c.Parameters.AddWithValue("@d", domain);
                s.Edges = (int)(long)(c.ExecuteScalar() ?? 0L);
            }

            using (var c = new SqliteCommand(
                $"SELECT type,COUNT(*) FROM kg_node {nFilter} GROUP BY type", conn))
            {
                if (!string.IsNullOrEmpty(domain)) c.Parameters.AddWithValue("@d", domain);
                using var r = c.ExecuteReader();
                while (r.Read()) s.NodesByType[r.GetString(0)] = (int)r.GetInt64(1);
            }
            using (var c = new SqliteCommand(
                $"SELECT rel,COUNT(*) FROM kg_edge {eFilter} GROUP BY rel", conn))
            {
                if (!string.IsNullOrEmpty(domain)) c.Parameters.AddWithValue("@d", domain);
                using var r = c.ExecuteReader();
                while (r.Read()) s.EdgesByRel[r.GetString(0)] = (int)r.GetInt64(1);
            }

            s.ImportedAt = DateTime.Now;
            return s;
        }

        // ── SPARQL-like 질의 (자주 쓰는 패턴) — 기존 호환 ─
        public List<KgNode> GetParametersOfRule(string ruleId)
            => GetEdges(ruleId, "USES")
                .Where(e => e.SrcId == ruleId && e.DstType == "Parameter")
                .Select(e => new KgNode { Id = e.DstId, Type = "Parameter", Label = e.DstLabel })
                .ToList();

        public List<KgNode> GetRulesOfWorkspace(string workspaceId)
            => GetEdges(workspaceId, "BELONGS_TO")
                .Where(e => e.DstId == workspaceId && e.SrcType == "PhysicsRule")
                .Select(e => new KgNode { Id = e.SrcId, Type = "PhysicsRule", Label = e.SrcLabel })
                .ToList();

        public List<KgNode> GetRulesGoverningSpec(string specId)
            => GetEdges(specId, "GOVERNS")
                .Where(e => e.DstId == specId && e.SrcType == "PhysicsRule")
                .Select(e => new KgNode { Id = e.SrcId, Type = "PhysicsRule", Label = e.SrcLabel })
                .ToList();

        public (List<GraphViewNode>, List<GraphViewEdge>) BuildView(
            string focusNodeId = "", int radius = 220)
        {
            var nodes = string.IsNullOrEmpty(focusNodeId)
                ? GetNodes("PhysicsRule")
                : GetNeighbors(focusNodeId);
            var edges = string.IsNullOrEmpty(focusNodeId)
                ? GetEdges()
                : GetEdges(focusNodeId);

            var view = new List<GraphViewNode>();
            int n = nodes.Count;
            for (int i = 0; i < n; i++)
            {
                double theta = 2 * Math.PI * i / Math.Max(1, n);
                view.Add(new GraphViewNode
                {
                    Id = nodes[i].Id, Label = nodes[i].Label, Type = nodes[i].Type,
                    X = 320 + radius * Math.Cos(theta),
                    Y = 320 + radius * Math.Sin(theta),
                    Color = ColorOf(nodes[i].Type),
                });
            }
            var ve = edges.Select(e => new GraphViewEdge
            { SrcId = e.SrcId, DstId = e.DstId, Rel = e.Rel }).ToList();
            return (view, ve);
        }

        public List<KgNode> GetNeighbors(string nodeId)
        {
            var list = new List<KgNode>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT DISTINCT n.id, n.type, n.label, n.props_json
                FROM kg_node n
                WHERE n.id IN (
                    SELECT dst_id FROM kg_edge WHERE src_id=@n
                    UNION
                    SELECT src_id FROM kg_edge WHERE dst_id=@n
                )", conn);
            cmd.Parameters.AddWithValue("@n", nodeId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new KgNode
                {
                    Id = r.GetString(0), Type = r.GetString(1),
                    Label = r.GetString(2), PropsJson = r.GetString(3),
                });
            return list;
        }

        private static string ColorOf(string type) => type switch
        {
            "PhysicsRule" => "#E85577",
            "Material"    => "#2EA4C7",
            "Workspace"   => "#4CAF72",
            "Parameter"   => "#E8A838",
            "Spec"        => "#8B6FBF",
            _             => "#9DB4CC",
        };
    }
}
