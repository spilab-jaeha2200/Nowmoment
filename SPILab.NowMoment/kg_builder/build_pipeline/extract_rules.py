#!/usr/bin/env python3
# ════════════════════════════════════════════════════════════════════
# extract_rules.py — NowMoment v4.1 Phase 3 (작업 1)
#
# 개선 개발계획서 4.2 / 6.3:
#   "1순위 — Cython 컴파일 + 룰 메타데이터 암호화 분리"
#
#   Cython 컴파일만으로는 부족하다 (Phase 3 사전 검증 리포트 3.2):
#   .so/.pyd 를 import 하면 RULES 가 메모리에 평문으로 올라온다.
#   따라서 RULES(룰 ID·수식·인용·severity = 진짜 IP, 계획서 C2)를
#   빌더 .py 에서 물리적으로 분리하여 별도 암호화 리소스(.enc)로
#   만든다. 컴파일된 빌더는 런타임에 키를 받아 .enc 를 복호화한다.
#
# 본 도구의 역할:
#   build_kg_*.py  →  ┬→ build_kg_*.stripped.py  (RULES 없는 빌드 로직)
#                     └→ rules/*.enc             (AES-256-GCM 암호화 RULES)
#
#   .stripped.py 가 Cython 컴파일 입력이 되고, .enc 는 .spc 번들의
#   rules/ 에 담긴다 (계획서 4.3 번들 구조).
#
# 사용:
#   python extract_rules.py --key-file core.key --out-dir ../build_out
#   (--key-file 미지정 시 새 키를 생성해 저장)
#
# 보안 주의:
#   - core.key 는 절대 .spc 번들이나 저장소에 포함하지 않는다.
#     Secure-Verify(Phase 3 작업 3)가 인증 후 키 저장소에서 발급한다.
#   - 본 도구는 빌드 PC(Core-Owner 권한)에서만 실행한다.
# ════════════════════════════════════════════════════════════════════
from __future__ import annotations

import argparse
import ast
import io
import json
import os
import sys
from pathlib import Path

# ★ v4.1: 출력 인코딩 UTF-8 고정 (앱이 외부 프로세스로 호출할 때
#   stdout 이 cp949 로 잡혀 '—' 등에서 UnicodeEncodeError 나는 것 방지).
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except (AttributeError, ValueError):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

try:
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
except ImportError:
    print("[ERROR] cryptography 패키지가 필요합니다: pip install cryptography",
          file=sys.stderr)
    sys.exit(2)


# RULES 리스트를 모듈 레벨에 두는 빌더 4종.
# build_kg_photo.py 는 RULES 리스트가 없는 모듈형 구조이므로 제외
# (photo 의 IP 는 빌드 로직 자체 → Cython 컴파일만으로 보호).
BUILDERS_WITH_RULES = [
    "build_kg_cs.py",
    "build_kg_cmp.py",
    "build_kg_etch.py",
    "build_kg_thinfilm.py",
]

# AES-256-GCM: 256-bit 키, 96-bit nonce (계획서 4.3)
KEY_BYTES = 32
NONCE_BYTES = 12


def load_or_create_key(key_file: Path) -> bytes:
    """키 파일을 읽거나, 없으면 새로 생성해 저장한다."""
    if key_file.exists():
        key = key_file.read_bytes()
        if len(key) != KEY_BYTES:
            raise ValueError(f"키 길이 오류: {len(key)} bytes (32 필요)")
        print(f"  키 로드: {key_file}")
        return key
    key = os.urandom(KEY_BYTES)
    key_file.write_bytes(key)
    try:
        os.chmod(key_file, 0o600)  # 소유자만 읽기
    except OSError:
        pass
    print(f"  키 신규 생성: {key_file}  (★ 안전한 키 저장소로 이동할 것)")
    return key


def extract_rules_source(py_path: Path) -> tuple[str, str]:
    """
    빌더 .py 를 AST 로 파싱하여 'RULES = [...]' 할당문을 찾는다.
    반환: (rules 할당문 소스, RULES 를 제거한 나머지 모듈 소스)
    """
    source = py_path.read_text(encoding="utf-8")
    tree = ast.parse(source)
    lines = source.splitlines(keepends=True)

    rules_node = None
    for node in tree.body:
        # RULES = [...]  또는  RULES: 타입 = [...]
        is_rules = False
        if isinstance(node, ast.Assign):
            is_rules = any(
                isinstance(t, ast.Name) and t.id == "RULES"
                for t in node.targets
            )
        elif isinstance(node, ast.AnnAssign):
            is_rules = (isinstance(node.target, ast.Name)
                        and node.target.id == "RULES")
        if is_rules:
            rules_node = node
            break

    if rules_node is None:
        raise ValueError(f"{py_path.name}: 모듈 레벨 RULES 정의를 찾지 못함")

    # ast 의 lineno/end_lineno 는 1-indexed, end 는 포함
    start = rules_node.lineno - 1
    end = rules_node.end_lineno
    rules_src = "".join(lines[start:end])
    stripped_src = "".join(lines[:start]) + "".join(lines[end:])
    return rules_src, stripped_src


def evaluate_rules(rules_src: str) -> list:
    """
    'RULES = [...]' 소스를 안전하게 평가하여 실제 리스트 객체를 얻는다.
    빌더의 RULES 는 dict(...) / 리터럴만 쓰므로 제한된 네임스페이스로 exec.
    """
    ns: dict = {"__builtins__": {"dict": dict, "list": list,
                                 "tuple": tuple, "set": set}}
    exec(rules_src, ns)  # noqa: S102 - 신뢰된 사내 소스, 제한 네임스페이스
    rules = ns.get("RULES")
    if not isinstance(rules, list):
        raise ValueError("RULES 평가 결과가 list 가 아님")
    return rules


def encrypt_rules(rules: list, key: bytes) -> bytes:
    """RULES 리스트를 JSON 직렬화 후 AES-256-GCM 으로 암호화한다."""
    plaintext = json.dumps(rules, ensure_ascii=False,
                           separators=(",", ":")).encode("utf-8")
    nonce = os.urandom(NONCE_BYTES)
    aesgcm = AESGCM(key)
    ciphertext = aesgcm.encrypt(nonce, plaintext, associated_data=b"NowMomentRULESv1")
    # 산출물 = nonce(12) + ciphertext+tag
    return nonce + ciphertext


def process_builder(py_path: Path, key: bytes, out_dir: Path) -> dict:
    """빌더 1개를 처리: stripped.py + .enc 생성. 요약 dict 반환."""
    rules_src, stripped_src = extract_rules_source(py_path)
    rules = evaluate_rules(rules_src)

    stem = py_path.stem  # build_kg_cmp
    domain = stem.replace("build_kg_", "")

    # 1) stripped 소스 — RULES 자리에 런타임 로더 호출을 끼워넣는다
    loader_stub = (
        "# ── v4.1 Phase 3: RULES 는 암호화 분리됨 (rules_loader 가 복호화) ──\n"
        "from rules_loader import load_rules as _load_rules\n"
        f'RULES = _load_rules("{domain}")\n'
    )
    stripped_final = _insert_loader(stripped_src, loader_stub)
    stripped_path = out_dir / f"{stem}.stripped.py"
    stripped_path.write_text(stripped_final, encoding="utf-8")

    # 2) 암호화 RULES
    enc = encrypt_rules(rules, key)
    enc_path = out_dir / "rules" / f"{domain}.enc"
    enc_path.parent.mkdir(parents=True, exist_ok=True)
    enc_path.write_bytes(enc)

    return {
        "builder": py_path.name,
        "domain": domain,
        "rule_count": len(rules),
        "stripped": stripped_path.name,
        "enc": f"rules/{domain}.enc",
        "enc_bytes": len(enc),
    }


def _insert_loader(stripped_src: str, loader_stub: str) -> str:
    """
    RULES 가 제거된 자리에 로더 스텁을 넣는다.

    삽입 위치는 "마지막 모듈 레벨 import 문의 바로 다음 줄"이다.
    AST 로 import 문의 end_lineno 를 찾으므로, 데코레이터(@dataclass)
    한가운데나 함수 본문 안에 잘못 끼워 넣는 일이 없다.
    import 가 하나도 없으면(드묾) 파일 맨 앞에 넣는다.
    """
    tree = ast.parse(stripped_src)
    lines = stripped_src.splitlines(keepends=True)

    last_import_end = 0  # 0 = import 없음 → 맨 앞
    for node in tree.body:
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            # end_lineno 는 1-indexed 포함 → 그 줄 다음에 삽입
            last_import_end = max(last_import_end, node.end_lineno or node.lineno)

    return ("".join(lines[:last_import_end])
            + "\n" + loader_stub + "\n"
            + "".join(lines[last_import_end:]))


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description="KG 빌더에서 RULES 를 분리·암호화 (v4.1 Phase 3 작업 1)")
    ap.add_argument("--builders-dir", type=Path, default=Path(__file__).parent.parent,
                    help="build_kg_*.py 가 있는 폴더 (기본: kg_builder/)")
    ap.add_argument("--out-dir", type=Path, required=True,
                    help="stripped.py + rules/*.enc 출력 폴더")
    ap.add_argument("--key-file", type=Path, default=Path("core.key"),
                    help="AES-256 키 파일 (없으면 생성)")
    args = ap.parse_args(argv)

    args.out_dir.mkdir(parents=True, exist_ok=True)

    print("=" * 60)
    print("  RULES 분리·암호화 (v4.1 Phase 3 작업 1)")
    print("=" * 60)
    key = load_or_create_key(args.key_file)
    print()

    summary = []
    for name in BUILDERS_WITH_RULES:
        py_path = args.builders_dir / name
        if not py_path.exists():
            print(f"  [SKIP] {name} — 파일 없음")
            continue
        try:
            info = process_builder(py_path, key, args.out_dir)
            summary.append(info)
            print(f"  [OK] {info['domain']:9s} RULES {info['rule_count']:3d}개 "
                  f"→ {info['enc']} ({info['enc_bytes']} bytes)")
        except Exception as e:  # noqa: BLE001
            print(f"  [FAIL] {name}: {e}", file=sys.stderr)
            return 1

    # build_kg_photo.py 안내
    photo = args.builders_dir / "build_kg_photo.py"
    if photo.exists():
        print(f"  [INFO] build_kg_photo.py — RULES 리스트 없음 (모듈형). "
              f"Cython 컴파일만 적용.")

    # 요약 매니페스트
    manifest = args.out_dir / "rules_manifest.json"
    manifest.write_text(json.dumps(summary, ensure_ascii=False, indent=2),
                        encoding="utf-8")
    print()
    print(f"  매니페스트: {manifest}")
    total = sum(s["rule_count"] for s in summary)
    print(f"  총 {len(summary)}개 빌더, {total}개 룰 분리·암호화 완료.")
    print()
    print("  ★ 다음: build_cython.py 로 *.stripped.py 를 .pyd 컴파일")
    print("  ★ core.key 는 .spc 번들에 넣지 말 것 (Secure-Verify 가 관리)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
