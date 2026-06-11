"""
build_kg_cs.py — 레이판 Sim CS Edition 지식그래프 빌더 (GaN/SiC 반도체)
================================================================
입력: 레이판 Sim CS 의 CSPhysicsEngine.cs (단일 C# 파일)
출력: kg_raypann_cs.json  (NowMoment 임베딩용)
       kg_raypann_cs.ttl   (RDF/Turtle, 외부 KG와 연동용 옵션)

설계 원칙
----------
1. 정적 분석 only (실행 의존 없음): C# 파일을 정규식/AST 기반으로 파싱
2. 노드 5종: PhysicsRule, Material, Workspace, Parameter, Spec
3. 엣지 6종: USES, GOVERNS, DERIVES_FROM, CITES, BELONGS_TO, REQUIRES
4. 결정적 ID: prefix + sluggified key  → 재실행 시 ID 안정성 보장
5. JSON-LD 호환: @id, @type 필드를 함께 발행

작성: SPILab Corp / NowMoment Integration Track
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Any


# ────────────────────────────────────────────────────────────────
# 데이터 모델
# ────────────────────────────────────────────────────────────────
@dataclass
class Node:
    id: str
    type: str               # PhysicsRule | Material | Workspace | Parameter | Spec
    label: str
    props: dict[str, Any] = field(default_factory=dict)


@dataclass
class Edge:
    src: str
    dst: str
    rel: str                # USES | GOVERNS | DERIVES_FROM | CITES | BELONGS_TO | REQUIRES
    props: dict[str, Any] = field(default_factory=dict)


# ────────────────────────────────────────────────────────────────
# 메타데이터 — 23 룰 정의 (소스의 주석/Loc.cs 와 정합)
#   각 룰 키: id, label_ko, label_en, formula, citation, severity_default
# ────────────────────────────────────────────────────────────────
RULES: list[dict[str, Any]] = [
    # W1·W2: Material + MOCVD
    dict(id="R1",  ws="W2", name_ko="Arrhenius 성장률",     name_en="Arrhenius growth rate",
         formula="GR = A0·exp(-Ea/kBT)·√(P/76)·min(2.5, TMGa/50)",
         citation="Arrhenius (1889)", severity="warning",
         params=["TempC", "TmgaFlow", "Pressure"]),
    dict(id="R2",  ws="W2", name_ko="V/III 비율 효율",      name_en="V/III ratio efficiency",
         formula="η_V3 = exp(-0.5·((V3-2000)/800)^2)",
         citation="Stringfellow (1999)", severity="warning",
         params=["V3Ratio"]),
    dict(id="R3",  ws="W1", name_ko="Vegard 격자상수",       name_en="Vegard lattice constant",
         formula="a(x) = (1-x)·aA + x·aB",
         citation="Vegard (1921)", severity="info",
         params=["AlFraction", "InFraction"]),
    dict(id="R4",  ws="W1", name_ko="Bowing 밴드갭",         name_en="Bowing bandgap",
         formula="Eg(x) = (1-x)·EgA + x·EgB - b·x·(1-x)",
         citation="Vurgaftman (2003)", severity="info",
         params=["AlFraction", "InFraction"]),
    dict(id="R5",  ws="W1", name_ko="Varshni T 의존성",      name_en="Varshni T-dependence",
         formula="Eg(T) = Eg0 - α·T²/(T+β)",
         citation="Varshni (1967) Physica 34", severity="info",
         params=["TempK"]),
    dict(id="R6",  ws="W2", name_ko="Matthews-Blakeslee 임계두께", name_en="Critical thickness",
         formula="hc = a/(8π·f)·(1-ν/4)·ln(1/f)",
         citation="Matthews & Blakeslee (1974)", severity="critical",
         params=["AlFraction", "BarrierThick"]),
    dict(id="R7",  ws="W2", name_ko="In 재증발 효과",         name_en="In re-evaporation",
         formula="η_re = 1 - exp(-Ea_re/kBT)",
         citation="Piner et al. (1997)", severity="warning",
         params=["TempC", "InFraction"]),
    dict(id="R8",  ws="W2", name_ko="BCF 표면 이동도",        name_en="BCF surface mobility",
         formula="Rq = 0.08·(GR/80)^0.3 / √(T/1323)",
         citation="Burton-Cabrera-Frank (1951)", severity="info",
         params=["TempC"]),

    # W3: HEMT
    dict(id="R9",  ws="W3", name_ko="2DEG 분극 전하",        name_en="2DEG polarization charge",
         formula="ns = σ·d/q,  σ = |Ppz + ΔPsp|",
         citation="Ambacher (1999) J.Phys.D 32", severity="info",
         params=["AlFraction", "BarrierThick"]),
    dict(id="R10", ws="W3", name_ko="문턱전압 Vth",           name_en="Threshold voltage Vth",
         formula="Vth = φb - σ·d/ε - ΔEc",
         citation="Ambacher (1999)", severity="info",
         params=["AlFraction", "BarrierThick"]),
    dict(id="R11", ws="W3", name_ko="속도포화 모델 Id",       name_en="Velocity saturation Id",
         formula="Id = q·ns·vsat·W",
         citation="Shockley (1949)", severity="info",
         params=["AlFraction", "BarrierThick"]),
    dict(id="R12", ws="W3", name_ko="차단주파수 fT",          name_en="Cutoff frequency fT",
         formula="fT = vsat / (2π·Lg)",
         citation="Tasker & Hughes (1989)", severity="info",
         params=["GateLength"]),
    dict(id="R13", ws="W3", name_ko="Baliga 항복전압",         name_en="Baliga breakdown voltage",
         formula="Vbr ≈ 0.4·Ec·(0.6 + 0.4x)·Lsd",
         citation="Baliga (2008)", severity="info",
         params=["AlFraction", "SdSpacing"]),
    dict(id="R14", ws="W3", name_ko="온저항 Ron",              name_en="On-resistance Ron",
         formula="Ron = 1 / (q·ns·μ_eff)",
         citation="Baliga (2008)", severity="info",
         params=["AlFraction", "BarrierThick"]),

    # W4: Measurement (15~23)
    dict(id="R15", ws="W4", name_ko="Shockley 다이오드",       name_en="Shockley diode",
         formula="I = I0·(exp(qV/nkT) - 1)",
         citation="Shockley (1949)", severity="info",
         params=["TempK", "Nd"]),
    dict(id="R16", ws="W4", name_ko="쇼트키 장벽",              name_en="Schottky barrier",
         formula="I0 = A·A*·T²·exp(-φb/kT)",
         citation="Sze (2006)", severity="info",
         params=["TempK", "AreaCm2"]),
    dict(id="R17", ws="W4", name_ko="C-V 1/C² 추출",           name_en="C-V 1/C² extraction",
         formula="1/C² = 2(Vbi-V) / (qεNd)",
         citation="Schroder (2005)", severity="info",
         params=["Nd", "AreaCm2"]),
    dict(id="R18", ws="W4", name_ko="공핍 영역폭",              name_en="Depletion width",
         formula="W = √(2εφb/(qNd))",
         citation="Sze (2006)", severity="info",
         params=["Nd"]),
    dict(id="R19", ws="W4", name_ko="Matthiessen 법칙",         name_en="Matthiessen's rule",
         formula="1/μ = 1/μac + 1/μpo + 1/μii + 1/μdis",
         citation="Matthiessen (1864)", severity="info",
         params=["TempK", "Nd", "DisDensity"]),
    dict(id="R20", ws="W4", name_ko="홀 이동도 온도의존",        name_en="Hall mobility T-dependence",
         formula="μ_ac ∝ T^(-1.5),  μ_po ∝ exp(ELO/kT)",
         citation="Look (1989)", severity="info",
         params=["TempK"]),
    dict(id="R21", ws="W4", name_ko="PL Near-band-edge",         name_en="PL near-band-edge",
         formula="E_NBE = Eg - 0.012",
         citation="Reshchikov (2005)", severity="info",
         params=["TempK"]),
    dict(id="R22", ws="W4", name_ko="Yellow Luminescence ratio",  name_en="Yellow Luminescence ratio",
         formula="YL/NBE = 0.02·(Nd_disloc/1e8)^0.6",
         citation="Reshchikov (2005)", severity="info",
         params=["DisDensity"]),
    dict(id="R23", ws="W4", name_ko="IQE 추정",                   name_en="Internal quantum efficiency",
         formula="IQE = (1 - 0.8·YL/NBE - 0.001·(DD/1e8)^0.5)",
         citation="Karpov (2010)", severity="info",
         params=["DisDensity"]),
]


# 워크스페이스 메타
WORKSPACES: list[dict[str, str]] = [
    dict(id="W1", name_ko="재료 워크스페이스",     name_en="Material workspace",
         desc="Vegard, Bowing, Varshni 결정 — 합금 조성/온도에 따른 격자/밴드갭"),
    dict(id="W2", name_ko="MOCVD 워크스페이스",   name_en="MOCVD workspace",
         desc="Arrhenius 성장률, 임계두께, In 재증발, BCF 표면 거칠기"),
    dict(id="W3", name_ko="HEMT 워크스페이스",    name_en="HEMT workspace",
         desc="2DEG 분극, Vth, Id, fT/fmax, Baliga Vbr, Ron"),
    dict(id="W4", name_ko="측정 워크스페이스",     name_en="Measurement workspace",
         desc="I-V Shockley/Schottky, C-V Nd 추출, Matthiessen 이동도, PL/IQE"),
    dict(id="W5", name_ko="통합 워크스페이스",     name_en="Integrated workspace",
         desc="W1~W4 인과체인 + 5G 스펙 점수 + Grade 산출"),
]


# 5G/전력소자 스펙 (W5의 Spec 노드)
SPECS: list[dict[str, Any]] = [
    dict(id="SPEC_FT",  name="fT ≥ 40 GHz",       metric="fT",   threshold=40,  unit="GHz",   domain="5G/RF"),
    dict(id="SPEC_VBR", name="Vbr ≥ 200 V",       metric="Vbr",  threshold=200, unit="V",     domain="Power"),
    dict(id="SPEC_ID",  name="Id ≥ 500 mA/mm",    metric="Id",   threshold=500, unit="mA/mm", domain="Power"),
    dict(id="SPEC_PAE", name="PAE ≥ 35 %",        metric="PAE",  threshold=35,  unit="%",     domain="5G/RF"),
    dict(id="SPEC_MU",  name="μ_eff ≥ 1500 cm²/Vs", metric="MuEff", threshold=1500, unit="cm²/Vs", domain="Power"),
]


# 슬러그 헬퍼
def slug(s: str) -> str:
    return re.sub(r"[^a-zA-Z0-9]+", "_", s).strip("_")


# ────────────────────────────────────────────────────────────────
# 정적 분석: C# 소스에서 재료 DB / 함수 호출 / 인용 추출
# ────────────────────────────────────────────────────────────────
RE_MAT_ROW = re.compile(
    r'\["(?P<name>[^"]+)"\]\s*=\s*new\s*\(\s*'
    r'(?P<eg0>[\-\d\.eE]+)\s*,\s*'
    r'(?P<alpha>[\-\d\.eE]+)\s*,\s*'
    r'(?P<beta>[\-\d\.eE]+)\s*,\s*'
    r'(?P<a>[\-\d\.eE]+)\s*,\s*'
    r'(?P<c>[\-\d\.eE]+)\s*,\s*'
    r'(?P<mu>[\-\d\.eE]+)\s*,\s*'
    r'(?P<kappa>[\-\d\.eE]+)\s*,\s*'
    r'(?P<epsr>[\-\d\.eE]+)'
)
RE_BOWING = re.compile(
    r'\[\("(?P<a>[A-Za-z]+)","(?P<b>[A-Za-z]+)"\)\]\s*=\s*(?P<v>[\-\d\.eE]+)'
)
RE_RULE_TAG = re.compile(r'Rule\s+(\d+)')   # 주석 내 Rule 표기


def parse_engine_source(src: str) -> dict[str, Any]:
    """CSPhysicsEngine.cs 1차 파싱 결과를 dict 로 반환"""
    materials: dict[str, dict[str, float]] = {}
    for m in RE_MAT_ROW.finditer(src):
        name = m.group("name")
        materials[name] = dict(
            Eg0_eV=float(m.group("eg0")),
            Varshni_alpha=float(m.group("alpha")),
            Varshni_beta=float(m.group("beta")),
            lattice_a=float(m.group("a")),
            lattice_c=float(m.group("c")),
            mobility=float(m.group("mu")),
            thermal_k=float(m.group("kappa")),
            eps_r=float(m.group("epsr")),
        )

    bowing: list[dict[str, Any]] = []
    seen = set()
    for m in RE_BOWING.finditer(src):
        a, b, v = m.group("a"), m.group("b"), float(m.group("v"))
        key = tuple(sorted((a, b)))
        if key in seen:
            continue
        seen.add(key)
        bowing.append(dict(a=a, b=b, b_eV=v))

    # 코드 내 Rule N 언급 — 어떤 함수가 어떤 룰을 구현하는지 단서
    rule_mentions: list[int] = sorted({int(x) for x in RE_RULE_TAG.findall(src)})

    return dict(materials=materials, bowing=bowing, rule_mentions=rule_mentions)


# ────────────────────────────────────────────────────────────────
# 그래프 빌더
# ────────────────────────────────────────────────────────────────
def build_graph(engine_path: Path) -> tuple[list[Node], list[Edge], dict[str, Any]]:
    src = engine_path.read_text(encoding="utf-8")
    parsed = parse_engine_source(src)

    nodes: list[Node] = []
    edges: list[Edge] = []

    # 1) Workspace 노드
    for ws in WORKSPACES:
        nodes.append(Node(
            id=f"ws:{ws['id']}", type="Workspace", label=ws["name_ko"],
            props=dict(name_en=ws["name_en"], description=ws["desc"]),
        ))

    # 2) Material 노드 (소스 추출)
    for name, props in parsed["materials"].items():
        nodes.append(Node(
            id=f"mat:{slug(name)}", type="Material", label=name,
            props=props,
        ))
    # bowing 파라미터는 Material → Material 의 DERIVES_FROM 엣지로
    for bow in parsed["bowing"]:
        edges.append(Edge(
            src=f"mat:{slug(bow['a'])}", dst=f"mat:{slug(bow['b'])}",
            rel="DERIVES_FROM",
            props=dict(via="Bowing", b_eV=bow["b_eV"]),
        ))

    # 3) PhysicsRule 노드 + Workspace 귀속 + 인용
    param_seen: set[str] = set()
    for r in RULES:
        nodes.append(Node(
            id=f"rule:{r['id']}", type="PhysicsRule", label=r["name_ko"],
            props=dict(
                name_en=r["name_en"], formula=r["formula"],
                citation=r["citation"], severity=r["severity"], workspace=r["ws"],
            ),
        ))
        # BELONGS_TO Workspace
        edges.append(Edge(src=f"rule:{r['id']}", dst=f"ws:{r['ws']}", rel="BELONGS_TO"))
        # CITES (텍스트로만)
        edges.append(Edge(
            src=f"rule:{r['id']}", dst=f"cit:{slug(r['citation'])[:40]}",
            rel="CITES", props=dict(text=r["citation"]),
        ))
        # USES Parameter (Parameter 노드는 첫 등장에만 추가)
        for p in r["params"]:
            pid = f"par:{p}"
            if pid not in param_seen:
                nodes.append(Node(
                    id=pid, type="Parameter", label=p,
                    props=dict(unit=_param_unit(p)),
                ))
                param_seen.add(pid)
            edges.append(Edge(src=f"rule:{r['id']}", dst=pid, rel="USES"))

    # 4) Spec 노드 + REQUIRES (어떤 룰이 어떤 spec을 충족시키는지)
    for s in SPECS:
        nodes.append(Node(
            id=f"spec:{s['id']}", type="Spec", label=s["name"],
            props=dict(metric=s["metric"], threshold=s["threshold"],
                       unit=s["unit"], domain=s["domain"]),
        ))
    # 명시 매핑 (룰 ↔ spec)
    rule_spec_map = [
        ("R12", "SPEC_FT"),    # fT
        ("R13", "SPEC_VBR"),   # Vbr
        ("R11", "SPEC_ID"),    # Id
        ("R14", "SPEC_MU"),    # Ron via μ_eff
        ("R9",  "SPEC_PAE"),   # 2DEG → PAE 연계
    ]
    for rid, sid in rule_spec_map:
        edges.append(Edge(src=f"rule:{rid}", dst=f"spec:{sid}", rel="GOVERNS"))

    # 5) Material → Rule (어떤 룰이 어느 재료에 직접 적용되는지)
    #    GaN/AlN 의 polarization → R9, R10
    for rid in ("R9", "R10", "R3", "R4", "R6"):
        for mat in ("GaN", "AlN"):
            if f"mat:{mat}" in {n.id for n in nodes}:
                edges.append(Edge(src=f"rule:{rid}", dst=f"mat:{mat}", rel="USES"))

    # 6) 통합 워크스페이스 인과 체인 — W5 → W1/W2/W3/W4
    for ws in ("W1", "W2", "W3", "W4"):
        edges.append(Edge(src="ws:W5", dst=f"ws:{ws}", rel="DERIVES_FROM"))

    stats = dict(
        nodes_total=len(nodes),
        edges_total=len(edges),
        nodes_by_type=_count_by(nodes, lambda n: n.type),
        edges_by_rel=_count_by(edges, lambda e: e.rel),
        materials_extracted=list(parsed["materials"].keys()),
        bowing_pairs=len(parsed["bowing"]),
        rule_mentions_in_source=parsed["rule_mentions"],
    )
    return nodes, edges, stats


def _param_unit(p: str) -> str:
    return {
        "TempC": "°C", "TempK": "K", "V3Ratio": "ratio",
        "AlFraction": "fraction", "InFraction": "fraction",
        "TmgaFlow": "sccm", "Pressure": "Torr",
        "BarrierThick": "nm", "GateLength": "μm", "SdSpacing": "μm",
        "Nd": "cm⁻³", "AreaCm2": "cm²", "DisDensity": "cm⁻²",
    }.get(p, "")


def _count_by(items, key) -> dict[str, int]:
    out: dict[str, int] = {}
    for it in items:
        k = key(it)
        out[k] = out.get(k, 0) + 1
    return out


# ────────────────────────────────────────────────────────────────
# 시리얼라이저
# ────────────────────────────────────────────────────────────────
def to_jsonld(nodes: list[Node], edges: list[Edge], stats: dict[str, Any]) -> dict[str, Any]:
    return dict(
        **{"@context": {
            "@vocab":  "https://spilab.ai/ns/raypann-cs#",
            "rdfs":    "http://www.w3.org/2000/01/rdf-schema#",
        }},
        meta=dict(
            generator="build_kg.py v1.0",
            source="RaypannSimCS_v2 / CSPhysicsEngine.cs",
            stats=stats,
        ),
        nodes=[asdict(n) for n in nodes],
        edges=[asdict(e) for e in edges],
    )


def to_turtle(nodes: list[Node], edges: list[Edge]) -> str:
    PFX = "@prefix raypann: <https://spilab.ai/ns/raypann-cs#> .\n"
    PFX += "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n\n"
    lines = [PFX]

    def lit(v: Any) -> str:
        if isinstance(v, (int, float)):
            return str(v)
        s = str(v).replace("\\", "\\\\").replace('"', '\\"')
        return f'"{s}"'

    for n in nodes:
        lines.append(f"raypann:{n.id.replace(':','_')} a raypann:{n.type} ;")
        lines.append(f'    rdfs:label {lit(n.label)} ;')
        kvs = list(n.props.items())
        for i, (k, v) in enumerate(kvs):
            sep = " ;" if i < len(kvs) - 1 else " ."
            lines.append(f'    raypann:{k} {lit(v)}{sep}')
        if not kvs:
            lines[-1] = lines[-1].rstrip(";") + "."
        lines.append("")

    for e in edges:
        s = e.src.replace(":", "_")
        d = e.dst.replace(":", "_")
        lines.append(f"raypann:{s} raypann:{e.rel} raypann:{d} .")
    return "\n".join(lines)


# ────────────────────────────────────────────────────────────────
# 엔트리
# ────────────────────────────────────────────────────────────────
def main(argv: list[str]) -> int:
    # 두 가지 호출 방식 지원
    #   (A) 옵션 방식: python build_kg.py --src <cs> [--out-json <json>] [--out-ttl <ttl>]
    #   (B) 구식 위치 인자: python build_kg.py <cs> <out_dir>
    #
    # 기본 출력 경로 정책 (옵션 방식 한정):
    #   --out-json / --out-ttl 미지정 시 build_kg.py 가 위치한 폴더에
    #   kg_raypann_cs.json / kg_raypann_cs.ttl 을 생성한다.
    args = argv[1:]
    use_legacy = bool(args) and not any(a.startswith("-") for a in args)

    # 스크립트가 위치한 폴더 = 기본 출력 폴더
    script_dir = Path(__file__).resolve().parent
    default_json = script_dir / "kg_raypann_cs.json"
    default_ttl  = script_dir / "kg_raypann_cs.ttl"

    if use_legacy:
        if len(args) < 2:
            print("usage: python build_kg.py <CSPhysicsEngine.cs path> <output dir>")
            return 1
        engine_path = Path(args[0])
        out_dir = Path(args[1])
        if out_dir.exists() and not out_dir.is_dir():
            print(f"[ERROR] output dir path is not a directory: {out_dir}")
            return 2
        out_dir.mkdir(parents=True, exist_ok=True)
        json_path = out_dir / "kg_raypann_cs.json"
        ttl_path = out_dir / "kg_raypann_cs.ttl"
    else:
        parser = argparse.ArgumentParser(
            prog="build_kg.py",
            description="레이판 Sim CS 지식그래프 빌더 "
                        "(기본 출력 위치: build_kg.py 와 같은 폴더)",
        )
        parser.add_argument("--src", required=True,
                            help="CSPhysicsEngine.cs 파일 경로")
        parser.add_argument("--out-json", default=str(default_json),
                            help=f"출력 JSON-LD 파일 경로 (기본: {default_json})")
        parser.add_argument("--out-ttl", default=str(default_ttl),
                            help=f"출력 Turtle(.ttl) 파일 경로 (기본: {default_ttl})")
        ns = parser.parse_args(args)

        engine_path = Path(ns.src)
        json_path = Path(ns.out_json)
        ttl_path = Path(ns.out_ttl)

        # 출력 파일들의 부모 디렉토리만 생성 (파일 자체에 mkdir 하지 않음)
        for p in (json_path, ttl_path):
            parent = p.parent
            if str(parent) and not parent.exists():
                parent.mkdir(parents=True, exist_ok=True)

    if not engine_path.exists():
        print(f"[ERROR] source file not found: {engine_path}")
        return 3
    if not engine_path.is_file():
        print(f"[ERROR] source path is not a file: {engine_path}")
        return 3

    nodes, edges, stats = build_graph(engine_path)

    json_path.write_text(
        json.dumps(to_jsonld(nodes, edges, stats), ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    ttl_path.write_text(to_turtle(nodes, edges), encoding="utf-8")

    print(f"[OK] nodes={stats['nodes_total']}  edges={stats['edges_total']}")
    print(f"     by type: {stats['nodes_by_type']}")
    print(f"     by rel : {stats['edges_by_rel']}")
    print(f"     -> {json_path}")
    print(f"     -> {ttl_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
