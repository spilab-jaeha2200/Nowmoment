// ════════════════════════════════════════════════════════════════════
// KnowledgeGraphService.Import.cs (v2.7)
//
// 통합 임포트 진입점 — 파일 확장자로 JSON / TTL 자동 분기.
//
//   ImportFromFile(path, domain)   ← 사용자가 호출
//        ├ .json/.jsonld → 기존 ImportFromJson 위임
//        └ .ttl/.rdf/.nt → 새로 추가된 ImportFromTtl
//
// TTL 파싱은 dotNetRDF (NuGet: dotNetRDF) 사용.
//   .csproj 에 추가:
//     <PackageReference Include="dotNetRDF" Version="3.2.1" />
//
// 매핑 규약:
//   * 노드 id    = subject URI 단축형 (prefix:local 또는 local)
//   * 노드 type  = rdf:type 의 object 의 LocalName (없으면 "Resource")
//   * 노드 label = rdfs:label / skos:prefLabel / schema:name  (우선순위 순)
//   * 노드 props = 그 외 datatype property 를 {pred:value} JSON 객체로 수집
//   * 엣지 rel   = predicate LocalName 의 UPPERCASE
//                  (단 rdf:type / rdfs:label / skos:prefLabel / schema:name 제외)
//
// 사용 조건:
//   1) 기존 KnowledgeGraphService.cs 의 클래스 선언을 partial 로 변경.
//   2) 본 파일을 Services/ 에 추가.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace SPILab.NowMoment.Services
{
    public partial class KnowledgeGraphService
    {
        /// <summary>
        /// 확장자 기반 자동 분기 임포트.
        /// 지원: .json / .jsonld → JSON-LD 빌더 출력
        ///       .ttl  / .rdf  / .nt / .n3 → RDF
        /// </summary>
        public KgStats ImportFromFile(string path, string domain = DOMAIN_CS)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {path}");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".json"  or ".jsonld"          => ImportFromJson(path, domain),
                ".ttl"   or ".rdf" or ".nt" or ".n3" => ImportFromTtl(path, domain),
                _ => throw new NotSupportedException(
                        $"지원하지 않는 확장자: {ext}\n지원: .json, .jsonld, .ttl, .rdf, .nt, .n3"),
            };
        }

        // ── TTL/RDF 임포트 ──────────────────────────────
        public KgStats ImportFromTtl(string ttlPath, string domain = DOMAIN_CS)
        {
            if (!File.Exists(ttlPath))
                throw new FileNotFoundException($"TTL not found: {ttlPath}");
            if (string.IsNullOrWhiteSpace(domain)) domain = DOMAIN_CS;

            // 1) 그래프 로드 — 확장자별 파서 자동 선택
            var g = new Graph();
            var ext = Path.GetExtension(ttlPath).ToLowerInvariant();
            try
            {
                IRdfReader reader = ext switch
                {
                    ".nt" => new NTriplesParser(),
                    ".n3" => new Notation3Parser(),
                    ".rdf" => new RdfXmlParser(),
                    _ => new TurtleParser(),
                };
                reader.Load(g, ttlPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"RDF 파싱 실패: {ex.Message}", ex);
            }

            // 2) Subject 별로 (type, label, props) 집계, edge 별도 수집
            var typePred  = g.CreateUriNode(UriFactory.Create(NamespaceMapper.RDF + "type"));
            var labelPred = g.CreateUriNode(UriFactory.Create(NamespaceMapper.RDFS + "label"));
            // 옵션 라벨 prop 들 — 도메인에 따라 사용
            var skosPref  = TryUri(g, "http://www.w3.org/2004/02/skos/core#prefLabel");
            var schemaName= TryUri(g, "http://schema.org/name");

            // 매핑된 노드 (key = id 단축형)
            var nodes = new Dictionary<string, NodeBuf>();
            var edges = new List<EdgeBuf>();

            foreach (var t in g.Triples)
            {
                // subject 는 IRI / blank 둘 다 가능 — blank 도 일단 id 부여해 보존
                var sId = ShortId(t.Subject);
                if (!nodes.TryGetValue(sId, out var nb))
                {
                    nb = new NodeBuf { Id = sId, Type = "Resource", Label = sId, Props = new Dictionary<string, object>() };
                    nodes[sId] = nb;
                }

                // rdf:type
                if (Equals(t.Predicate, typePred))
                {
                    nb.Type = LocalName(t.Object);
                    continue;
                }
                // 라벨류
                if (IsLabelPredicate(t.Predicate, labelPred, skosPref, schemaName))
                {
                    if (t.Object is ILiteralNode lit) nb.Label = lit.Value;
                    continue;
                }

                // datatype property → props 에 누적
                if (t.Object is ILiteralNode dl)
                {
                    var key = LocalName(t.Predicate);
                    // 같은 키가 두 번 이상이면 배열로 승격
                    if (nb.Props.TryGetValue(key, out var prev))
                    {
                        if (prev is List<object> arr) arr.Add(dl.Value);
                        else nb.Props[key] = new List<object> { prev, dl.Value };
                    }
                    else nb.Props[key] = dl.Value;
                    continue;
                }

                // object property → 엣지
                edges.Add(new EdgeBuf
                {
                    SrcId = sId,
                    DstId = ShortId(t.Object),
                    Rel   = LocalName(t.Predicate).ToUpperInvariant(),
                });
            }

            // 엣지의 dst 노드가 type 트리플을 갖지 않은 경우(예: 인용 텍스트만 있는 외부 IRI) —
            // ImportFromJson 의 EnsureNodeIfMissing 과 같은 정책으로 placeholder 생성.
            foreach (var e in edges)
            {
                if (!nodes.ContainsKey(e.DstId))
                {
                    nodes[e.DstId] = new NodeBuf
                    {
                        Id = e.DstId, Type = "Resource", Label = e.DstId,
                        Props = new Dictionary<string, object>(),
                    };
                }
            }

            // 3) 트랜잭션으로 도메인 교체 + INSERT (ImportFromJson 과 동일 정책)
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var d1 = new SqliteCommand("DELETE FROM kg_edge WHERE domain=@d", conn, tx))
            { d1.Parameters.AddWithValue("@d", domain); d1.ExecuteNonQuery(); }
            using (var d2 = new SqliteCommand("DELETE FROM kg_node WHERE domain=@d", conn, tx))
            { d2.Parameters.AddWithValue("@d", domain); d2.ExecuteNonQuery(); }

            using (var insN = new SqliteCommand(
                "INSERT OR REPLACE INTO kg_node(id,type,label,props_json,domain) VALUES(@i,@t,@l,@p,@d)", conn, tx))
            {
                insN.Parameters.Add("@i", SqliteType.Text);
                insN.Parameters.Add("@t", SqliteType.Text);
                insN.Parameters.Add("@l", SqliteType.Text);
                insN.Parameters.Add("@p", SqliteType.Text);
                insN.Parameters.Add("@d", SqliteType.Text);
                foreach (var n in nodes.Values)
                {
                    insN.Parameters["@i"].Value = n.Id;
                    insN.Parameters["@t"].Value = n.Type;
                    insN.Parameters["@l"].Value = n.Label;
                    insN.Parameters["@p"].Value = JsonSerializer.Serialize(n.Props);
                    insN.Parameters["@d"].Value = domain;
                    insN.ExecuteNonQuery();
                }
            }

            using (var insE = new SqliteCommand(
                "INSERT INTO kg_edge(src_id,dst_id,rel,props_json,domain) VALUES(@s,@dst,@r,'{}',@d)", conn, tx))
            {
                insE.Parameters.Add("@s",   SqliteType.Text);
                insE.Parameters.Add("@dst", SqliteType.Text);
                insE.Parameters.Add("@r",   SqliteType.Text);
                insE.Parameters.Add("@d",   SqliteType.Text);
                foreach (var e in edges)
                {
                    insE.Parameters["@s"].Value   = e.SrcId;
                    insE.Parameters["@dst"].Value = e.DstId;
                    insE.Parameters["@r"].Value   = e.Rel;
                    insE.Parameters["@d"].Value   = domain;
                    insE.ExecuteNonQuery();
                }
            }
            tx.Commit();

            return GetStats(ttlPath, domain);
        }

        // ── helpers ──────────────────────────────────────

        private static IUriNode? TryUri(IGraph g, string uri)
        {
            try { return g.CreateUriNode(UriFactory.Create(uri)); } catch { return null; }
        }

        private static bool IsLabelPredicate(INode p, INode rdfsLabel, IUriNode? skosPref, IUriNode? schemaName)
        {
            if (rdfsLabel != null && p.Equals(rdfsLabel)) return true;
            if (skosPref  != null && p.Equals(skosPref))  return true;
            if (schemaName!= null && p.Equals(schemaName))return true;
            return false;
        }

        /// <summary>IRI 의 LocalName (# 또는 / 뒤). Literal 은 값 그대로.</summary>
        private static string LocalName(INode n)
        {
            if (n is IUriNode u)
            {
                var s = u.Uri.ToString();
                int i = Math.Max(s.LastIndexOf('#'), s.LastIndexOf('/'));
                return i >= 0 && i + 1 < s.Length ? s.Substring(i + 1) : s;
            }
            if (n is ILiteralNode l) return l.Value;
            return n.ToString() ?? "";
        }

        /// <summary>
        /// 노드 ID 단축형:
        ///   IRI 가 prefix:local 로 표현 가능하면 "prefix:local",
        ///   아니면 LocalName, blank 노드는 "_:bN".
        /// </summary>
        private static string ShortId(INode n)
        {
            if (n is IUriNode u)
            {
                var uri = u.Uri.ToString();
                // 슬래시/해시 마지막 segment 가 충분히 식별성이 있다고 본다.
                // 동일 LocalName 충돌 가능성을 줄이기 위해 직전 segment 를 prefix 로 부여.
                int hash = uri.LastIndexOf('#');
                if (hash >= 0 && hash + 1 < uri.Length)
                {
                    var ns = uri.Substring(0, hash).TrimEnd('/').Split('/').LastOrDefault() ?? "";
                    var local = uri.Substring(hash + 1);
                    return string.IsNullOrEmpty(ns) ? local : $"{ns}:{local}";
                }
                int slash = uri.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < uri.Length)
                {
                    var local = uri.Substring(slash + 1);
                    var rest  = uri.Substring(0, slash);
                    var ns    = rest.Split('/').LastOrDefault() ?? "";
                    return string.IsNullOrEmpty(ns) ? local : $"{ns}:{local}";
                }
                return uri;
            }
            if (n is IBlankNode b) return "_:" + b.InternalID;
            if (n is ILiteralNode l) return l.Value;
            return n.ToString() ?? "";
        }

        private class NodeBuf
        {
            public string Id    = "";
            public string Type  = "Resource";
            public string Label = "";
            public Dictionary<string, object> Props = new();
        }
        private class EdgeBuf
        {
            public string SrcId = "";
            public string DstId = "";
            public string Rel   = "";
        }
    }
}
