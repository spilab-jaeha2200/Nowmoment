// ════════════════════════════════════════════════════════════
// TtlIOService.cs — v3.0 F-005 TTL Studio (Step 5.1)
//
// dotNetRDF 의 TurtleParser/TurtleWriter 를 사용해서
// TtlOntology ↔ .ttl 파일 간 변환을 담당.
//
// 직렬화 규칙:
//   - 기본 prefix (spilab:) + 표준 prefix (rdf:, rdfs:, owl:, xsd:)
//   - Classes  → :LocalName a owl:Class ; rdfs:label "..." .
//   - Properties → owl:ObjectProperty / owl:DatatypeProperty + domain/range
//   - Instances  → :LocalName a :ClassOf ; rdfs:label "..." .
//   - Triples    → :s :p :o (혹은 리터럴)
//
// 역직렬화는 위 규칙의 역순 — 단순 RDF 그래프를 4개 컬렉션으로 분류.
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Writing;
using SPILab.NowMoment.Models.Ttl;

namespace SPILab.NowMoment.Services.Ttl
{
    public class TtlIOService
    {
        // 표준 어휘 IRI
        private const string RdfNs   = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string RdfsNs  = "http://www.w3.org/2000/01/rdf-schema#";
        private const string OwlNs   = "http://www.w3.org/2002/07/owl#";
        private const string XsdNs   = "http://www.w3.org/2001/XMLSchema#";

        /// <summary>TtlOntology 를 .ttl 파일로 저장.</summary>
        public void Export(TtlOntology onto, string outputPath)
        {
            if (onto == null) throw new ArgumentNullException(nameof(onto));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("출력 경로가 비어있습니다.", nameof(outputPath));

            var g = new Graph();
            // prefix 등록
            g.NamespaceMap.AddNamespace("rdf",  new Uri(RdfNs));
            g.NamespaceMap.AddNamespace("rdfs", new Uri(RdfsNs));
            g.NamespaceMap.AddNamespace("owl",  new Uri(OwlNs));
            g.NamespaceMap.AddNamespace("xsd",  new Uri(XsdNs));
            g.NamespaceMap.AddNamespace(onto.BasePrefix, new Uri(onto.BaseUri));
            g.BaseUri = new Uri(onto.BaseUri);

            // 자주 쓰는 노드들
            var rdfType  = g.CreateUriNode(new Uri(RdfNs  + "type"));
            var rdfsLabel   = g.CreateUriNode(new Uri(RdfsNs + "label"));
            var rdfsComment = g.CreateUriNode(new Uri(RdfsNs + "comment"));
            var rdfsSubClassOf = g.CreateUriNode(new Uri(RdfsNs + "subClassOf"));
            var rdfsDomain  = g.CreateUriNode(new Uri(RdfsNs + "domain"));
            var rdfsRange   = g.CreateUriNode(new Uri(RdfsNs + "range"));
            var owlClass    = g.CreateUriNode(new Uri(OwlNs  + "Class"));
            var owlObjectProperty   = g.CreateUriNode(new Uri(OwlNs + "ObjectProperty"));
            var owlDatatypeProperty = g.CreateUriNode(new Uri(OwlNs + "DatatypeProperty"));

            // 1) Classes
            foreach (var c in onto.Classes)
            {
                if (string.IsNullOrWhiteSpace(c.LocalName)) continue;
                var node = g.CreateUriNode(new Uri(onto.BaseUri + c.LocalName));
                g.Assert(node, rdfType, owlClass);
                if (!string.IsNullOrWhiteSpace(c.Label))
                    g.Assert(node, rdfsLabel, g.CreateLiteralNode(c.Label, "ko"));
                if (!string.IsNullOrWhiteSpace(c.Comment))
                    g.Assert(node, rdfsComment, g.CreateLiteralNode(c.Comment, "ko"));
                if (!string.IsNullOrWhiteSpace(c.ParentClass))
                {
                    var parent = g.CreateUriNode(new Uri(onto.BaseUri + c.ParentClass));
                    g.Assert(node, rdfsSubClassOf, parent);
                }
            }

            // 2) Properties
            foreach (var p in onto.Properties)
            {
                if (string.IsNullOrWhiteSpace(p.LocalName)) continue;
                var node = g.CreateUriNode(new Uri(onto.BaseUri + p.LocalName));
                g.Assert(node, rdfType,
                    p.Kind == TtlPropertyKind.ObjectProperty ? owlObjectProperty : owlDatatypeProperty);
                if (!string.IsNullOrWhiteSpace(p.Label))
                    g.Assert(node, rdfsLabel, g.CreateLiteralNode(p.Label, "ko"));
                if (!string.IsNullOrWhiteSpace(p.Comment))
                    g.Assert(node, rdfsComment, g.CreateLiteralNode(p.Comment, "ko"));
                if (!string.IsNullOrWhiteSpace(p.Domain))
                {
                    var domain = g.CreateUriNode(new Uri(onto.BaseUri + p.Domain));
                    g.Assert(node, rdfsDomain, domain);
                }
                if (!string.IsNullOrWhiteSpace(p.Range))
                {
                    INode rangeNode;
                    if (p.Kind == TtlPropertyKind.DatatypeProperty &&
                        IsXsdType(p.Range, out var xsdLocal))
                    {
                        rangeNode = g.CreateUriNode(new Uri(XsdNs + xsdLocal));
                    }
                    else
                    {
                        rangeNode = g.CreateUriNode(new Uri(onto.BaseUri + p.Range));
                    }
                    g.Assert(node, rdfsRange, rangeNode);
                }
            }

            // 3) Instances
            foreach (var i in onto.Instances)
            {
                if (string.IsNullOrWhiteSpace(i.LocalName)) continue;
                var node = g.CreateUriNode(new Uri(onto.BaseUri + i.LocalName));
                if (!string.IsNullOrWhiteSpace(i.ClassOf))
                {
                    var cls = g.CreateUriNode(new Uri(onto.BaseUri + i.ClassOf));
                    g.Assert(node, rdfType, cls);
                }
                if (!string.IsNullOrWhiteSpace(i.Label))
                    g.Assert(node, rdfsLabel, g.CreateLiteralNode(i.Label, "ko"));
            }

            // 4) Triples (자유 진술)
            foreach (var t in onto.Triples)
            {
                if (string.IsNullOrWhiteSpace(t.Subject) ||
                    string.IsNullOrWhiteSpace(t.Predicate) ||
                    string.IsNullOrWhiteSpace(t.ObjectValue)) continue;
                var s = MakeNode(g, onto, t.Subject, asLiteral: false);
                var p = MakeNode(g, onto, t.Predicate, asLiteral: false);
                var o = MakeNode(g, onto, t.ObjectValue, asLiteral: t.ObjectIsLiteral);
                g.Assert(s, p, o);
            }

            // 직렬화
            var writer = new CompressingTurtleWriter();
            writer.Save(g, outputPath);
        }

        /// <summary>지정된 .ttl 파일을 읽어 새 TtlOntology 로 반환.</summary>
        public TtlOntology Import(string inputPath)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"TTL 파일을 찾을 수 없습니다: {inputPath}", inputPath);

            var g = new Graph();
            var parser = new TurtleParser();
            parser.Load(g, inputPath);

            var onto = new TtlOntology();
            // base / prefix 자동 추출
            if (g.BaseUri != null)
                onto.BaseUri = g.BaseUri.ToString();

            // ":" 또는 첫 번째 prefix 사용 (rdf/rdfs/owl/xsd 제외)
            var customPrefix = g.NamespaceMap.Prefixes
                .FirstOrDefault(p => p != "rdf" && p != "rdfs" && p != "owl" && p != "xsd");
            if (!string.IsNullOrEmpty(customPrefix))
            {
                onto.BasePrefix = customPrefix;
                onto.BaseUri = g.NamespaceMap.GetNamespaceUri(customPrefix).ToString();
            }

            var rdfTypeIri      = RdfNs + "type";
            var owlClassIri     = OwlNs + "Class";
            var owlObjPropIri   = OwlNs + "ObjectProperty";
            var owlDataPropIri  = OwlNs + "DatatypeProperty";

            var rdfType            = g.CreateUriNode(new Uri(rdfTypeIri));
            var rdfsLabel          = g.CreateUriNode(new Uri(RdfsNs + "label"));
            var rdfsComment        = g.CreateUriNode(new Uri(RdfsNs + "comment"));
            var rdfsSubClassOf     = g.CreateUriNode(new Uri(RdfsNs + "subClassOf"));
            var rdfsDomain         = g.CreateUriNode(new Uri(RdfsNs + "domain"));
            var rdfsRange          = g.CreateUriNode(new Uri(RdfsNs + "range"));

            // 1) type 진술로 Class/Property/Instance 분류
            //    Class/Property/Instance subject 의 IRI 셋을 따로 기록 → 자유 트리플 단계에서 제외할지 여부 판단용
            var classifiedSubjectIris = new HashSet<string>(StringComparer.Ordinal);
            // 자유 트리플 import 단계에서 "이미 Class/Property/Instance 메타데이터로 흡수된 술어"는 제외
            var absorbedPredicateIris = new HashSet<string>(StringComparer.Ordinal)
            {
                rdfTypeIri,
                RdfsNs + "label",
                RdfsNs + "comment",
                RdfsNs + "subClassOf",
                RdfsNs + "domain",
                RdfsNs + "range",
            };

            foreach (var triple in g.GetTriplesWithPredicate(rdfType))
            {
                if (triple.Subject is not IUriNode subj) continue;
                var subjLocal = LocalNameOf(subj.Uri.ToString(), onto.BaseUri);
                if (string.IsNullOrEmpty(subjLocal)) continue;

                // ★ 핵심 수정: IUriNode.Equals 가 환경에 따라 reference 비교가 될 수 있어
                //   IRI 문자열을 직접 비교하는 안전한 방식으로 전환.
                if (triple.Object is not IUriNode objNode) continue;
                string objIri = objNode.Uri.ToString();

                // owl:Class
                if (objIri == owlClassIri)
                {
                    var c = new TtlClass { LocalName = subjLocal };
                    PopulateClass(g, subj, c, onto.BaseUri,
                        rdfsLabel, rdfsComment, rdfsSubClassOf);
                    onto.Classes.Add(c);
                    classifiedSubjectIris.Add(subj.Uri.ToString());
                }
                // owl:ObjectProperty / owl:DatatypeProperty  ← Kind 수입 버그 핵심
                else if (objIri == owlObjPropIri || objIri == owlDataPropIri)
                {
                    var p = new TtlProperty();
                    p.LocalName = subjLocal;
                    p.Kind = (objIri == owlObjPropIri)
                        ? TtlPropertyKind.ObjectProperty
                        : TtlPropertyKind.DatatypeProperty;
                    PopulateProperty(g, subj, p, onto.BaseUri,
                        rdfsLabel, rdfsComment, rdfsDomain, rdfsRange);
                    onto.Properties.Add(p);
                    classifiedSubjectIris.Add(subj.Uri.ToString());
                }
                // 기타 클래스에 대한 인스턴스
                else
                {
                    string clsLocal = LocalNameOf(objIri, onto.BaseUri);
                    // owl:/rdfs:/rdf:/xsd: 표준 어휘는 인스턴스로 안 잡음
                    if (string.IsNullOrEmpty(clsLocal)) continue;
                    if (objIri.StartsWith(RdfNs)
                        || objIri.StartsWith(RdfsNs)
                        || objIri.StartsWith(OwlNs)
                        || objIri.StartsWith(XsdNs))
                        continue;

                    var inst = new TtlInstance { LocalName = subjLocal, ClassOf = clsLocal };
                    var lbl = g.GetTriplesWithSubjectPredicate(subj, rdfsLabel)
                                .Select(x => x.Object).OfType<ILiteralNode>().FirstOrDefault();
                    if (lbl != null) inst.Label = lbl.Value;
                    onto.Instances.Add(inst);
                    classifiedSubjectIris.Add(subj.Uri.ToString());
                }
            }

            // 2) 자유 트리플 import — Class/Property/Instance 메타데이터로 흡수되지 않은 진술
            //    예: spilab:patient_001 spilab:takes spilab:aspirin .
            //        spilab:aspirin     spilab:doseInMg   "100"^^xsd:integer .
            //
            //    제외 규칙:
            //      - Predicate 가 rdf:type / rdfs:label / rdfs:comment / rdfs:subClassOf / rdfs:domain / rdfs:range
            //        → 이미 Class/Property/Instance 의 필드로 흡수됨. 트리플 탭에 중복 표시하지 않음.
            //      - Subject 가 Property 인 경우 (rdfs:domain/range 외에) 도 메타로 본다 — 위 흡수 규칙으로 충분히 걸러짐
            foreach (var t in g.Triples)
            {
                if (t.Predicate is not IUriNode predNode) continue;
                string predIri = predNode.Uri.ToString();
                if (absorbedPredicateIris.Contains(predIri)) continue;

                var triple = new TtlTriple
                {
                    Subject     = NodeToToken(t.Subject,   onto),
                    Predicate   = NodeToToken(t.Predicate, onto),
                };
                if (t.Object is ILiteralNode lit)
                {
                    triple.ObjectValue = lit.Value;
                    triple.ObjectIsLiteral = true;
                }
                else
                {
                    triple.ObjectValue = NodeToToken(t.Object, onto);
                    triple.ObjectIsLiteral = false;
                }
                onto.Triples.Add(triple);
            }

            return onto;
        }

        /// <summary>RDF 노드를 ":Local" 또는 "prefix:Local" 또는 절대 IRI 문자열 토큰으로 변환.
        /// 자유 트리플 import 시 사용 — Subject/Predicate/Object 컬럼에 그대로 표시될 값.</summary>
        private static string NodeToToken(INode node, TtlOntology onto)
        {
            if (node is IUriNode u)
            {
                string iri = u.Uri.ToString();
                if (!string.IsNullOrEmpty(onto.BaseUri) && iri.StartsWith(onto.BaseUri))
                    return iri.Substring(onto.BaseUri.Length);   // ":xxx" 의 xxx 부분만
                if (iri.StartsWith(RdfNs))  return "rdf:"  + iri.Substring(RdfNs.Length);
                if (iri.StartsWith(RdfsNs)) return "rdfs:" + iri.Substring(RdfsNs.Length);
                if (iri.StartsWith(OwlNs))  return "owl:"  + iri.Substring(OwlNs.Length);
                if (iri.StartsWith(XsdNs))  return "xsd:"  + iri.Substring(XsdNs.Length);
                return iri;
            }
            if (node is ILiteralNode l) return l.Value;
            return node.ToString();
        }

        // ── 헬퍼 ────────────────────────────────────────
        private static void PopulateClass(IGraph g, IUriNode subj, TtlClass c, string baseUri,
            INode rdfsLabel, INode rdfsComment, INode rdfsSubClassOf)
        {
            var lbl = g.GetTriplesWithSubjectPredicate(subj, rdfsLabel)
                       .Select(x => x.Object).OfType<ILiteralNode>().FirstOrDefault();
            if (lbl != null) c.Label = lbl.Value;

            var cmt = g.GetTriplesWithSubjectPredicate(subj, rdfsComment)
                       .Select(x => x.Object).OfType<ILiteralNode>().FirstOrDefault();
            if (cmt != null) c.Comment = cmt.Value;

            var parent = g.GetTriplesWithSubjectPredicate(subj, rdfsSubClassOf)
                          .Select(x => x.Object).OfType<IUriNode>().FirstOrDefault();
            if (parent != null) c.ParentClass = LocalNameOf(parent.Uri.ToString(), baseUri);
        }

        private static void PopulateProperty(IGraph g, IUriNode subj, TtlProperty p, string baseUri,
            INode rdfsLabel, INode rdfsComment, INode rdfsDomain, INode rdfsRange)
        {
            var lbl = g.GetTriplesWithSubjectPredicate(subj, rdfsLabel)
                       .Select(x => x.Object).OfType<ILiteralNode>().FirstOrDefault();
            if (lbl != null) p.Label = lbl.Value;

            var cmt = g.GetTriplesWithSubjectPredicate(subj, rdfsComment)
                       .Select(x => x.Object).OfType<ILiteralNode>().FirstOrDefault();
            if (cmt != null) p.Comment = cmt.Value;

            var dom = g.GetTriplesWithSubjectPredicate(subj, rdfsDomain)
                       .Select(x => x.Object).OfType<IUriNode>().FirstOrDefault();
            if (dom != null) p.Domain = LocalNameOf(dom.Uri.ToString(), baseUri);

            var rng = g.GetTriplesWithSubjectPredicate(subj, rdfsRange)
                       .Select(x => x.Object).OfType<IUriNode>().FirstOrDefault();
            if (rng != null)
            {
                var rngStr = rng.Uri.ToString();
                if (rngStr.StartsWith(XsdNs))
                    p.Range = rngStr.Substring(XsdNs.Length);   // string/integer/...
                else
                    p.Range = LocalNameOf(rngStr, baseUri);
            }
        }

        /// <summary>IRI 에서 LocalName 만 추출. baseUri 와 일치하면 그 부분 제거.</summary>
        private static string LocalNameOf(string iri, string baseUri)
        {
            if (string.IsNullOrEmpty(iri)) return "";
            if (!string.IsNullOrEmpty(baseUri) && iri.StartsWith(baseUri))
                return iri.Substring(baseUri.Length);
            int hashIdx = iri.LastIndexOf('#');
            if (hashIdx >= 0 && hashIdx + 1 < iri.Length) return iri.Substring(hashIdx + 1);
            int slashIdx = iri.LastIndexOf('/');
            if (slashIdx >= 0 && slashIdx + 1 < iri.Length) return iri.Substring(slashIdx + 1);
            return iri;
        }

        /// <summary>지정된 문자열이 xsd:* 표준 데이터타입인지.</summary>
        private static bool IsXsdType(string range, out string xsdLocal)
        {
            xsdLocal = "";
            if (string.IsNullOrEmpty(range)) return false;
            string r = range.StartsWith("xsd:") ? range.Substring(4) : range;
            switch (r.ToLowerInvariant())
            {
                case "string":
                case "integer":
                case "int":
                case "double":
                case "float":
                case "boolean":
                case "date":
                case "datetime":
                case "decimal":
                    xsdLocal = r;
                    return true;
            }
            return false;
        }

        /// <summary>트리플의 s/p/o 문자열을 RDF 노드로 변환.
        /// "literal" 표시이면 리터럴, 그 외는 ":xxx" 또는 "prefix:xxx" 또는 절대 IRI.</summary>
        private static INode MakeNode(IGraph g, TtlOntology onto, string text, bool asLiteral)
        {
            text = text?.Trim() ?? "";
            if (asLiteral) return g.CreateLiteralNode(text);

            // ":xxx" 또는 그냥 "xxx"
            if (text.StartsWith(":")) text = text.Substring(1);
            // "prefix:xxx"
            if (text.Contains(':') && !text.Contains("://"))
            {
                int idx = text.IndexOf(':');
                string prefix = text.Substring(0, idx);
                string local  = text.Substring(idx + 1);
                if (g.NamespaceMap.HasNamespace(prefix))
                    return g.CreateUriNode(new Uri(g.NamespaceMap.GetNamespaceUri(prefix) + local));
            }
            // 절대 IRI
            if (text.Contains("://"))
                return g.CreateUriNode(new Uri(text));
            // 기본 — base prefix 사용
            return g.CreateUriNode(new Uri(onto.BaseUri + text));
        }
    }
}
