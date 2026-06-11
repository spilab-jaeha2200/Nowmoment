"""
dump_photo_to_csharp.py — RaypannSimPhoto python_engine → PhotoEngineMeta.cs
================================================================
입력: 레이판 Sim Photo 의 python_engine/ 디렉토리 (여러 .py 파일)
출력: PhotoEngineMeta.cs (메타데이터 단일 파일)

설계
----
build_kg_photo.py 가 직접 ast 분석을 하던 것을 두 단계로 분리:
  (1) 이 스크립트가 ast 분석 결과를 PhotoEngineMeta.cs 로 출력
  (2) build_kg_photo.py 가 PhotoEngineMeta.cs 를 정규식으로 분석하여 KG 추출

PhotoEngineMeta.cs 는 build_kg_cs.py 가 사용하는 CSPhysicsEngine.cs 와
같은 형식의 패턴을 갖는다:
  * // Rule N: <module>     (모듈을 PhysicsRule 로 추출)
  * Dictionary<string, ...> = new() { ... }   (재료/파라미터를 추출)
  * 주석으로 추가 메타데이터 (BELONGS_TO, USES 관계)

사용법
------
  python dump_photo_to_csharp.py --src "<path>\\RaypannSimPhoto\\python_engine"
  결과: <스크립트폴더>/PhotoEngineMeta.cs

지원 환경
---------
  Python 3.10+ (ast 표준 라이브러리만 사용)
"""
from __future__ import annotations
import argparse
import ast
import os
import sys
from pathlib import Path
from typing import Dict, List, Set, Any

EXCLUDE_FILES: Set[str] = {
    "__init__.py",
    "litho_sim_pb2.py",
    "litho_sim_pb2_grpc.py",
    "server_dual.py",
    "sweep.py",
    "test_engine.py",
}

SPEC_FUNC_PREFIXES = (
    "compute_", "simulate_", "rcwa_", "build_", "solve_", "extract_",
    "mack_", "rayleigh_", "bossung_", "process_",
)


def analyze_module(file_path: Path, module_name: str) -> Dict[str, Any]:
    """단일 .py 파일 ast 분석 — build_kg_photo.py 와 동일 로직."""
    src = file_path.read_text(encoding="utf-8")
    try:
        tree = ast.parse(src, filename=str(file_path))
    except SyntaxError as e:
        print(f"  [WARN] {file_path.name} 파싱 실패: {e}", file=sys.stderr)
        return {"module": module_name, "doc": "", "classes": [], "functions": [],
                "dataclasses": [], "imports_local": []}

    doc = (ast.get_docstring(tree) or "").strip()
    doc_first_line = doc.split("\n")[0] if doc else ""

    classes: List[Dict[str, Any]] = []
    dataclasses_: List[str] = []
    for node in tree.body:
        if isinstance(node, ast.ClassDef):
            cdoc = (ast.get_docstring(node) or "").strip()
            cdoc_first = cdoc.split("\n")[0] if cdoc else ""
            is_dc = any(
                (isinstance(d, ast.Name) and d.id == "dataclass") or
                (isinstance(d, ast.Call) and isinstance(d.func, ast.Name)
                    and d.func.id == "dataclass")
                for d in node.decorator_list
            )
            fields = []
            if is_dc:
                for item in node.body:
                    if isinstance(item, ast.AnnAssign) and isinstance(item.target, ast.Name):
                        fname = item.target.id
                        default = None
                        if item.value is not None:
                            try:
                                default = ast.literal_eval(item.value)
                            except Exception:
                                try:
                                    default = ast.unparse(item.value)
                                except Exception:
                                    default = None
                        try:
                            type_str = ast.unparse(item.annotation) if hasattr(ast, "unparse") else ""
                        except Exception:
                            type_str = ""
                        fields.append({"name": fname, "type": type_str, "default": default})
                dataclasses_.append(node.name)
            classes.append({
                "name": node.name, "doc": cdoc_first,
                "is_dataclass": is_dc, "fields": fields,
            })

    functions: List[Dict[str, str]] = []
    for node in tree.body:
        if isinstance(node, ast.FunctionDef):
            fdoc = (ast.get_docstring(node) or "").strip()
            fdoc_first = fdoc.split("\n")[0] if fdoc else ""
            functions.append({"name": node.name, "doc": fdoc_first})

    imports_local: List[str] = []
    for node in tree.body:
        if isinstance(node, ast.ImportFrom) and node.module:
            imports_local.append(node.module.split(".")[0])
        elif isinstance(node, ast.Import):
            for alias in node.names:
                imports_local.append(alias.name.split(".")[0])

    return {
        "module": module_name,
        "doc": doc_first_line,
        "classes": classes,
        "dataclasses": dataclasses_,
        "functions": functions,
        "imports_local": imports_local,
    }


def cs_escape(s: str) -> str:
    """C# 문자열 리터럴용 escape."""
    return (s or "").replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ").strip()


def emit_cs(modules: List[Dict[str, Any]]) -> str:
    """모듈 분석 결과를 PhotoEngineMeta.cs 텍스트로 직렬화."""
    module_names = {m["module"] for m in modules}

    lines: List[str] = []
    lines.append("// ════════════════════════════════════════════════════════════════════")
    lines.append("// PhotoEngineMeta.cs — RaypannSimPhoto 메타데이터 (auto-generated)")
    lines.append("// ════════════════════════════════════════════════════════════════════")
    lines.append("//")
    lines.append("// 이 파일은 dump_photo_to_csharp.py 가 자동 생성합니다. 직접 수정하지 마세요.")
    lines.append("// 원본: RaypannSimPhoto/python_engine/ (9개 .py 파일)")
    lines.append("// 빌더: build_kg_photo.py 가 이 파일을 정규식으로 분석해 KG 추출.")
    lines.append("//")
    lines.append("// 형식 규칙:")
    lines.append("//   * // Rule N: <module>     ← PhysicsRule 노드")
    lines.append("//   * // BELONGS_TO_WORKSPACE: <ws_name>")
    lines.append("//   * // USES_MODULE: <other_module>")
    lines.append("//   * Dictionary<string, ParameterMeta> Parameters = new() { ... }")
    lines.append("//   * Dictionary<string, SpecMeta>      Specs      = new() { ... }")
    lines.append("// ════════════════════════════════════════════════════════════════════")
    lines.append("")
    lines.append("using System.Collections.Generic;")
    lines.append("")
    lines.append("namespace RaypannSimPhoto.Generated")
    lines.append("{")
    lines.append("    public record ParameterMeta(string Module, string Description, string FieldsJson);")
    lines.append("    public record SpecMeta(string Module, string Description);")
    lines.append("")
    lines.append("    public static class PhotoEngineMeta")
    lines.append("    {")
    lines.append('        public const string WorkspaceName = "Photo";')
    lines.append('        public const string WorkspaceDescription = "포토리소그래피 시뮬레이션 통합 워크스페이스 (rigorous_aerial)";')
    lines.append("")

    # ── PhysicsRule (모듈) ──
    lines.append("        // ───────── PhysicsRule (Modules) ─────────")
    for idx, m in enumerate(modules, start=1):
        lines.append(f"        // Rule {idx}: {m['module']}")
        lines.append(f"        //   doc: {cs_escape(m['doc'])}")
        lines.append(f"        //   BELONGS_TO_WORKSPACE: Photo")
        for imp in m["imports_local"]:
            if imp in module_names and imp != m["module"]:
                lines.append(f"        //   USES_MODULE: {imp}")
        lines.append("")

    lines.append("        public static readonly List<string> Modules = new()")
    lines.append("        {")
    for m in modules:
        lines.append(f'            "{m["module"]}",')
    lines.append("        };")
    lines.append("")

    # ── 모듈 간 의존성 (USES) ──
    lines.append("        // ───────── Module Dependencies (USES edges) ─────────")
    lines.append("        public static readonly List<(string From, string To)> ModuleUses = new()")
    lines.append("        {")
    for m in modules:
        for imp in m["imports_local"]:
            if imp in module_names and imp != m["module"]:
                lines.append(f'            ("{m["module"]}", "{imp}"),')
    lines.append("        };")
    lines.append("")

    # ── Parameter (dataclass) ──
    lines.append("        // ───────── Parameter (dataclasses) ─────────")
    lines.append("        public static readonly Dictionary<string, ParameterMeta> Parameters = new()")
    lines.append("        {")
    for m in modules:
        for cls in m["classes"]:
            if not cls["is_dataclass"]:
                continue
            # fields 를 간단 JSON 문자열로 직렬화 (이중 escape)
            import json as _json
            fields_obj = {fld["name"]: {"type": fld["type"], "default": fld["default"]}
                          for fld in cls["fields"]}
            fields_json = _json.dumps(fields_obj, ensure_ascii=False).replace('"', '\\"')
            lines.append(
                f'            ["{cls["name"]}"] = new("{m["module"]}", '
                f'"{cs_escape(cls["doc"] or cls["name"])}", '
                f'"{fields_json}"),'
            )
    lines.append("        };")
    lines.append("")

    # ── Spec (핵심 함수) ──
    lines.append("        // ───────── Spec (Core Functions) ─────────")
    lines.append("        public static readonly Dictionary<string, SpecMeta> Specs = new()")
    lines.append("        {")
    seen: Set[str] = set()
    for m in modules:
        for fn in m["functions"]:
            if not fn["name"].startswith(SPEC_FUNC_PREFIXES):
                continue
            key = fn["name"]
            if key in seen:
                key = f"{m['module']}.{fn['name']}"
            seen.add(key)
            lines.append(
                f'            ["{key}"] = new("{m["module"]}", '
                f'"{cs_escape(fn["doc"] or fn["name"])}"),'
            )
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")
    return "\n".join(lines)


def main() -> None:
    p = argparse.ArgumentParser(description="RaypannSimPhoto python_engine → PhotoEngineMeta.cs")
    p.add_argument("--src", required=True,
                   help="python_engine 폴더 절대경로 (여러 .py 파일 포함)")
    p.add_argument("--out", default=None,
                   help="출력 .cs 경로 (기본: <스크립트폴더>/PhotoEngineMeta.cs)")
    args = p.parse_args()

    src_dir = Path(args.src).resolve()
    if not src_dir.exists() or not src_dir.is_dir():
        print(f"[ERR] --src 폴더가 존재하지 않거나 디렉토리가 아닙니다: {src_dir}",
              file=sys.stderr)
        sys.exit(1)

    script_dir = Path(__file__).resolve().parent
    out_path = Path(args.out) if args.out else (script_dir / "PhotoEngineMeta.cs")

    py_files = sorted(f for f in src_dir.iterdir()
                      if f.is_file() and f.suffix == ".py" and f.name not in EXCLUDE_FILES)
    if not py_files:
        print(f"[ERR] {src_dir} 안에 분석할 .py 파일이 없습니다.", file=sys.stderr)
        sys.exit(2)

    print(f"[INFO] 분석 대상 {len(py_files)}개 모듈:")
    modules = []
    for f in py_files:
        meta = analyze_module(f, f.stem)
        modules.append(meta)
        print(f"  - {f.name}: classes={len(meta['classes'])}, "
              f"funcs={len(meta['functions'])}, "
              f"local_imports={[i for i in meta['imports_local'] if i in {x.stem for x in py_files}]}")

    cs_text = emit_cs(modules)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(cs_text, encoding="utf-8")

    # 통계
    n_modules = len(modules)
    n_dataclasses = sum(1 for m in modules for c in m["classes"] if c["is_dataclass"])
    n_specs = sum(1 for m in modules for fn in m["functions"]
                  if fn["name"].startswith(SPEC_FUNC_PREFIXES))
    n_uses = sum(1 for m in modules for imp in m["imports_local"]
                 if imp in {x["module"] for x in modules} and imp != m["module"])
    print()
    print(f"[OK] PhotoEngineMeta.cs 생성 완료")
    print(f"     modules={n_modules}, dataclasses={n_dataclasses}, specs={n_specs}, uses={n_uses}")
    print(f"     -> {out_path}")
    print(f"        ({len(cs_text):,} bytes, {cs_text.count(chr(10))+1} lines)")


if __name__ == "__main__":
    main()
