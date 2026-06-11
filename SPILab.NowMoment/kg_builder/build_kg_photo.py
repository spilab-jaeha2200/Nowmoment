"""
build_kg_photo.py — 레이판 Sim Photo 지식그래프 빌더 (포토리소그래피)
================================================================
입력: PhotoEngineMeta.cs
       (dump_photo_to_csharp.py 가 python_engine/ 폴더에서 자동 생성한 메타데이터 파일)
출력: kg_raypann_photo.json  (NowMoment 임베딩용)
       kg_raypann_photo.ttl   (RDF/Turtle, 외부 KG 도구 연동용)

설계
----
build_kg_cs.py 가 CSPhysicsEngine.cs 를 정규식으로 분석하는 것과 동일한 방식.
PhotoEngineMeta.cs 에는 ast 분석 결과가 다음 형식으로 직렬화되어 있다:

  // Rule N: <module>            ← PhysicsRule 노드
  //   doc: <description>
  //   BELONGS_TO_WORKSPACE: <ws>
  //   USES_MODULE: <other>      ← USES 엣지

  public static readonly List<(string From, string To)> ModuleUses = new()
  {
      ("dof_calculator", "aerial_image"),
      ...
  };

  public static readonly Dictionary<string, ParameterMeta> Parameters = new()
  {
      ["DevelopParams"] = new("develop_simulator", "...", "{fields_json}"),
      ...
  };

  public static readonly Dictionary<string, SpecMeta> Specs = new()
  {
      ["compute_aerial_image_1d"] = new("aerial_image", "..."),
      ...
  };

KG 매핑
-------
  [Workspace]   ws:Photo
  [PhysicsRule] rule:<module>     × 9개 모듈
  [Parameter]   param:<ClassName> × dataclass
  [Spec]        spec:<func_name>  × 핵심 함수
  엣지: BELONGS_TO (Param/Spec→Rule, Rule→Workspace), USES (Rule→Rule)

사용법
------
  python build_kg_photo.py --src "<path>\\PhotoEngineMeta.cs"

지원 환경
---------
  Python 3.10+ (표준 라이브러리만)
"""
from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Set, Any


# ── 노드/엣지 자료구조 ─────────────────────────────────────
class Graph:
    def __init__(self) -> None:
        self.nodes: Dict[str, Dict[str, Any]] = {}
        self.edges: List[Dict[str, Any]] = []
        self._edge_keys: Set[tuple] = set()

    def add_node(self, node_id: str, type_: str, label: str, **props: Any) -> None:
        if node_id in self.nodes:
            return
        self.nodes[node_id] = {
            "id": node_id,
            "type": type_,
            "label": label,
            "props": props,
        }

    def add_edge(self, src: str, dst: str, rel: str, **props: Any) -> None:
        key = (src, dst, rel)
        if key in self._edge_keys:
            return
        self._edge_keys.add(key)
        self.edges.append({"src": src, "dst": dst, "rel": rel, "props": props})


# ── PhotoEngineMeta.cs 파싱 ────────────────────────────────
RE_RULE_HEADER = re.compile(r'//\s*Rule\s+\d+\s*:\s*(\w+)')
RE_RULE_DOC    = re.compile(r'//\s*doc:\s*(.+)')
RE_USES_MODULE = re.compile(r'//\s*USES_MODULE:\s*(\w+)')
RE_BELONGS_WS  = re.compile(r'//\s*BELONGS_TO_WORKSPACE:\s*(\w+)')

RE_WORKSPACE_NAME = re.compile(r'public\s+const\s+string\s+WorkspaceName\s*=\s*"([^"]+)"')
RE_WORKSPACE_DESC = re.compile(r'public\s+const\s+string\s+WorkspaceDescription\s*=\s*"([^"]*)"')

# ModuleUses 항목: ("from", "to"),
RE_MODULE_USE_ITEM = re.compile(r'\("([^"]+)"\s*,\s*"([^"]+)"\)')

# Parameters / Specs 항목:
#   ["Name"] = new("module", "desc", "fields_json"),
#   ["Name"] = new("module", "desc"),
RE_DICT_ITEM_3 = re.compile(
    r'\["([^"]+)"\]\s*=\s*new\(\s*"([^"]*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)'
)
RE_DICT_ITEM_2 = re.compile(
    r'\["([^"]+)"\]\s*=\s*new\(\s*"([^"]*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)'
)

# 섹션 추출용 — Dictionary<...> Parameters = new() { ... };  같은 블록
def _extract_block(text: str, header_pattern: str) -> str:
    """header_pattern 으로 시작하는 블록의 { ... }; 본문을 반환.

    헤더 주석에 같은 패턴이 들어있을 수 있으므로 모든 매칭을 시도하고
    가장 큰 블록(실제 코드 블록)을 반환한다.
    """
    best = ""
    for m in re.finditer(header_pattern, text):
        start = text.find("{", m.end())
        if start < 0:
            continue
        depth = 0
        block = ""
        for i in range(start, len(text)):
            c = text[i]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    block = text[start + 1:i]
                    break
        if len(block) > len(best):
            best = block
    return best


def cs_unescape(s: str) -> str:
    """C# 문자열 escape 해제."""
    return s.replace('\\"', '"').replace("\\\\", "\\")


def parse_meta_cs(cs_text: str) -> Dict[str, Any]:
    """PhotoEngineMeta.cs 본문을 정규식으로 파싱."""

    # 1) Workspace 메타데이터
    ws_name_m = RE_WORKSPACE_NAME.search(cs_text)
    ws_desc_m = RE_WORKSPACE_DESC.search(cs_text)
    ws_name = ws_name_m.group(1) if ws_name_m else "Photo"
    ws_desc = ws_desc_m.group(1) if ws_desc_m else ""

    # 2) PhysicsRule (모듈) — // Rule N: <name> 헤더 + 그 아래 doc 라인
    modules: List[Dict[str, Any]] = []
    lines = cs_text.split("\n")
    i = 0
    while i < len(lines):
        rh = RE_RULE_HEADER.search(lines[i])
        if rh:
            mod_name = rh.group(1)
            mod = {"module": mod_name, "doc": "", "uses": []}
            # 다음 몇 줄 안에 doc / USES_MODULE 가 있는지 스캔
            j = i + 1
            while j < len(lines) and j < i + 30:
                line = lines[j]
                if not line.strip().startswith("//"):
                    # 주석 블록 종료
                    if line.strip() == "":
                        j += 1
                        continue
                    break
                m_doc = RE_RULE_DOC.search(line)
                if m_doc and not mod["doc"]:
                    mod["doc"] = m_doc.group(1).strip()
                m_use = RE_USES_MODULE.search(line)
                if m_use:
                    mod["uses"].append(m_use.group(1))
                # 다음 Rule 헤더가 나오면 종료
                if RE_RULE_HEADER.search(line):
                    break
                j += 1
            modules.append(mod)
            i = j
        else:
            i += 1

    # 3) ModuleUses 블록 (확정 USES — 헤더 주석과 중복되지만 더 정확)
    uses_block = _extract_block(
        cs_text, r'List<\(string\s+From,\s*string\s+To\)>\s+ModuleUses\s*=\s*new\(\)')
    module_uses = []
    if uses_block:
        for m in RE_MODULE_USE_ITEM.finditer(uses_block):
            module_uses.append((m.group(1), m.group(2)))

    # 4) Parameters 블록 — ["Name"] = new("module", "desc", "fields_json")
    params_block = _extract_block(
        cs_text, r'Dictionary<string,\s*ParameterMeta>\s+Parameters\s*=\s*new\(\)')
    parameters: List[Dict[str, Any]] = []
    if params_block:
        for m in RE_DICT_ITEM_3.finditer(params_block):
            name = m.group(1)
            module = m.group(2)
            desc = cs_unescape(m.group(3))
            fields_json_raw = cs_unescape(m.group(4))
            try:
                fields = json.loads(fields_json_raw) if fields_json_raw else {}
            except json.JSONDecodeError:
                fields = {}
            parameters.append({
                "name": name, "module": module, "doc": desc, "fields": fields,
            })

    # 5) Specs 블록 — ["Name"] = new("module", "desc")
    specs_block = _extract_block(
        cs_text, r'Dictionary<string,\s*SpecMeta>\s+Specs\s*=\s*new\(\)')
    specs: List[Dict[str, Any]] = []
    if specs_block:
        for m in RE_DICT_ITEM_2.finditer(specs_block):
            specs.append({
                "name": m.group(1),
                "module": m.group(2),
                "doc": cs_unescape(m.group(3)),
            })

    return {
        "workspace_name": ws_name,
        "workspace_desc": ws_desc,
        "modules": modules,
        "module_uses": module_uses,
        "parameters": parameters,
        "specs": specs,
    }


# ── 그래프 빌드 (build_kg_cs.py 와 동일 매핑) ───────────────
def build_graph(meta: Dict[str, Any]) -> Graph:
    g = Graph()
    ws_id = f"ws:{meta['workspace_name']}"
    g.add_node(ws_id, "Workspace", f"{meta['workspace_name']} 워크스페이스",
               description=meta["workspace_desc"])

    module_names = {m["module"] for m in meta["modules"]}
    for m in meta["modules"]:
        rid = f"rule:{m['module']}"
        g.add_node(rid, "PhysicsRule", m["module"],
                   description=m["doc"] or m["module"])
        g.add_edge(rid, ws_id, "BELONGS_TO")

    # USES 엣지 (ModuleUses 블록 기준 — 중복 add_edge 가 자동으로 dedupe)
    for src, dst in meta["module_uses"]:
        if src in module_names and dst in module_names:
            g.add_edge(f"rule:{src}", f"rule:{dst}", "USES")

    # Parameter 노드
    for p in meta["parameters"]:
        pid = f"param:{p['name']}"
        g.add_node(pid, "Parameter", p["name"],
                   description=p["doc"], fields=p["fields"])
        if p["module"] in module_names:
            g.add_edge(pid, f"rule:{p['module']}", "BELONGS_TO")

    # Spec 노드
    seen_spec: Set[str] = set()
    for s in meta["specs"]:
        sid = f"spec:{s['name']}"
        if sid in seen_spec:
            sid = f"spec:{s['module']}.{s['name']}"
        seen_spec.add(sid)
        g.add_node(sid, "Spec", s["name"],
                   description=s["doc"], module=s["module"])
        if s["module"] in module_names:
            g.add_edge(sid, f"rule:{s['module']}", "BELONGS_TO")

    return g


# ── 출력 ──────────────────────────────────────────────────
def write_json(g: Graph, path: Path) -> None:
    data = {
        "@context": {
            "@vocab": "http://spilab.ai/kg/raypann_photo/",
            "id": "@id", "type": "@type",
        },
        "domain": "raypann_photo",
        "nodes": list(g.nodes.values()),
        "edges": g.edges,
    }
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def write_ttl(g: Graph, path: Path) -> None:
    lines = [
        "@prefix kg:  <http://spilab.ai/kg/raypann_photo/> .",
        "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .",
        "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .",
        "",
    ]
    for n in g.nodes.values():
        nid = n["id"].replace(":", "_")
        label = n["label"].replace('"', '\\"')
        lines.append(f"kg:{nid} a kg:{n['type']} ;")
        lines.append(f'    rdfs:label "{label}" .')
        lines.append("")
    for e in g.edges:
        s = e["src"].replace(":", "_")
        d = e["dst"].replace(":", "_")
        lines.append(f"kg:{s} kg:{e['rel']} kg:{d} .")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


# ── main ───────────────────────────────────────────────────
def main() -> None:
    p = argparse.ArgumentParser(
        description="Build KG from PhotoEngineMeta.cs (auto-generated by dump_photo_to_csharp.py)")
    p.add_argument("--src", required=True, help="PhotoEngineMeta.cs 절대경로")
    p.add_argument("--out-json", default=None,
                   help="출력 JSON (기본: <스크립트폴더>/kg_raypann_photo.json)")
    p.add_argument("--out-ttl", default=None,
                   help="출력 TTL (기본: <스크립트폴더>/kg_raypann_photo.ttl)")
    args = p.parse_args()

    src = Path(args.src).resolve()
    if not src.exists() or not src.is_file():
        print(f"[ERR] --src 파일이 없거나 파일이 아닙니다: {src}", file=sys.stderr)
        sys.exit(1)

    script_dir = Path(__file__).resolve().parent
    out_json = Path(args.out_json) if args.out_json else (script_dir / "kg_raypann_photo.json")
    out_ttl  = Path(args.out_ttl)  if args.out_ttl  else (script_dir / "kg_raypann_photo.ttl")

    cs_text = src.read_text(encoding="utf-8")
    meta = parse_meta_cs(cs_text)
    print(f"[INFO] PhotoEngineMeta.cs 파싱 완료:")
    print(f"  - workspace: {meta['workspace_name']}")
    print(f"  - modules:   {len(meta['modules'])}")
    print(f"  - uses:      {len(meta['module_uses'])}")
    print(f"  - params:    {len(meta['parameters'])}")
    print(f"  - specs:     {len(meta['specs'])}")

    g = build_graph(meta)

    out_json.parent.mkdir(parents=True, exist_ok=True)
    write_json(g, out_json)
    write_ttl(g, out_ttl)

    by_type: Dict[str, int] = {}
    for n in g.nodes.values():
        by_type[n["type"]] = by_type.get(n["type"], 0) + 1
    by_rel: Dict[str, int] = {}
    for e in g.edges:
        by_rel[e["rel"]] = by_rel.get(e["rel"], 0) + 1

    print()
    print(f"[OK] nodes={len(g.nodes)}  edges={len(g.edges)}")
    print(f"     by type: {dict(sorted(by_type.items()))}")
    print(f"     by rel : {dict(sorted(by_rel.items()))}")
    print(f"     -> {out_json}")
    print(f"     -> {out_ttl}")


if __name__ == "__main__":
    main()
