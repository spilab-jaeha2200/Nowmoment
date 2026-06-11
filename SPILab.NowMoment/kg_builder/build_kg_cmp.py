"""
build_kg_cmp.py — 레이판 Sim CMP Edition 지식그래프 빌더 (Chemical Mechanical Polishing)
================================================================
입력: 레이판 Sim CMP 의 CmpPhysicsEngine.cs (단일 C# 파일)
출력: kg_raypann_cmp.json  (NowMoment 임베딩용)
       kg_raypann_cmp.ttl   (RDF/Turtle, 외부 KG와 연동용 옵션)

설계 원칙 (build_kg_cs.py 와 동일)
----------
1. 정적 분석 only (실행 의존 없음): C# 파일을 정규식 기반으로 파싱
2. 노드 5종: PhysicsRule, ProcessParam, Workspace, Parameter, Spec
3. 엣지 6종: USES, GOVERNS, DERIVES_FROM, CITES, BELONGS_TO, REQUIRES
4. 결정적 ID: prefix + sluggified key  → 재실행 시 ID 안정성 보장
5. JSON-LD 호환: @id, @type 필드를 함께 발행

CS Edition 과의 도메인 차이
----------
- Material DB → ProcessParam DB (12 공정 파라미터: wafer_pressure, slurry_ph, ...)
- 23 Rules (R1~R23) → 40 Rules (CM1~CM40)
- 5 Workspaces (W1~W5) → 5 Workspaces (W1~W5, 카테고리 기반)
- 5 Specs (5G/Power) → 5 Specs (CMP 공정 KPI)

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
# 데이터 모델 (build_kg_cs.py 와 동일)
# ────────────────────────────────────────────────────────────────
@dataclass
class Node:
    id: str
    type: str               # PhysicsRule | ProcessParam | Workspace | Parameter | Spec
    label: str
    props: dict[str, Any] = field(default_factory=dict)


@dataclass
class Edge:
    src: str
    dst: str
    rel: str                # USES | GOVERNS | DERIVES_FROM | CITES | BELONGS_TO | REQUIRES
    props: dict[str, Any] = field(default_factory=dict)


# ────────────────────────────────────────────────────────────────
# 메타데이터 — 40 룰 정의 (CmpPhysicsEngine.cs 의 BuildRules() 와 정합)
#   각 룰 키: id, ws, name_ko, name_en, formula, citation, severity, params
# ────────────────────────────────────────────────────────────────
RULES: list[dict[str, Any]] = [
    # W1: Pressure & Speed (CM1~CM10) — Preston 방정식 + 속도
    dict(id="CM1",  ws="W1", name_ko="스크래치 위험 (고압)",          name_en="Scratch risk (high pressure)",
         formula="P > 5 psi → MR = Kp·P·V·(1+α(P-Pth))",
         citation="Preston (1927) J.Soc.Glass.Tech 11", severity="critical",
         params=["wafer_pressure"]),
    dict(id="CM2",  ws="W1", name_ko="MRR 부족 (저압)",                name_en="MRR shortage (low pressure)",
         formula="P < 1 psi → MRR < target",
         citation="Preston (1927)", severity="warning",
         params=["wafer_pressure"]),
    dict(id="CM3",  ws="W1", name_ko="Dishing 위험 (저압+넓은 패턴)",  name_en="Dishing (low P + wide pattern)",
         formula="P < 1.5 ∧ ρpattern < 30 % → dishing",
         citation="Cook (1990)", severity="critical",
         params=["wafer_pressure", "pattern_density"]),
    dict(id="CM4",  ws="W1", name_ko="Preston 비선형 영역",             name_en="Preston nonlinear region",
         formula="P > 5.5 ∧ V > 100 → MRR ≠ Kp·P·V",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["wafer_pressure", "platen_speed"]),
    dict(id="CM5",  ws="W1", name_ko="Platen 속도 부족",                name_en="Platen speed insufficient",
         formula="V_platen < 25 rpm",
         citation="Preston (1927)", severity="warning",
         params=["platen_speed"]),
    dict(id="CM6",  ws="W1", name_ko="Platen 속도 극한",                name_en="Platen speed extreme",
         formula="V_platen > 115 rpm",
         citation="Preston (1927)", severity="warning",
         params=["platen_speed"]),
    dict(id="CM7",  ws="W1", name_ko="Carrier 속도 부족",               name_en="Carrier speed insufficient",
         formula="V_carrier < 25 rpm",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["carrier_speed"]),
    dict(id="CM8",  ws="W1", name_ko="Carrier 속도 극한",               name_en="Carrier speed extreme",
         formula="V_carrier > 115 rpm",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["carrier_speed"]),
    dict(id="CM9",  ws="W1", name_ko="Platen·Carrier 속도차 과다",      name_en="Platen-Carrier speed mismatch",
         formula="|V_platen - V_carrier| > 50 rpm",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["platen_speed", "carrier_speed"]),
    dict(id="CM10", ws="W1", name_ko="상대속도 0",                      name_en="Zero relative velocity",
         formula="|V_platen - V_carrier| < 2 rpm → MRR ≈ 0",
         citation="Preston (1927)", severity="critical",
         params=["platen_speed", "carrier_speed"]),

    # W2: Slurry Chemistry (CM11~CM20)
    dict(id="CM11", ws="W2", name_ko="슬러리 유량 부족",  name_en="Slurry flow insufficient",
         formula="Q_slurry < 80 ml/min",
         citation="Cook (1990)", severity="warning",
         params=["slurry_flow"]),
    dict(id="CM12", ws="W2", name_ko="슬러리 유량 과다",  name_en="Slurry flow excessive",
         formula="Q_slurry > 380 ml/min",
         citation="Cook (1990)", severity="info",
         params=["slurry_flow"]),
    dict(id="CM13", ws="W2", name_ko="pH 극산",           name_en="Extreme acidic pH",
         formula="pH < 2.5 → corrosion",
         citation="Cook (1990) J.Non-Cryst.Solids 120", severity="critical",
         params=["slurry_ph"]),
    dict(id="CM14", ws="W2", name_ko="pH 극염기",          name_en="Extreme alkaline pH",
         formula="pH > 11 → corrosion",
         citation="Cook (1990)", severity="critical",
         params=["slurry_ph"]),
    dict(id="CM15", ws="W2", name_ko="pH 중성",            name_en="Neutral pH",
         formula="6 < pH < 8 → low MRR",
         citation="Cook (1990)", severity="warning",
         params=["slurry_ph"]),
    dict(id="CM16", ws="W2", name_ko="Abrasive 과소",      name_en="Abrasive size too small",
         formula="d_abrasive < 15 nm",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["abrasive_size"]),
    dict(id="CM17", ws="W2", name_ko="Abrasive 과다",      name_en="Abrasive size too large",
         formula="d_abrasive > 180 nm → scratch",
         citation="Luo & Dornfeld (2001)", severity="critical",
         params=["abrasive_size"]),
    dict(id="CM18", ws="W2", name_ko="Abrasive 응집",       name_en="Abrasive agglomeration",
         formula="T > 35°C ∧ pH > 9 → agglomeration",
         citation="Krishnan et al. (2010)", severity="warning",
         params=["slurry_temp", "slurry_ph"]),
    dict(id="CM19", ws="W2", name_ko="슬러리 온도 저온",    name_en="Slurry temp low",
         formula="T_slurry < 18°C",
         citation="Cook (1990)", severity="info",
         params=["slurry_temp"]),
    dict(id="CM20", ws="W2", name_ko="슬러리 온도 과다",    name_en="Slurry temp high",
         formula="T_slurry > 38°C",
         citation="Cook (1990)", severity="warning",
         params=["slurry_temp"]),

    # W3: Uniformity (CM21~CM30)
    dict(id="CM21", ws="W3", name_ko="Dishing",                  name_en="Dishing",
         formula="ρ_pattern < 25 % ∧ Removal > 300 nm",
         citation="Steigerwald et al. (1997)", severity="critical",
         params=["pattern_density", "target_removal"]),
    dict(id="CM22", ws="W3", name_ko="Erosion",                   name_en="Erosion",
         formula="ρ_pattern > 70 % ∧ Removal > 250 nm",
         citation="Steigerwald et al. (1997)", severity="warning",
         params=["pattern_density", "target_removal"]),
    dict(id="CM23", ws="W3", name_ko="Step Height 과다",          name_en="Step height excessive",
         formula="h_step_init > 800 nm",
         citation="Steigerwald et al. (1997)", severity="warning",
         params=["initial_step_height"]),
    dict(id="CM24", ws="W3", name_ko="Step Height 부족",          name_en="Step height insufficient",
         formula="h_step_init < 150 nm",
         citation="Steigerwald et al. (1997)", severity="info",
         params=["initial_step_height"]),
    dict(id="CM25", ws="W3", name_ko="Center-Edge non-uniform",   name_en="Center-Edge non-uniformity",
         formula="|ΔV_relative| ∉ [5, 60] rpm",
         citation="Luo & Dornfeld (2001)", severity="warning",
         params=["platen_speed", "carrier_speed"]),
    dict(id="CM26", ws="W3", name_ko="Pad Conditioning 부족",     name_en="Pad conditioning insufficient",
         formula="t_cond < 15 s → glazed pad",
         citation="Borucki et al. (2004)", severity="critical",
         params=["pad_conditioning"]),
    dict(id="CM27", ws="W3", name_ko="Pad 과 Conditioning",       name_en="Pad over-conditioning",
         formula="t_cond > 55 s → pad wear",
         citation="Borucki et al. (2004)", severity="info",
         params=["pad_conditioning"]),
    dict(id="CM28", ws="W3", name_ko="과연마",                     name_en="Over-polishing",
         formula="t_polish > 280 s",
         citation="Steigerwald et al. (1997)", severity="warning",
         params=["polish_time"]),
    dict(id="CM29", ws="W3", name_ko="미연마",                     name_en="Under-polishing",
         formula="t_polish < 40 s → residue",
         citation="Steigerwald et al. (1997)", severity="critical",
         params=["polish_time"]),
    dict(id="CM30", ws="W3", name_ko="Target Removal 과다",        name_en="Target removal excessive",
         formula="Removal > 450 nm",
         citation="Steigerwald et al. (1997)", severity="warning",
         params=["target_removal"]),

    # W4: Defects (CM31~CM35)
    dict(id="CM31", ws="W4", name_ko="Micro-scratch",             name_en="Micro-scratch",
         formula="P > 4.5 ∧ d_abrasive > 120 nm",
         citation="Luo & Dornfeld (2001)", severity="critical",
         params=["wafer_pressure", "abrasive_size"]),
    dict(id="CM32", ws="W4", name_ko="Slurry residue",            name_en="Slurry residue",
         formula="Q_slurry < 100 ∧ d_abrasive > 100 nm",
         citation="Cook (1990)", severity="warning",
         params=["slurry_flow", "abrasive_size"]),
    dict(id="CM33", ws="W4", name_ko="Corrosion",                  name_en="Corrosion",
         formula="pH < 3 ∨ pH > 10",
         citation="Cook (1990)", severity="critical",
         params=["slurry_ph"]),
    dict(id="CM34", ws="W4", name_ko="Peeling",                    name_en="Peeling",
         formula="Q_slurry < 100 ∧ t_polish > 200 s",
         citation="Steigerwald et al. (1997)", severity="warning",
         params=["slurry_flow", "polish_time"]),
    dict(id="CM35", ws="W4", name_ko="Hazy surface",                name_en="Hazy surface",
         formula="T_slurry > 32°C ∧ d_abrasive > 100 nm",
         citation="Krishnan et al. (2010)", severity="warning",
         params=["slurry_temp", "abrasive_size"]),

    # W5: Integration (CM36~CM40)
    dict(id="CM36", ws="W5", name_ko="Preston 비선형 복합",         name_en="Preston nonlinear compound",
         formula="P > 5 ∧ V > 100 ∧ d_abrasive > 100",
         citation="Luo & Dornfeld (2001)", severity="critical",
         params=["wafer_pressure", "platen_speed", "abrasive_size"]),
    dict(id="CM37", ws="W5", name_ko="Dual-damascene 손상",          name_en="Dual-damascene damage",
         formula="P > 5.5 ∧ ρ_pattern > 70 %",
         citation="Steigerwald et al. (1997)", severity="critical",
         params=["wafer_pressure", "pattern_density"]),
    dict(id="CM38", ws="W5", name_ko="Pad life 임계",                 name_en="Pad life critical",
         formula="T_slurry > 35 ∧ t_cond > 50",
         citation="Borucki et al. (2004)", severity="info",
         params=["slurry_temp", "pad_conditioning"]),
    dict(id="CM39", ws="W5", name_ko="복합 이상 (2종 이탈)",          name_en="Compound anomaly (2 deviations)",
         formula="|P_dev| ∨ |pH_dev| ∨ |Q_dev| ≥ 2",
         citation="SPILab (2024)", severity="warning",
         params=["wafer_pressure", "slurry_ph", "slurry_flow"]),
    dict(id="CM40", ws="W5", name_ko="복합 고위험 (3종 이상 이탈)",   name_en="Compound high-risk (3+ deviations)",
         formula="Σ deviations ≥ 3",
         citation="SPILab (2024)", severity="critical",
         params=["wafer_pressure", "platen_speed", "slurry_ph", "abrasive_size"]),
]


# 워크스페이스 메타 (build_kg_cs.py 와 동일 형식, CMP 의 5개 카테고리)
WORKSPACES: list[dict[str, str]] = [
    dict(id="W1", name_ko="압력·속도 워크스페이스",  name_en="Pressure & Speed workspace",
         desc="Preston MRR=Kp·P·V, Platen/Carrier 속도, 스크래치/Dishing"),
    dict(id="W2", name_ko="슬러리 화학 워크스페이스", name_en="Slurry Chemistry workspace",
         desc="pH·Abrasive 크기·온도, 응집·Corrosion·MRR 화학적 영향"),
    dict(id="W3", name_ko="균일도 워크스페이스",     name_en="Uniformity workspace",
         desc="Dishing/Erosion, Pad Conditioning, Step Height, Center-Edge"),
    dict(id="W4", name_ko="결함 워크스페이스",       name_en="Defect workspace",
         desc="Micro-scratch, Residue, Corrosion, Peeling, Haze 결함 검출"),
    dict(id="W5", name_ko="통합 워크스페이스",        name_en="Integrated workspace",
         desc="W1~W4 인과체인 + Dual-damascene + 복합 이상 검출 + Grade 산출"),
]


# CMP 공정 KPI Spec (W5의 Spec 노드)
SPECS: list[dict[str, Any]] = [
    dict(id="SPEC_MRR",      name="MRR ≥ 100 nm/min",       metric="MRR",          threshold=100,  unit="nm/min", domain="Throughput"),
    dict(id="SPEC_WIWNU",    name="WIWNU ≤ 5 %",             metric="WIWNU",        threshold=5,    unit="%",      domain="Uniformity"),
    dict(id="SPEC_DEFECT",   name="Defect Probability ≤ 0.15", metric="DefectProb", threshold=0.15, unit="prob",   domain="Yield"),
    dict(id="SPEC_DISHING",  name="Dishing ≤ 30 nm",           metric="Dishing",    threshold=30,   unit="nm",     domain="Uniformity"),
    dict(id="SPEC_QUALITY",  name="Quality Score ≥ 0.85",       metric="Quality",    threshold=0.85, unit="score",  domain="Composite"),
]


# 슬러그 헬퍼 (build_kg_cs.py 와 동일)
def slug(s: str) -> str:
    return re.sub(r"[^a-zA-Z0-9]+", "_", s).strip("_")


# ────────────────────────────────────────────────────────────────
# 정적 분석: C# 소스에서 공정 파라미터 / CPI 가중치 / 룰 언급 추출
# (build_kg_cs.py 의 RE_MAT_ROW / RE_BOWING / RE_RULE_TAG 와 동일 역할)
# ────────────────────────────────────────────────────────────────
# DEFAULTS dict 항목: ["wafer_pressure"] = 3,
RE_DEFAULT_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*(?P<value>[\-\d\.eE]+)\s*[,}]'
)
# RANGES dict 항목: ["wafer_pressure"] = (0.5, 7),
RE_RANGE_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*\(\s*(?P<lo>[\-\d\.eE]+)\s*,\s*(?P<hi>[\-\d\.eE]+)\s*\)'
)
# CpiWeights tuple: ("wafer_pressure", 0.18, 3),
RE_CPI_WEIGHT = re.compile(
    r'\(\s*"(?P<name>\w+)"\s*,\s*(?P<weight>[\d\.eE]+)\s*,\s*(?P<baseline>[\-\d\.eE]+)\s*\)'
)
# 코드 내 CM 룰 언급 (예: "CM1", new Rule("CM3", ...))
RE_RULE_TAG = re.compile(r'"(CM\d+)"')


def parse_engine_source(src: str) -> dict[str, Any]:
    """CmpPhysicsEngine.cs 1차 파싱 결과를 dict 로 반환."""
    # DEFAULTS 영역 추출 (CMP 엔진은 DEFAULTS 와 RANGES 가 별도 dict)
    defaults: dict[str, float] = {}
    m = re.search(r'DEFAULTS\s*=\s*new\(\)\s*\{([^}]+)\}', src, re.DOTALL)
    if m:
        for mm in RE_DEFAULT_ROW.finditer(m.group(1)):
            defaults[mm.group("name")] = float(mm.group("value"))

    # RANGES 영역 추출
    ranges: dict[str, tuple[float, float]] = {}
    m = re.search(r'RANGES\s*=\s*new\(\)\s*\{([^}]+)\}', src, re.DOTALL)
    if m:
        for mm in RE_RANGE_ROW.finditer(m.group(1)):
            ranges[mm.group("name")] = (float(mm.group("lo")), float(mm.group("hi")))

    # CpiWeights 영역 추출
    cpi_weights: list[dict[str, Any]] = []
    m = re.search(r'CpiWeights\s*=\s*\{([^;]+)\};', src, re.DOTALL)
    if m:
        for mm in RE_CPI_WEIGHT.finditer(m.group(1)):
            cpi_weights.append(dict(
                name=mm.group("name"),
                weight=float(mm.group("weight")),
                baseline=float(mm.group("baseline")),
            ))

    # 12개 ProcessParam 노드 데이터 (defaults + ranges 병합)
    process_params: dict[str, dict[str, float]] = {}
    weight_map = {w["name"]: w["weight"] for w in cpi_weights}
    for name, default in defaults.items():
        lo, hi = ranges.get(name, (None, None))
        process_params[name] = dict(
            default=default,
            range_lo=lo,
            range_hi=hi,
            cpi_weight=weight_map.get(name, 0.0),
        )

    # CM 룰 언급 (build_kg_cs.py 의 rule_mentions 와 동일 역할)
    rule_mentions: list[str] = sorted({m for m in RE_RULE_TAG.findall(src)},
                                      key=lambda x: int(x[2:]))

    return dict(
        process_params=process_params,
        cpi_weights=cpi_weights,
        rule_mentions=rule_mentions,
    )


# ────────────────────────────────────────────────────────────────
# 그래프 빌더 (build_kg_cs.py 와 동일 구조)
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

    # 2) ProcessParam 노드 (소스 추출 — CS Edition 의 Material 노드와 동일 역할)
    for name, props in parsed["process_params"].items():
        nodes.append(Node(
            id=f"param:{slug(name)}", type="ProcessParam", label=name,
            props=props,
        ))
    # CPI 가중치 페어 — ProcessParam → ProcessParam 의 DERIVES_FROM 엣지로 표현하는 대신
    # CPI 자체가 가중 합산이므로 가중치 메타는 props 에만 포함 (Bowing 처럼 별도 엣지 안 만듦)

    # 3) PhysicsRule 노드 + Workspace 귀속 + 인용 (build_kg_cs.py 와 100% 동일)
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
        # CITES (텍스트 ID)
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

    # 4) Spec 노드 + GOVERNS (어떤 룰이 어떤 spec을 충족시키는지)
    for s in SPECS:
        nodes.append(Node(
            id=f"spec:{s['id']}", type="Spec", label=s["name"],
            props=dict(metric=s["metric"], threshold=s["threshold"],
                       unit=s["unit"], domain=s["domain"]),
        ))
    # 명시 매핑 (룰 ↔ spec)
    rule_spec_map = [
        ("CM2",  "SPEC_MRR"),       # MRR 부족
        ("CM10", "SPEC_MRR"),       # 상대속도 0
        ("CM21", "SPEC_DISHING"),   # Dishing
        ("CM25", "SPEC_WIWNU"),     # Center-Edge non-uniformity
        ("CM31", "SPEC_DEFECT"),    # Micro-scratch
        ("CM33", "SPEC_DEFECT"),    # Corrosion
        ("CM40", "SPEC_QUALITY"),   # 복합 고위험
    ]
    for rid, sid in rule_spec_map:
        edges.append(Edge(src=f"rule:{rid}", dst=f"spec:{sid}", rel="GOVERNS"))

    # 5) ProcessParam → Rule (어떤 룰이 어느 공정 파라미터를 직접 트리거하는지)
    #    파라미터별 트리거 룰 매핑 (CS Edition 의 Material → Rule 매핑과 동일 역할)
    param_to_rules: dict[str, list[str]] = {}
    for r in RULES:
        for p in r["params"]:
            param_to_rules.setdefault(p, []).append(r["id"])
    existing_pp_ids = {n.id for n in nodes if n.type == "ProcessParam"}
    for pname, rule_ids in param_to_rules.items():
        pp_id = f"param:{slug(pname)}"
        if pp_id in existing_pp_ids:
            for rid in rule_ids:
                edges.append(Edge(src=f"rule:{rid}", dst=pp_id, rel="USES",
                                  props=dict(via="process_param")))

    # 6) 통합 워크스페이스 인과 체인 — W5 → W1/W2/W3/W4
    for ws in ("W1", "W2", "W3", "W4"):
        edges.append(Edge(src="ws:W5", dst=f"ws:{ws}", rel="DERIVES_FROM"))

    stats = dict(
        nodes_total=len(nodes),
        edges_total=len(edges),
        nodes_by_type=_count_by(nodes, lambda n: n.type),
        edges_by_rel=_count_by(edges, lambda e: e.rel),
        process_params_extracted=list(parsed["process_params"].keys()),
        cpi_weights_extracted=len(parsed["cpi_weights"]),
        rule_mentions_in_source=parsed["rule_mentions"],
    )
    return nodes, edges, stats


def _param_unit(p: str) -> str:
    """build_kg_cs.py 의 _param_unit 와 동일 역할 (CMP 공정 파라미터 단위)."""
    return {
        "wafer_pressure": "psi", "platen_speed": "rpm", "carrier_speed": "rpm",
        "slurry_flow": "ml/min", "slurry_ph": "pH", "abrasive_size": "nm",
        "polish_time": "s", "pad_conditioning": "s", "slurry_temp": "°C",
        "target_removal": "nm", "pattern_density": "%", "initial_step_height": "nm",
    }.get(p, "")


def _count_by(items, key) -> dict[str, int]:
    out: dict[str, int] = {}
    for it in items:
        k = key(it)
        out[k] = out.get(k, 0) + 1
    return out


# ────────────────────────────────────────────────────────────────
# 시리얼라이저 (build_kg_cs.py 와 100% 동일)
# ────────────────────────────────────────────────────────────────
def to_jsonld(nodes: list[Node], edges: list[Edge], stats: dict[str, Any]) -> dict[str, Any]:
    return dict(
        **{"@context": {
            "@vocab":  "https://spilab.ai/ns/raypann-cmp#",
            "rdfs":    "http://www.w3.org/2000/01/rdf-schema#",
        }},
        meta=dict(
            generator="build_kg_cmp.py v1.0",
            source="RaypannSimCMP_v1.1.4 / CmpPhysicsEngine.cs",
            stats=stats,
        ),
        nodes=[asdict(n) for n in nodes],
        edges=[asdict(e) for e in edges],
    )


def to_turtle(nodes: list[Node], edges: list[Edge]) -> str:
    PFX = "@prefix raypann: <https://spilab.ai/ns/raypann-cmp#> .\n"
    PFX += "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n\n"
    lines = [PFX]

    def lit(v: Any) -> str:
        if isinstance(v, (int, float)):
            return str(v)
        if v is None:
            return '""'
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
# 엔트리 (build_kg_cs.py 와 100% 동일 인터페이스)
# ────────────────────────────────────────────────────────────────
def main(argv: list[str]) -> int:
    args = argv[1:]
    use_legacy = bool(args) and not any(a.startswith("-") for a in args)

    script_dir = Path(__file__).resolve().parent
    default_json = script_dir / "kg_raypann_cmp.json"
    default_ttl  = script_dir / "kg_raypann_cmp.ttl"

    if use_legacy:
        if len(args) < 2:
            print("usage: python build_kg_cmp.py <CmpPhysicsEngine.cs path> <output dir>")
            return 1
        engine_path = Path(args[0])
        out_dir = Path(args[1])
        if out_dir.exists() and not out_dir.is_dir():
            print(f"[ERROR] output dir path is not a directory: {out_dir}")
            return 2
        out_dir.mkdir(parents=True, exist_ok=True)
        json_path = out_dir / "kg_raypann_cmp.json"
        ttl_path = out_dir / "kg_raypann_cmp.ttl"
    else:
        parser = argparse.ArgumentParser(
            prog="build_kg_cmp.py",
            description="레이판 Sim CMP 지식그래프 빌더 "
                        "(기본 출력 위치: build_kg_cmp.py 와 같은 폴더)",
        )
        parser.add_argument("--src", required=True,
                            help="CmpPhysicsEngine.cs 파일 경로")
        parser.add_argument("--out-json", default=str(default_json),
                            help=f"출력 JSON-LD 파일 경로 (기본: {default_json})")
        parser.add_argument("--out-ttl", default=str(default_ttl),
                            help=f"출력 Turtle(.ttl) 파일 경로 (기본: {default_ttl})")
        ns = parser.parse_args(args)

        engine_path = Path(ns.src)
        json_path = Path(ns.out_json)
        ttl_path = Path(ns.out_ttl)

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
