// ════════════════════════════════════════════════════════════
// TtlSparqlEngine.cs — v3.0 F-005 TTL Studio (Step 5.1)
//
// TtlOntology 를 in-memory RDF 그래프로 변환한 후 SPARQL 쿼리 실행.
// dotNetRDF 의 Leviathan SPARQL 엔진을 사용 (외부 서버 불필요).
//
// 결과는 SparqlResultRow 리스트로 반환되어 DataGrid 에 바로 바인딩 가능.
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using SPILab.NowMoment.Models.Ttl;

namespace SPILab.NowMoment.Services.Ttl
{
    /// <summary>SPARQL 결과 행 — DataGrid 표시용 동적 사전.</summary>
    public class SparqlResultRow
    {
        public Dictionary<string, string> Values { get; } = new();

        /// <summary>결과를 한 줄 요약으로 (디버그·로그용).</summary>
        public override string ToString()
            => string.Join(" | ", Values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public class TtlSparqlEngine
    {
        private const string RdfNs   = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string RdfsNs  = "http://www.w3.org/2000/01/rdf-schema#";
        private const string OwlNs   = "http://www.w3.org/2002/07/owl#";
        private const string XsdNs   = "http://www.w3.org/2001/XMLSchema#";

        /// <summary>TtlOntology 의 모든 진술을 RDF 그래프로 빌드.</summary>
        public IGraph BuildGraph(TtlOntology onto)
        {
            var g = new Graph();
            g.NamespaceMap.AddNamespace("rdf",  new Uri(RdfNs));
            g.NamespaceMap.AddNamespace("rdfs", new Uri(RdfsNs));
            g.NamespaceMap.AddNamespace("owl",  new Uri(OwlNs));
            g.NamespaceMap.AddNamespace("xsd",  new Uri(XsdNs));
            g.NamespaceMap.AddNamespace(onto.BasePrefix, new Uri(onto.BaseUri));
            g.BaseUri = new Uri(onto.BaseUri);

            // 이 부분은 TtlIOService.Export 와 사실상 동일한 로직.
            // 코드 중복 대신 TtlIOService 를 재사용해도 되지만 메모리 그래프로만
            // 빌드하면 되니 간단히 인라인.
            var rdfType  = g.CreateUriNode(new Uri(RdfNs + "type"));
            var rdfsLabel = g.CreateUriNode(new Uri(RdfsNs + "label"));
            var owlClass = g.CreateUriNode(new Uri(OwlNs + "Class"));
            var owlObj   = g.CreateUriNode(new Uri(OwlNs + "ObjectProperty"));
            var owlData  = g.CreateUriNode(new Uri(OwlNs + "DatatypeProperty"));

            foreach (var c in onto.Classes)
            {
                if (string.IsNullOrWhiteSpace(c.LocalName)) continue;
                var n = g.CreateUriNode(new Uri(onto.BaseUri + c.LocalName));
                g.Assert(n, rdfType, owlClass);
                if (!string.IsNullOrEmpty(c.Label))
                    g.Assert(n, rdfsLabel, g.CreateLiteralNode(c.Label));
            }
            foreach (var p in onto.Properties)
            {
                if (string.IsNullOrWhiteSpace(p.LocalName)) continue;
                var n = g.CreateUriNode(new Uri(onto.BaseUri + p.LocalName));
                g.Assert(n, rdfType, p.Kind == TtlPropertyKind.ObjectProperty ? owlObj : owlData);
                if (!string.IsNullOrEmpty(p.Label))
                    g.Assert(n, rdfsLabel, g.CreateLiteralNode(p.Label));
            }
            foreach (var i in onto.Instances)
            {
                if (string.IsNullOrWhiteSpace(i.LocalName)) continue;
                var n = g.CreateUriNode(new Uri(onto.BaseUri + i.LocalName));
                if (!string.IsNullOrEmpty(i.ClassOf))
                {
                    var cls = g.CreateUriNode(new Uri(onto.BaseUri + i.ClassOf));
                    g.Assert(n, rdfType, cls);
                }
                if (!string.IsNullOrEmpty(i.Label))
                    g.Assert(n, rdfsLabel, g.CreateLiteralNode(i.Label));
            }
            // 자유 트리플
            foreach (var t in onto.Triples)
            {
                if (string.IsNullOrWhiteSpace(t.Subject) ||
                    string.IsNullOrWhiteSpace(t.Predicate) ||
                    string.IsNullOrWhiteSpace(t.ObjectValue)) continue;
                try
                {
                    var s = MakeNode(g, onto, t.Subject, false);
                    var p = MakeNode(g, onto, t.Predicate, false);
                    var o = MakeNode(g, onto, t.ObjectValue, t.ObjectIsLiteral);
                    g.Assert(s, p, o);
                }
                catch { /* 잘못된 트리플은 무시 */ }
            }
            return g;
        }

        /// <summary>SPARQL 쿼리 실행. SELECT 만 지원 (DataGrid 표시용).</summary>
        public List<SparqlResultRow> ExecuteSelect(TtlOntology onto, string sparql,
                                                    out List<string> columns)
        {
            columns = new List<string>();
            var rows = new List<SparqlResultRow>();
            if (string.IsNullOrWhiteSpace(sparql)) return rows;

            var g = BuildGraph(onto);
            var ds = new VDS.RDF.Query.Datasets.InMemoryDataset(g);
            var processor = new LeviathanQueryProcessor(ds);
            var parser = new SparqlQueryParser();

            // 사용자 쿼리에 PREFIX 가 없으면 자동 추가
            string fullQuery = EnsurePrefixes(sparql, onto);

            var query = parser.ParseFromString(fullQuery);
            var result = processor.ProcessQuery(query);

            if (result is SparqlResultSet rs)
            {
                columns = rs.Variables.ToList();
                foreach (var r in rs)
                {
                    var row = new SparqlResultRow();
                    foreach (var v in columns)
                    {
                        var node = r.HasValue(v) ? r[v] : null;
                        row.Values[v] = NodeToDisplay(node, onto);
                    }
                    rows.Add(row);
                }
            }
            return rows;
        }

        // ── 헬퍼 ────────────────────────────────────────
        private static string EnsurePrefixes(string sparql, TtlOntology onto)
        {
            // 사용자 쿼리에 이미 PREFIX 또는 BASE 가 있으면 그대로 둠
            if (sparql.TrimStart().StartsWith("PREFIX", StringComparison.OrdinalIgnoreCase)
                || sparql.TrimStart().StartsWith("BASE", StringComparison.OrdinalIgnoreCase))
                return sparql;

            return $"PREFIX rdf:  <{RdfNs}>\n"
                 + $"PREFIX rdfs: <{RdfsNs}>\n"
                 + $"PREFIX owl:  <{OwlNs}>\n"
                 + $"PREFIX xsd:  <{XsdNs}>\n"
                 + $"PREFIX {onto.BasePrefix}: <{onto.BaseUri}>\n"
                 + sparql;
        }

        private static string NodeToDisplay(INode? node, TtlOntology onto)
        {
            if (node == null) return "";
            if (node is IUriNode u)
            {
                string iri = u.Uri.ToString();
                if (iri.StartsWith(onto.BaseUri)) return $"{onto.BasePrefix}:{iri.Substring(onto.BaseUri.Length)}";
                if (iri.StartsWith(RdfNs))  return "rdf:"  + iri.Substring(RdfNs.Length);
                if (iri.StartsWith(RdfsNs)) return "rdfs:" + iri.Substring(RdfsNs.Length);
                if (iri.StartsWith(OwlNs))  return "owl:"  + iri.Substring(OwlNs.Length);
                if (iri.StartsWith(XsdNs))  return "xsd:"  + iri.Substring(XsdNs.Length);
                return $"<{iri}>";
            }
            if (node is ILiteralNode l) return $"\"{l.Value}\"";
            return node.ToString();
        }

        private static INode MakeNode(IGraph g, TtlOntology onto, string text, bool asLiteral)
        {
            text = text?.Trim() ?? "";
            if (asLiteral) return g.CreateLiteralNode(text);
            if (text.StartsWith(":")) text = text.Substring(1);
            if (text.Contains(':') && !text.Contains("://"))
            {
                int idx = text.IndexOf(':');
                string prefix = text.Substring(0, idx);
                string local  = text.Substring(idx + 1);
                if (g.NamespaceMap.HasNamespace(prefix))
                    return g.CreateUriNode(new Uri(g.NamespaceMap.GetNamespaceUri(prefix) + local));
            }
            if (text.Contains("://"))
                return g.CreateUriNode(new Uri(text));
            return g.CreateUriNode(new Uri(onto.BaseUri + text));
        }
    }
}
