"""
build_kg_thinfilm.py — 레이판 Sim ThinFilm Edition 지식그래프 빌더 (CVD/PVD/ALD)
================================================================
입력: 레이판 Sim ThinFilm 의 ThinFilmPhysicsEngine.cs (단일 C# 파일)
출력: kg_raypann_thinfilm.json  (NowMoment 임베딩용)
       kg_raypann_thinfilm.ttl   (RDF/Turtle, 외부 KG와 연동용 옵션)

설계 원칙 (build_kg_cs.py 와 동일)
----------
1. 정적 분석 only (실행 의존 없음): C# 파일을 정규식 기반으로 파싱
2. 노드 5종: PhysicsRule, ProcessParam, Workspace, Parameter, Spec
3. 엣지 6종: USES, GOVERNS, DERIVES_FROM, CITES, BELONGS_TO, REQUIRES
4. 결정적 ID: prefix + sluggified key  → 재실행 시 ID 안정성 보장
5. JSON-LD 호환: @id, @type 필드를 함께 발행

CS Edition 과의 도메인 차이
----------
- Material DB → ProcessParam DB (14 공정 파라미터: substrate_temp, rf_dc_power, ...)
- 23 Rules (R1~R23) → 45 Rules (TF1~TF45)
- 5 Workspaces (W1~W5) → 5 Workspaces (W1~W5, 6개 카테고리를 5개로 그룹화)
- 5 Specs (5G/Power) → 5 Specs (ThinFilm 공정 KPI)
- BOWING (Material 상호작용) → TpiWeights (가중 합산)
  ※ CMP 의 CpiWeights / Etch 의 EpiWeights 와 다른 이름
- 핵심 공식: GPC × cycles = thickness ± stress
- 지원 공정: CVD · PVD · ALD 3가지

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
# 메타데이터 — 45 룰 정의 (ThinFilmPhysicsEngine.cs 의 BuildRules() 와 정합)
#   각 룰 키: id, ws, name_ko, name_en, formula, citation, severity, params
#
# 카테고리 → 워크스페이스 매핑 (5개 그룹화):
#   W1 Reaction          ← Reaction         (TF1~TF10)
#   W2 Power & Growth    ← Power            (TF11~TF20)
#   W3 GPC/Thickness     ← GPC              (TF21~TF30)
#   W4 Coverage/Stress   ← StepCov + Stress (TF31~TF40)
#   W5 Integration       ← Integration      (TF41~TF45)
# ────────────────────────────────────────────────────────────────
RULES: list[dict[str, Any]] = [
    # ── W1: Reaction Chemistry (TF1~TF10) ──
    dict(id="TF1",  ws="W1", name_ko="반응 불완전 (저온)",          name_en="Incomplete reaction (low temp)",
         formula="T_sub < 130°C → 표면 흡수 반응 부족",
         citation="George (2010) Chem.Rev 110", severity="critical",
         params=["substrate_temp"]),
    dict(id="TF2",  ws="W1", name_ko="기상 핵생성 (고온)",          name_en="Gas-phase nucleation (high temp)",
         formula="T_sub > 550°C → 입자 생성",
         citation="Mahan (2000) Physical Vapor Deposition", severity="warning",
         params=["substrate_temp"]),
    dict(id="TF3",  ws="W1", name_ko="온도 창 이탈",                name_en="Temperature window deviation",
         formula="T_sub ∉ [200, 500]°C",
         citation="George (2010)", severity="warning",
         params=["substrate_temp"]),
    dict(id="TF4",  ws="W1", name_ko="Precursor1 부족",              name_en="Precursor1 insufficient",
         formula="Q_p1 < 5 sccm",
         citation="George (2010)", severity="critical",
         params=["precursor1_flow"]),
    dict(id="TF5",  ws="W1", name_ko="Precursor1 과다",              name_en="Precursor1 excessive",
         formula="Q_p1 > 400 sccm",
         citation="George (2010)", severity="warning",
         params=["precursor1_flow"]),
    dict(id="TF6",  ws="W1", name_ko="Precursor 비율 이상",          name_en="Precursor ratio abnormal",
         formula="Q_p2 / Q_p1 > 2",
         citation="Puurunen (2005) J.Appl.Phys 97", severity="warning",
         params=["precursor1_flow", "precursor2_flow"]),
    dict(id="TF7",  ws="W1", name_ko="반응가스 부족",                name_en="Reactive gas insufficient",
         formula="Q_react < 10 sccm",
         citation="Mahan (2000)", severity="warning",
         params=["reactive_gas_flow"]),
    dict(id="TF8",  ws="W1", name_ko="Carrier gas 부족",             name_en="Carrier gas insufficient",
         formula="Q_carrier < 15 sccm",
         citation="Mahan (2000)", severity="info",
         params=["carrier_gas_flow"]),
    dict(id="TF9",  ws="W1", name_ko="압력 극저",                    name_en="Pressure too low",
         formula="p < 5 mTorr",
         citation="Mahan (2000)", severity="warning",
         params=["chamber_pressure"]),
    dict(id="TF10", ws="W1", name_ko="압력 과다 (입자 생성)",         name_en="Pressure excessive (particles)",
         formula="p > 4500 mTorr",
         citation="Smith (1995) Thin-Film Deposition", severity="warning",
         params=["chamber_pressure"]),

    # ── W2: Power & Growth Mode (TF11~TF20) ──
    dict(id="TF11", ws="W2", name_ko="Power 부족",                   name_en="Power insufficient",
         formula="P_RF/DC < 80 W",
         citation="Smith (1995)", severity="critical",
         params=["rf_dc_power"]),
    dict(id="TF12", ws="W2", name_ko="Power 과다",                   name_en="Power excessive",
         formula="P_RF/DC > 4500 W",
         citation="Smith (1995)", severity="warning",
         params=["rf_dc_power"]),
    dict(id="TF13", ws="W2", name_ko="저온+저전력",                  name_en="Low temp + low power",
         formula="T_sub < 200 ∧ P < 200",
         citation="Mahan (2000)", severity="warning",
         params=["substrate_temp", "rf_dc_power"]),
    dict(id="TF14", ws="W2", name_ko="Rotation 부족 (균일도)",       name_en="Rotation insufficient (uniformity)",
         formula="ω_rotation < 5 rpm",
         citation="Smith (1995)", severity="warning",
         params=["substrate_rotation"]),
    dict(id="TF15", ws="W2", name_ko="Rotation 과다",                name_en="Rotation excessive",
         formula="ω_rotation > 90 rpm",
         citation="Smith (1995)", severity="info",
         params=["substrate_rotation"]),
    dict(id="TF16", ws="W2", name_ko="GPC 변동",                     name_en="GPC fluctuation",
         formula="t_pulse < 0.3 ∨ t_pulse > 8 s",
         citation="Puurunen (2005)", severity="warning",
         params=["pulse_time"]),
    dict(id="TF17", ws="W2", name_ko="Purge 부족 (오염)",            name_en="Purge insufficient (contamination)",
         formula="t_purge < 2 s → ALD cross-contamination",
         citation="Puurunen (2005)", severity="critical",
         params=["purge_time"]),
    dict(id="TF18", ws="W2", name_ko="Purge 과다 (Throughput)",       name_en="Purge excessive (throughput)",
         formula="t_purge > 25 s",
         citation="Puurunen (2005)", severity="info",
         params=["purge_time"]),
    dict(id="TF19", ws="W2", name_ko="Pulse/Purge 비율 이상",         name_en="Pulse/Purge ratio abnormal",
         formula="t_pulse > 0.5 × t_purge",
         citation="Puurunen (2005)", severity="warning",
         params=["pulse_time", "purge_time"]),
    dict(id="TF20", ws="W2", name_ko="ALD 사이클 수 과소",           name_en="ALD cycles too few",
         formula="N_cycles < 5",
         citation="George (2010)", severity="warning",
         params=["ald_cycles"]),

    # ── W3: GPC & Thickness (TF21~TF30) ──
    dict(id="TF21", ws="W3", name_ko="목표 두께 극저",                name_en="Target thickness too low",
         formula="t_target < 3 nm",
         citation="George (2010)", severity="warning",
         params=["target_thickness"]),
    dict(id="TF22", ws="W3", name_ko="목표 두께 과다",                name_en="Target thickness excessive",
         formula="t_target > 800 nm",
         citation="Smith (1995)", severity="info",
         params=["target_thickness"]),
    dict(id="TF23", ws="W3", name_ko="증착시간 극저",                 name_en="Deposition time too low",
         formula="t_dep < 20 s",
         citation="Mahan (2000)", severity="warning",
         params=["deposition_time"]),
    dict(id="TF24", ws="W3", name_ko="증착시간 과다",                 name_en="Deposition time excessive",
         formula="t_dep > 3000 s",
         citation="Mahan (2000)", severity="info",
         params=["deposition_time"]),
    dict(id="TF25", ws="W3", name_ko="ALD 사이클 과다",               name_en="ALD cycles excessive",
         formula="N_cycles > 450",
         citation="Puurunen (2005)", severity="info",
         params=["ald_cycles"]),
    dict(id="TF26", ws="W3", name_ko="CVD 조건 사이클 부족",          name_en="CVD condition cycles insufficient",
         formula="N_cycles < 10 ∧ t_pulse < 1",
         citation="Mahan (2000)", severity="info",
         params=["ald_cycles", "pulse_time"]),
    dict(id="TF27", ws="W3", name_ko="GPC 사이클 변동",               name_en="GPC cycle fluctuation",
         formula="T_sub < 180 ∨ T_sub > 520°C",
         citation="Puurunen (2005)", severity="critical",
         params=["substrate_temp"]),
    dict(id="TF28", ws="W3", name_ko="박막 성장 정지 (저온+저P)",     name_en="Film growth halt (low T + low P)",
         formula="T_sub < 160 ∧ P < 150",
         citation="Mahan (2000)", severity="warning",
         params=["substrate_temp", "rf_dc_power"]),
    dict(id="TF29", ws="W3", name_ko="쉘 형성 위험",                  name_en="Shell formation risk",
         formula="P > 3500 ∧ t_dep < 60",
         citation="Smith (1995)", severity="warning",
         params=["rf_dc_power", "deposition_time"]),
    dict(id="TF30", ws="W3", name_ko="GPC saturation 부족",            name_en="GPC saturation insufficient",
         formula="t_pulse < 0.5 s",
         citation="George (2010)", severity="warning",
         params=["pulse_time"]),

    # ── W4: Coverage & Stress (TF31~TF40) ──
    dict(id="TF31", ws="W4", name_ko="Step Coverage 불량",             name_en="Step coverage poor",
         formula="t_pulse < 0.5 ∧ T_sub > 450",
         citation="Puurunen (2005)", severity="warning",
         params=["pulse_time", "substrate_temp"]),
    dict(id="TF32", ws="W4", name_ko="Conformality 저하",              name_en="Conformality degradation",
         formula="p > 3000 mTorr",
         citation="George (2010)", severity="warning",
         params=["chamber_pressure"]),
    dict(id="TF33", ws="W4", name_ko="균일도 불량 (Rotation)",         name_en="Uniformity poor (rotation)",
         formula="ω_rotation < 10 ∧ T_sub > 350",
         citation="Smith (1995)", severity="warning",
         params=["substrate_rotation", "substrate_temp"]),
    dict(id="TF34", ws="W4", name_ko="Center-edge 편차",               name_en="Center-edge deviation",
         formula="ω_rotation < 15 ∨ ω_rotation > 80",
         citation="Smith (1995)", severity="info",
         params=["substrate_rotation"]),
    dict(id="TF35", ws="W4", name_ko="핀치오프",                       name_en="Pinch-off",
         formula="p > 2000 ∧ t_pulse > 5",
         citation="Puurunen (2005)", severity="warning",
         params=["chamber_pressure", "pulse_time"]),
    dict(id="TF36", ws="W4", name_ko="Void 형성",                       name_en="Void formation",
         formula="p > 2500 ∧ ω_rotation < 15",
         citation="Smith (1995)", severity="critical",
         params=["chamber_pressure", "substrate_rotation"]),
    dict(id="TF37", ws="W4", name_ko="Re-sputter (고 Power)",            name_en="Re-sputter (high power)",
         formula="P > 4000 W (PVD)",
         citation="Mahan (2000)", severity="info",
         params=["rf_dc_power"]),
    dict(id="TF38", ws="W4", name_ko="Target Pinhole",                  name_en="Target pinhole",
         formula="t_dep > 2500 ∧ P > 3000",
         citation="Smith (1995)", severity="info",
         params=["deposition_time", "rf_dc_power"]),
    dict(id="TF39", ws="W4", name_ko="응력 과다 (온도 급변)",            name_en="Stress excessive (temp shift)",
         formula="T_sub > 500 ∨ T_sub < 150",
         citation="Doerner & Nix (1988) CRC Crit.Rev", severity="warning",
         params=["substrate_temp"]),
    dict(id="TF40", ws="W4", name_ko="Film stress 이상",                name_en="Film stress anomaly",
         formula="P > 3500 ∧ T_sub < 250",
         citation="Doerner & Nix (1988)", severity="warning",
         params=["rf_dc_power", "substrate_temp"]),

    # ── W5: Integration (TF41~TF45) ──
    dict(id="TF41", ws="W5", name_ko="복합 증착 이상 (고압+고온)",      name_en="Compound dep abnormal (high P + high T)",
         formula="p > 3000 ∧ T_sub > 500",
         citation="Mahan (2000)", severity="warning",
         params=["chamber_pressure", "substrate_temp"]),
    dict(id="TF42", ws="W5", name_ko="증착 불완전",                     name_en="Deposition incomplete",
         formula="T_sub < 180 ∧ t_dep < 60",
         citation="George (2010)", severity="critical",
         params=["substrate_temp", "deposition_time"]),
    dict(id="TF43", ws="W5", name_ko="ALD cross-contamination",         name_en="ALD cross-contamination",
         formula="t_purge < 3 ∧ t_pulse > 2",
         citation="Puurunen (2005)", severity="critical",
         params=["purge_time", "pulse_time"]),
    dict(id="TF44", ws="W5", name_ko="잔류물 형성",                     name_en="Residue formation",
         formula="Q_p1 > 300 ∧ t_purge < 3",
         citation="Puurunen (2005)", severity="warning",
         params=["precursor1_flow", "purge_time"]),
    dict(id="TF45", ws="W5", name_ko="복합 고위험 (3종 이상 이탈)",     name_en="Compound high-risk (3+ deviations)",
         formula="Σ deviations ≥ 3",
         citation="SPILab (2024)", severity="critical",
         params=["substrate_temp", "chamber_pressure", "rf_dc_power", "pulse_time"]),
]


# 워크스페이스 메타 (build_kg_cs.py 와 동일 형식, ThinFilm 의 5개 그룹)
WORKSPACES: list[dict[str, str]] = [
    dict(id="W1", name_ko="반응 화학 워크스페이스",       name_en="Reaction Chemistry workspace",
         desc="Precursor·반응가스·Carrier gas·기판 온도 — 표면 반응 메커니즘"),
    dict(id="W2", name_ko="Power·성장모드 워크스페이스",  name_en="Power & Growth-Mode workspace",
         desc="RF/DC Power, Substrate Rotation, ALD Pulse/Purge·사이클 수"),
    dict(id="W3", name_ko="GPC·두께 워크스페이스",        name_en="GPC/Thickness workspace",
         desc="GPC × cycles = thickness, 증착시간, ALD/CVD 사이클, GPC saturation"),
    dict(id="W4", name_ko="커버리지·응력 워크스페이스",   name_en="Coverage/Stress workspace",
         desc="Step Coverage, Conformality, Pinch-off, Void, Film Stress"),
    dict(id="W5", name_ko="통합 워크스페이스",            name_en="Integrated workspace",
         desc="W1~W4 인과체인 + ALD cross-contamination + 복합 이상 + Grade 산출"),
]


# ThinFilm 공정 KPI Spec (W5의 Spec 노드)
SPECS: list[dict[str, Any]] = [
    dict(id="SPEC_THICKNESS", name="Thickness 정밀도 ≤ 2 %",   metric="ThicknessAcc", threshold=2,    unit="%",     domain="Thickness"),
    dict(id="SPEC_GPC",       name="GPC 안정성 ≥ 0.95",         metric="GpcStability", threshold=0.95, unit="ratio", domain="Growth"),
    dict(id="SPEC_CONF",      name="Conformality ≥ 0.90",        metric="Conformality", threshold=0.90, unit="ratio", domain="Coverage"),
    dict(id="SPEC_STRESS",    name="Film stress ≤ 500 MPa",      metric="FilmStress",   threshold=500,  unit="MPa",   domain="Stress"),
    dict(id="SPEC_DEFECT",    name="Defect Probability ≤ 0.15",   metric="DefectProb",   threshold=0.15, unit="prob",  domain="Yield"),
]


# 슬러그 헬퍼 (build_kg_cs.py 와 동일)
def slug(s: str) -> str:
    return re.sub(r"[^a-zA-Z0-9]+", "_", s).strip("_")


# ────────────────────────────────────────────────────────────────
# 정적 분석: C# 소스에서 공정 파라미터 / TPI 가중치 / 룰 언급 추출
# (build_kg_cs.py 의 RE_MAT_ROW / RE_BOWING / RE_RULE_TAG 와 동일 역할)
# ────────────────────────────────────────────────────────────────
# DEFAULTS dict 항목: ["substrate_temp"] = 300,
RE_DEFAULT_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*(?P<value>[\-\d\.eE]+)\s*[,}]'
)
# RANGES dict 항목: ["substrate_temp"] = (100, 600),
RE_RANGE_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*\(\s*(?P<lo>[\-\d\.eE]+)\s*,\s*(?P<hi>[\-\d\.eE]+)\s*\)'
)
# TpiWeights tuple: ("substrate_temp", 0.20, 300),  ※ ThinFilm 은 TpiWeights
RE_TPI_WEIGHT = re.compile(
    r'\(\s*"(?P<name>\w+)"\s*,\s*(?P<weight>[\d\.eE]+)\s*,\s*(?P<baseline>[\-\d\.eE]+)\s*\)'
)
# 코드 내 TF 룰 언급 (예: "TF1", new Rule("TF3", ...))
RE_RULE_TAG = re.compile(r'"(TF\d+)"')


def parse_engine_source(src: str) -> dict[str, Any]:
    """ThinFilmPhysicsEngine.cs 1차 파싱 결과를 dict 로 반환."""
    # DEFAULTS 영역 추출
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

    # TpiWeights 영역 추출 (※ ThinFilm 은 TpiWeights, CMP=CpiWeights, Etch=EpiWeights)
    tpi_weights: list[dict[str, Any]] = []
    m = re.search(r'TpiWeights\s*=\s*\{([^;]+)\};', src, re.DOTALL)
    if m:
        for mm in RE_TPI_WEIGHT.finditer(m.group(1)):
            tpi_weights.append(dict(
                name=mm.group("name"),
                weight=float(mm.group("weight")),
                baseline=float(mm.group("baseline")),
            ))

    # 14개 ProcessParam 노드 데이터 (defaults + ranges 병합)
    process_params: dict[str, dict[str, float]] = {}
    weight_map = {w["name"]: w["weight"] for w in tpi_weights}
    for name, default in defaults.items():
        lo, hi = ranges.get(name, (None, None))
        process_params[name] = dict(
            default=default,
            range_lo=lo,
            range_hi=hi,
            tpi_weight=weight_map.get(name, 0.0),
        )

    # TF 룰 언급 (build_kg_cs.py 의 rule_mentions 와 동일 역할)
    rule_mentions: list[str] = sorted({m for m in RE_RULE_TAG.findall(src)},
                                      key=lambda x: int(x[2:]))

    return dict(
        process_params=process_params,
        tpi_weights=tpi_weights,
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
        ("TF21", "SPEC_THICKNESS"),  # 목표 두께 극저
        ("TF22", "SPEC_THICKNESS"),  # 목표 두께 과다
        ("TF16", "SPEC_GPC"),        # GPC 변동
        ("TF27", "SPEC_GPC"),        # GPC 사이클 변동
        ("TF30", "SPEC_GPC"),        # GPC saturation
        ("TF31", "SPEC_CONF"),       # Step Coverage
        ("TF32", "SPEC_CONF"),       # Conformality
        ("TF35", "SPEC_CONF"),       # 핀치오프
        ("TF36", "SPEC_CONF"),       # Void
        ("TF39", "SPEC_STRESS"),     # 응력 과다
        ("TF40", "SPEC_STRESS"),     # Film stress
        ("TF1",  "SPEC_DEFECT"),     # 반응 불완전
        ("TF42", "SPEC_DEFECT"),     # 증착 불완전
        ("TF43", "SPEC_DEFECT"),     # cross-contamination
        ("TF45", "SPEC_DEFECT"),     # 복합 고위험
    ]
    for rid, sid in rule_spec_map:
        edges.append(Edge(src=f"rule:{rid}", dst=f"spec:{sid}", rel="GOVERNS"))

    # 5) ProcessParam → Rule (어떤 룰이 어느 공정 파라미터를 직접 트리거하는지)
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
        tpi_weights_extracted=len(parsed["tpi_weights"]),
        rule_mentions_in_source=parsed["rule_mentions"],
    )
    return nodes, edges, stats


def _param_unit(p: str) -> str:
    """build_kg_cs.py 의 _param_unit 와 동일 역할 (ThinFilm 공정 파라미터 단위)."""
    return {
        "substrate_temp":     "°C",   "chamber_pressure":   "mTorr",
        "rf_dc_power":        "W",    "precursor1_flow":    "sccm",
        "precursor2_flow":    "sccm", "reactive_gas_flow":  "sccm",
        "deposition_time":    "s",    "ald_cycles":         "cycles",
        "pulse_time":         "s",    "purge_time":         "s",
        "target_thickness":   "nm",   "substrate_rotation": "rpm",
        "target_material":    "type", "carrier_gas_flow":   "sccm",
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
            "@vocab":  "https://spilab.ai/ns/raypann-thinfilm#",
            "rdfs":    "http://www.w3.org/2000/01/rdf-schema#",
        }},
        meta=dict(
            generator="build_kg_thinfilm.py v1.0",
            source="RaypannSimThinFilm_v1.1.3 / ThinFilmPhysicsEngine.cs",
            stats=stats,
        ),
        nodes=[asdict(n) for n in nodes],
        edges=[asdict(e) for e in edges],
    )


def to_turtle(nodes: list[Node], edges: list[Edge]) -> str:
    PFX = "@prefix raypann: <https://spilab.ai/ns/raypann-thinfilm#> .\n"
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
    default_json = script_dir / "kg_raypann_thinfilm.json"
    default_ttl  = script_dir / "kg_raypann_thinfilm.ttl"

    if use_legacy:
        if len(args) < 2:
            print("usage: python build_kg_thinfilm.py <ThinFilmPhysicsEngine.cs path> <output dir>")
            return 1
        engine_path = Path(args[0])
        out_dir = Path(args[1])
        if out_dir.exists() and not out_dir.is_dir():
            print(f"[ERROR] output dir path is not a directory: {out_dir}")
            return 2
        out_dir.mkdir(parents=True, exist_ok=True)
        json_path = out_dir / "kg_raypann_thinfilm.json"
        ttl_path = out_dir / "kg_raypann_thinfilm.ttl"
    else:
        parser = argparse.ArgumentParser(
            prog="build_kg_thinfilm.py",
            description="레이판 Sim ThinFilm 지식그래프 빌더 "
                        "(기본 출력 위치: build_kg_thinfilm.py 와 같은 폴더)",
        )
        parser.add_argument("--src", required=True,
                            help="ThinFilmPhysicsEngine.cs 파일 경로")
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
