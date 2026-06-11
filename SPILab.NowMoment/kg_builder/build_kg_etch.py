"""
build_kg_etch.py — 레이판 Sim Etch Edition 지식그래프 빌더 (Plasma Etching)
================================================================
입력: 레이판 Sim Etch 의 EtchPhysicsEngine.cs (단일 C# 파일)
출력: kg_raypann_etch.json  (NowMoment 임베딩용)
       kg_raypann_etch.ttl   (RDF/Turtle, 외부 KG와 연동용 옵션)

설계 원칙 (build_kg_cs.py 와 동일)
----------
1. 정적 분석 only (실행 의존 없음): C# 파일을 정규식 기반으로 파싱
2. 노드 5종: PhysicsRule, ProcessParam, Workspace, Parameter, Spec
3. 엣지 6종: USES, GOVERNS, DERIVES_FROM, CITES, BELONGS_TO, REQUIRES
4. 결정적 ID: prefix + sluggified key  → 재실행 시 ID 안정성 보장
5. JSON-LD 호환: @id, @type 필드를 함께 발행

CS Edition 과의 도메인 차이
----------
- Material DB → ProcessParam DB (15 공정 파라미터: rf_power_source, chamber_pressure, ...)
- 23 Rules (R1~R23) → 50 Rules (ET1~ET50)
- 5 Workspaces (W1~W5) → 5 Workspaces (W1~W5, 7개 카테고리를 5개로 그룹화)
- 5 Specs (5G/Power) → 5 Specs (Etch 공정 KPI)
- BOWING (Material 상호작용) → EpiWeights (가중 합산, ※ CMP의 CpiWeights 와 다른 이름)

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
# 메타데이터 — 50 룰 정의 (EtchPhysicsEngine.cs 의 BuildRules() 와 정합)
#   각 룰 키: id, ws, name_ko, name_en, formula, citation, severity, params
#
# 카테고리 → 워크스페이스 매핑 (5개 그룹화):
#   W1 Plasma         ← Plasma         (ET1~ET15)
#   W2 Gas Chemistry  ← Gas            (ET16~ET24)
#   W3 Profile/CD     ← Profile + Uniformity (ET25~ET35)
#   W4 Time/Endpoint  ← Time + Endpoint (ET36~ET44)
#   W5 Integration    ← Integration    (ET45~ET50)
# ────────────────────────────────────────────────────────────────
RULES: list[dict[str, Any]] = [
    # ── W1: Plasma (ET1~ET15) ──
    dict(id="ET1",  ws="W1", name_ko="RF Source 부족 (플라즈마 미형성)", name_en="RF Source insufficient (no plasma)",
         formula="P_source < 80 W → ne 임계 미달",
         citation="Lieberman & Lichtenberg (2005)", severity="critical",
         params=["rf_power_source"]),
    dict(id="ET2",  ws="W1", name_ko="RF Source 과다 (장비 부하)",       name_en="RF Source excessive (equipment load)",
         formula="P_source > 900 W",
         citation="Lieberman & Lichtenberg (2005)", severity="warning",
         params=["rf_power_source"]),
    dict(id="ET3",  ws="W1", name_ko="RF Bias 과다 (PR 손상)",            name_en="RF Bias excessive (PR damage)",
         formula="P_bias > 400 W → 이온 에너지 과다",
         citation="Coburn & Winters (1979)", severity="critical",
         params=["rf_power_bias"]),
    dict(id="ET4",  ws="W1", name_ko="RF Bias 0W (이방성 소실)",         name_en="RF Bias zero (anisotropy loss)",
         formula="P_bias < 10 W → 이방성 식각 소실",
         citation="Coburn & Winters (1979)", severity="warning",
         params=["rf_power_bias"]),
    dict(id="ET5",  ws="W1", name_ko="Source≤Bias 비율 이상",             name_en="Source ≤ Bias abnormal ratio",
         formula="P_source ≤ P_bias → 플라즈마 발산 약화",
         citation="Lieberman & Lichtenberg (2005)", severity="warning",
         params=["rf_power_source", "rf_power_bias"]),
    dict(id="ET6",  ws="W1", name_ko="저 Source + 저압 (플라즈마 불안정)", name_en="Low Source + Low Pressure (unstable)",
         formula="P_source < 150 ∧ p < 15 mTorr",
         citation="Lieberman & Lichtenberg (2005)", severity="warning",
         params=["rf_power_source", "chamber_pressure"]),
    dict(id="ET7",  ws="W1", name_ko="압력 극저 (mean free path 과다)",   name_en="Pressure too low (excessive MFP)",
         formula="p < 8 mTorr → λ_mfp ≫ d",
         citation="Chen (2016) Plasma Phys", severity="warning",
         params=["chamber_pressure"]),
    dict(id="ET8",  ws="W1", name_ko="압력 과다 (물리식각 약화)",          name_en="Pressure excessive (sputter weakened)",
         formula="p > 400 mTorr → 이온 산란 증가",
         citation="Lieberman & Lichtenberg (2005)", severity="warning",
         params=["chamber_pressure"]),
    dict(id="ET9",  ws="W1", name_ko="고압+고Bias 역 상관",                name_en="High P + High Bias inverse correlation",
         formula="p > 200 ∧ P_bias > 300 → 이온산란",
         citation="Coburn & Winters (1979)", severity="warning",
         params=["chamber_pressure", "rf_power_bias"]),
    dict(id="ET10", ws="W1", name_ko="배기 부족 (입자 체류)",              name_en="Exhaust insufficient (particle residence)",
         formula="Q_exhaust < 200 sccm",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["exhaust_flow"]),
    dict(id="ET11", ws="W1", name_ko="DC Bias 이탈",                        name_en="DC Bias deviation",
         formula="|P_source - P_bias| < 50 W",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["rf_power_source", "rf_power_bias"]),
    dict(id="ET12", ws="W1", name_ko="Plasma kickback (저압+고Source)",    name_en="Plasma kickback (low P + high Source)",
         formula="p < 15 ∧ P_source > 700",
         citation="Chen (2016)", severity="info",
         params=["chamber_pressure", "rf_power_source"]),
    dict(id="ET13", ws="W1", name_ko="압력 산포 과다",                      name_en="Pressure dispersion excessive",
         formula="p > 350 mTorr",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["chamber_pressure"]),
    dict(id="ET14", ws="W1", name_ko="Source 극단 민감도",                  name_en="Source extreme sensitivity",
         formula="P_source > 850 ∨ P_source < 100",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["rf_power_source"]),
    dict(id="ET15", ws="W1", name_ko="Bias 극단 민감도",                    name_en="Bias extreme sensitivity",
         formula="P_bias > 450 ∨ (5 < P_bias < 30)",
         citation="Coburn & Winters (1979)", severity="info",
         params=["rf_power_bias"]),

    # ── W2: Gas Chemistry (ET16~ET24) ──
    dict(id="ET16", ws="W2", name_ko="Main gas 부족",                       name_en="Main gas insufficient",
         formula="Q_main < 10 sccm",
         citation="Coburn (1982)", severity="critical",
         params=["gas_flow_main"]),
    dict(id="ET17", ws="W2", name_ko="Main gas 과다 (희석)",                name_en="Main gas excessive (dilution)",
         formula="Q_main > 180 sccm",
         citation="Coburn (1982)", severity="warning",
         params=["gas_flow_main"]),
    dict(id="ET18", ws="W2", name_ko="Assist 과다 (패시베이션 과도)",        name_en="Assist excessive (over-passivation)",
         formula="Q_assist > 80 sccm",
         citation="Bondur (1976)", severity="warning",
         params=["gas_flow_assist"]),
    dict(id="ET19", ws="W2", name_ko="Carrier 부족 (이온 전달 부족)",        name_en="Carrier insufficient (ion transfer)",
         formula="Q_carrier < 10 sccm",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["gas_flow_carrier"]),
    dict(id="ET20", ws="W2", name_ko="Main/Assist 역전 (선택비 저하)",       name_en="Main/Assist reversed (selectivity loss)",
         formula="Q_assist > Q_main → 선택비 ↓",
         citation="Coburn (1982)", severity="warning",
         params=["gas_flow_main", "gas_flow_assist"]),
    dict(id="ET21", ws="W2", name_ko="총 유량 과다 (체류시간 부족)",        name_en="Total flow excessive (residence time)",
         formula="Q_total > 400 sccm",
         citation="Lieberman & Lichtenberg (2005)", severity="info",
         params=["gas_flow_main", "gas_flow_assist", "gas_flow_carrier"]),
    dict(id="ET22", ws="W2", name_ko="총 유량 부족",                         name_en="Total flow insufficient",
         formula="Q_total < 30 sccm",
         citation="Lieberman & Lichtenberg (2005)", severity="warning",
         params=["gas_flow_main", "gas_flow_assist", "gas_flow_carrier"]),
    dict(id="ET23", ws="W2", name_ko="Main gas 비율 과소 → 선택비 저하",    name_en="Main gas ratio low (selectivity loss)",
         formula="Q_main / Q_total < 0.2",
         citation="Coburn (1982)", severity="critical",
         params=["gas_flow_main", "gas_flow_assist", "gas_flow_carrier"]),
    dict(id="ET24", ws="W2", name_ko="O2 무첨가 (탄소 잔류)",               name_en="No O2 (carbon residue)",
         formula="Q_assist < 2 sccm → 폴리머 잔류",
         citation="Bondur (1976)", severity="info",
         params=["gas_flow_assist"]),

    # ── W3: Profile/CD (ET25~ET35) ──
    dict(id="ET25", ws="W3", name_ko="측벽 중간부 과식각 (고압·저Bias)",     name_en="Sidewall mid over-etch (high P + low Bias)",
         formula="p > 120 ∧ P_bias < 50",
         citation="Coburn & Winters (1979)", severity="critical",
         params=["chamber_pressure", "rf_power_bias"]),
    dict(id="ET26", ws="W3", name_ko="CD Loss 예상",                          name_en="CD Loss expected",
         formula="CD < 30 ∨ (P_bias > 400 ∧ CD < 80)",
         citation="Donnelly & Kornblit (2013)", severity="critical",
         params=["pattern_cd", "rf_power_bias"]),
    dict(id="ET27", ws="W3", name_ko="Bowing (측벽 만곡)",                    name_en="Bowing (sidewall curvature)",
         formula="p > 150 ∧ P_source > 500",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["chamber_pressure", "rf_power_source"]),
    dict(id="ET28", ws="W3", name_ko="역경사 측벽 (저압+고Bias)",             name_en="Re-entrant sidewall (low P + high Bias)",
         formula="p < 20 ∧ P_bias > 350",
         citation="Coburn & Winters (1979)", severity="warning",
         params=["chamber_pressure", "rf_power_bias"]),
    dict(id="ET29", ws="W3", name_ko="Undercut (저Bias)",                     name_en="Undercut (low Bias)",
         formula="P_bias < 30 ∧ Q_main > 50",
         citation="Coburn & Winters (1979)", severity="warning",
         params=["rf_power_bias", "gas_flow_main"]),
    dict(id="ET30", ws="W3", name_ko="Pattern CD 극소",                       name_en="Pattern CD too small",
         formula="CD < 25 nm",
         citation="Donnelly & Kornblit (2013)", severity="critical",
         params=["pattern_cd"]),
    dict(id="ET31", ws="W3", name_ko="Aspect Ratio 과다 (>5:1)",              name_en="Aspect ratio excessive (>5:1)",
         formula="t_target / CD > 5",
         citation="Gottscho et al. (1992) ARDE", severity="warning",
         params=["target_thickness", "pattern_cd"]),
    dict(id="ET32", ws="W3", name_ko="Mask 두께 부족",                        name_en="Mask thickness insufficient",
         formula="t_mask < 200 nm",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["mask_thickness"]),
    dict(id="ET33", ws="W3", name_ko="Target 두께 과다",                      name_en="Target thickness excessive",
         formula="t_target > 800 nm",
         citation="Donnelly & Kornblit (2013)", severity="info",
         params=["target_thickness"]),
    dict(id="ET34", ws="W3", name_ko="Pattern Density 극고 (ARDE)",           name_en="Pattern density extreme (ARDE)",
         formula="ρ_pattern > 85 %",
         citation="Gottscho et al. (1992)", severity="warning",
         params=["pattern_density"]),
    dict(id="ET35", ws="W3", name_ko="균일도 > 5% (Chuck Temp 극단)",         name_en="Uniformity > 5% (extreme chuck temp)",
         formula="T_chuck < -10 ∨ T_chuck > 70",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["chuck_temperature"]),

    # ── W4: Time/Endpoint (ET36~ET44) ──
    dict(id="ET36", ws="W4", name_ko="Etch time 극저 (부식각)",                name_en="Etch time too low (under-etch)",
         formula="t_etch < 10 s",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["etch_time"]),
    dict(id="ET37", ws="W4", name_ko="Etch time 과다 (과식각)",                name_en="Etch time excessive (over-etch)",
         formula="t_etch > 240 s",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["etch_time"]),
    dict(id="ET38", ws="W4", name_ko="Chuck 온도 부족",                        name_en="Chuck temperature low",
         formula="T_chuck < 0°C",
         citation="Donnelly & Kornblit (2013)", severity="info",
         params=["chuck_temperature"]),
    dict(id="ET39", ws="W4", name_ko="Chuck 온도 과다 (PR 변형)",              name_en="Chuck temperature excessive (PR damage)",
         formula="T_chuck > 60°C",
         citation="Donnelly & Kornblit (2013)", severity="warning",
         params=["chuck_temperature"]),
    dict(id="ET40", ws="W4", name_ko="Endpoint 미신뢰 (과소 over-etch)",       name_en="Endpoint unreliable (low over-etch)",
         formula="OE < 3 %",
         citation="Marcoux & Foo (1981)", severity="warning",
         params=["over_etch_ratio"]),
    dict(id="ET41", ws="W4", name_ko="Over-etch 과다 (> 25%)",                 name_en="Over-etch excessive (>25%)",
         formula="OE > 25 %",
         citation="Marcoux & Foo (1981)", severity="warning",
         params=["over_etch_ratio"]),
    dict(id="ET42", ws="W4", name_ko="Endpoint 센서 신호 저하",                name_en="Endpoint sensor signal weak",
         formula="P_source < 150 ∧ sensor=OES",
         citation="Marcoux & Foo (1981)", severity="info",
         params=["rf_power_source", "endpoint_sensor"]),
    dict(id="ET43", ws="W4", name_ko="OES 노이즈 (저 Source + 고 Pressure)",   name_en="OES noise (low Source + high P)",
         formula="P_source < 200 ∧ p > 300",
         citation="Marcoux & Foo (1981)", severity="info",
         params=["rf_power_source", "chamber_pressure"]),
    dict(id="ET44", ws="W4", name_ko="Over-etch 경미 위반 (교재 예시)",        name_en="Over-etch minor violation (textbook)",
         formula="13 < OE < 20 %",
         citation="SPILab (2024)", severity="info",
         params=["over_etch_ratio"]),

    # ── W5: Integration (ET45~ET50) ──
    dict(id="ET45", ws="W5", name_ko="선택비 < 1.5 (PR 손실)",                 name_en="Selectivity < 1.5 (PR loss)",
         formula="t_mask / t_target < 1.5",
         citation="Coburn (1982)", severity="warning",
         params=["mask_thickness", "target_thickness"]),
    dict(id="ET46", ws="W5", name_ko="PR 손실 (Bias·Time 복합)",              name_en="PR loss (Bias·Time compound)",
         formula="P_bias > 350 ∧ t_etch > 150",
         citation="Coburn (1982)", severity="warning",
         params=["rf_power_bias", "etch_time"]),
    dict(id="ET47", ws="W5", name_ko="ARDE 효과",                             name_en="ARDE effect",
         formula="ρ_pattern > 80 ∧ t_target > 300",
         citation="Gottscho et al. (1992)", severity="info",
         params=["pattern_density", "target_thickness"]),
    dict(id="ET48", ws="W5", name_ko="Micro-loading",                         name_en="Micro-loading",
         formula="ρ_pattern < 20 ∨ ρ_pattern > 85",
         citation="Mogab (1977)", severity="info",
         params=["pattern_density"]),
    dict(id="ET49", ws="W5", name_ko="Notching (고 Aspect+고압)",              name_en="Notching (high AR + high P)",
         formula="t_target/CD > 3 ∧ p > 200",
         citation="Hwang et al. (1996)", severity="info",
         params=["target_thickness", "pattern_cd", "chamber_pressure"]),
    dict(id="ET50", ws="W5", name_ko="복합 고위험 (3종 이상 Margin 이탈)",     name_en="Compound high-risk (3+ deviations)",
         formula="Σ deviations ≥ 3",
         citation="SPILab (2024)", severity="critical",
         params=["rf_power_source", "rf_power_bias", "chamber_pressure", "gas_flow_main"]),
]


# 워크스페이스 메타 (build_kg_cs.py 와 동일 형식, Etch 의 5개 그룹)
WORKSPACES: list[dict[str, str]] = [
    dict(id="W1", name_ko="플라즈마 워크스페이스",      name_en="Plasma workspace",
         desc="RF Source/Bias, 압력, 배기 — 플라즈마 발산·이온 에너지·DC Bias"),
    dict(id="W2", name_ko="가스 화학 워크스페이스",     name_en="Gas Chemistry workspace",
         desc="Main/Assist/Carrier 가스 유량, 비율, 패시베이션, 선택비"),
    dict(id="W3", name_ko="프로파일·CD 워크스페이스",   name_en="Profile/CD workspace",
         desc="CD Loss, Bowing, Undercut, Aspect Ratio, ARDE, 균일도"),
    dict(id="W4", name_ko="시간·종점 워크스페이스",     name_en="Time/Endpoint workspace",
         desc="Etch time, Over-etch, Chuck 온도, OES/RGA Endpoint 검출"),
    dict(id="W5", name_ko="통합 워크스페이스",          name_en="Integrated workspace",
         desc="W1~W4 인과체인 + ARDE + Notching + 복합 이상 검출 + Grade 산출"),
]


# Etch 공정 KPI Spec (W5의 Spec 노드)
SPECS: list[dict[str, Any]] = [
    dict(id="SPEC_ANISO",  name="이방성 ≥ 0.95",          metric="Anisotropy", threshold=0.95, unit="ratio",   domain="Profile"),
    dict(id="SPEC_SEL",    name="선택비 ≥ 1.5",            metric="Selectivity", threshold=1.5,  unit="ratio",   domain="Mask"),
    dict(id="SPEC_CD",     name="CD bias ≤ 5 nm",          metric="CDbias",     threshold=5,    unit="nm",      domain="CD"),
    dict(id="SPEC_UNIF",   name="Uniformity ≤ 5 %",        metric="WIWNU",      threshold=5,    unit="%",       domain="Uniformity"),
    dict(id="SPEC_DEFECT", name="Defect Probability ≤ 0.15", metric="DefectProb", threshold=0.15, unit="prob",   domain="Yield"),
]


# 슬러그 헬퍼 (build_kg_cs.py 와 동일)
def slug(s: str) -> str:
    return re.sub(r"[^a-zA-Z0-9]+", "_", s).strip("_")


# ────────────────────────────────────────────────────────────────
# 정적 분석: C# 소스에서 공정 파라미터 / EPI 가중치 / 룰 언급 추출
# (build_kg_cs.py 의 RE_MAT_ROW / RE_BOWING / RE_RULE_TAG 와 동일 역할)
# ────────────────────────────────────────────────────────────────
# DEFAULTS dict 항목: ["rf_power_source"] = 300,
RE_DEFAULT_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*(?P<value>[\-\d\.eE]+)\s*[,}]'
)
# RANGES dict 항목: ["rf_power_source"] = (50, 1000),
RE_RANGE_ROW = re.compile(
    r'\["(?P<name>\w+)"\]\s*=\s*\(\s*(?P<lo>[\-\d\.eE]+)\s*,\s*(?P<hi>[\-\d\.eE]+)\s*\)'
)
# EpiWeights tuple: ("rf_power_source", 0.15, 300),  ※ Etch 는 EpiWeights (CMP 의 CpiWeights 와 다름)
RE_EPI_WEIGHT = re.compile(
    r'\(\s*"(?P<name>\w+)"\s*,\s*(?P<weight>[\d\.eE]+)\s*,\s*(?P<baseline>[\-\d\.eE]+)\s*\)'
)
# 코드 내 ET 룰 언급 (예: "ET1", new Rule("ET3", ...))
RE_RULE_TAG = re.compile(r'"(ET\d+)"')


def parse_engine_source(src: str) -> dict[str, Any]:
    """EtchPhysicsEngine.cs 1차 파싱 결과를 dict 로 반환."""
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

    # EpiWeights 영역 추출 (※ Etch 는 EpiWeights, CMP 는 CpiWeights)
    epi_weights: list[dict[str, Any]] = []
    m = re.search(r'EpiWeights\s*=\s*\{([^;]+)\};', src, re.DOTALL)
    if m:
        for mm in RE_EPI_WEIGHT.finditer(m.group(1)):
            epi_weights.append(dict(
                name=mm.group("name"),
                weight=float(mm.group("weight")),
                baseline=float(mm.group("baseline")),
            ))

    # 15개 ProcessParam 노드 데이터 (defaults + ranges 병합)
    process_params: dict[str, dict[str, float]] = {}
    weight_map = {w["name"]: w["weight"] for w in epi_weights}
    for name, default in defaults.items():
        lo, hi = ranges.get(name, (None, None))
        process_params[name] = dict(
            default=default,
            range_lo=lo,
            range_hi=hi,
            epi_weight=weight_map.get(name, 0.0),
        )

    # ET 룰 언급 (build_kg_cs.py 의 rule_mentions 와 동일 역할)
    rule_mentions: list[str] = sorted({m for m in RE_RULE_TAG.findall(src)},
                                      key=lambda x: int(x[2:]))

    return dict(
        process_params=process_params,
        epi_weights=epi_weights,
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
        ("ET4",  "SPEC_ANISO"),    # Bias 0W → 이방성 소실
        ("ET28", "SPEC_ANISO"),    # 역경사 측벽
        ("ET29", "SPEC_ANISO"),    # Undercut
        ("ET20", "SPEC_SEL"),      # Main/Assist 역전
        ("ET23", "SPEC_SEL"),      # Main 비율 과소
        ("ET45", "SPEC_SEL"),      # 선택비 < 1.5
        ("ET26", "SPEC_CD"),       # CD Loss
        ("ET30", "SPEC_CD"),       # CD 극소
        ("ET35", "SPEC_UNIF"),     # 균일도 > 5%
        ("ET34", "SPEC_UNIF"),     # ARDE
        ("ET3",  "SPEC_DEFECT"),   # PR 손상
        ("ET27", "SPEC_DEFECT"),   # Bowing
        ("ET49", "SPEC_DEFECT"),   # Notching
        ("ET50", "SPEC_DEFECT"),   # 복합 고위험
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
        epi_weights_extracted=len(parsed["epi_weights"]),
        rule_mentions_in_source=parsed["rule_mentions"],
    )
    return nodes, edges, stats


def _param_unit(p: str) -> str:
    """build_kg_cs.py 의 _param_unit 와 동일 역할 (Etch 공정 파라미터 단위)."""
    return {
        "rf_power_source":   "W",     "rf_power_bias":     "W",
        "chamber_pressure":  "mTorr", "gas_flow_main":     "sccm",
        "gas_flow_assist":   "sccm",  "gas_flow_carrier":  "sccm",
        "chuck_temperature": "°C",    "etch_time":         "s",
        "over_etch_ratio":   "%",     "target_thickness":  "nm",
        "mask_thickness":    "nm",    "pattern_cd":        "nm",
        "pattern_density":   "%",     "endpoint_sensor":   "OES/RGA",
        "exhaust_flow":      "sccm",
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
            "@vocab":  "https://spilab.ai/ns/raypann-etch#",
            "rdfs":    "http://www.w3.org/2000/01/rdf-schema#",
        }},
        meta=dict(
            generator="build_kg_etch.py v1.0",
            source="RaypannSimEtch_v1.1.3 / EtchPhysicsEngine.cs",
            stats=stats,
        ),
        nodes=[asdict(n) for n in nodes],
        edges=[asdict(e) for e in edges],
    )


def to_turtle(nodes: list[Node], edges: list[Edge]) -> str:
    PFX = "@prefix raypann: <https://spilab.ai/ns/raypann-etch#> .\n"
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
    default_json = script_dir / "kg_raypann_etch.json"
    default_ttl  = script_dir / "kg_raypann_etch.ttl"

    if use_legacy:
        if len(args) < 2:
            print("usage: python build_kg_etch.py <EtchPhysicsEngine.cs path> <output dir>")
            return 1
        engine_path = Path(args[0])
        out_dir = Path(args[1])
        if out_dir.exists() and not out_dir.is_dir():
            print(f"[ERROR] output dir path is not a directory: {out_dir}")
            return 2
        out_dir.mkdir(parents=True, exist_ok=True)
        json_path = out_dir / "kg_raypann_etch.json"
        ttl_path = out_dir / "kg_raypann_etch.ttl"
    else:
        parser = argparse.ArgumentParser(
            prog="build_kg_etch.py",
            description="레이판 Sim Etch 지식그래프 빌더 "
                        "(기본 출력 위치: build_kg_etch.py 와 같은 폴더)",
        )
        parser.add_argument("--src", required=True,
                            help="EtchPhysicsEngine.cs 파일 경로")
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
