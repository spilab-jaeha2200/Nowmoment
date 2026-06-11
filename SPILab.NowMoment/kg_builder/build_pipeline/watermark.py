#!/usr/bin/env python3
# ════════════════════════════════════════════════════════════════════
# watermark.py — NowMoment v4.1 Phase 3 (작업 5)
#
# 개선 개발계획서 5.4 "워터마킹":
#   Core 가 생성하는 KG JSON/TTL 산출물에 비가시(invisible) 워터마크를
#   삽입한다. 빌드를 수행한 세션 ID·사용자·시각을 인코딩하여, 산출물이
#   외부로 유출될 경우 출처 추적이 가능하게 한다.
#
#   계획서 5.4 핵심 제약:
#     "워터마크는 KG 의 노드/엣지 데이터에는 영향을 주지 않는다."
#   → JSON 은 무해한 meta 필드에, TTL 은 주석에 인코딩한다.
#     노드·엣지 자체는 한 글자도 바뀌지 않는다.
#
# 워터마크 구성:
#   - 평문 요약 1줄 (사람이 읽을 수 있는 출처 표기)
#   - 서명된 페이로드 1개 (위변조 탐지 — HMAC-SHA256)
#     페이로드: {session, actor, role, built_at, domain}
#     서명 키는 SPILab 만 보유 → 워터마크 자체 위조 불가.
#
# 사용:
#   build 직후 후처리로 적용:
#     python watermark.py stamp --json kg_raypann_cmp.json \
#         --ttl kg_raypann_cmp.ttl --session ab12 --actor "홍길동" \
#         --role CoreDeveloper --domain cmp --wm-key wm.key
#
#   유출 산출물 추적:
#     python watermark.py extract --json suspect.json --wm-key wm.key
# ════════════════════════════════════════════════════════════════════
from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path


# JSON meta 안에 들어갈 워터마크 필드명. 'provenance' — KG 의미론에
# 무해한 출처 메타. 노드/엣지와 무관.
WM_FIELD = "_provenance"
WM_VERSION = "1.0"

# TTL 워터마크 주석 접두. 파서가 무시하는 '#' 주석.
TTL_WM_PREFIX = "# spilab-provenance:"


def _sign(payload: dict, wm_key: bytes) -> str:
    """페이로드를 정규 JSON 직렬화 후 HMAC-SHA256 서명."""
    canonical = json.dumps(payload, sort_keys=True,
                           ensure_ascii=False, separators=(",", ":"))
    return hmac.new(wm_key, canonical.encode("utf-8"),
                    hashlib.sha256).hexdigest()


def build_watermark(session: str, actor: str, role: str,
                    domain: str, wm_key: bytes) -> dict:
    """서명된 워터마크 객체를 만든다."""
    payload = {
        "session":  session,
        "actor":    actor,
        "role":     role,
        "domain":   domain,
        "built_at": datetime.now(timezone.utc).isoformat(),
    }
    return {
        "wm_version": WM_VERSION,
        "note":       "SPILab Core build provenance — do not remove",
        "payload":    payload,
        "signature":  _sign(payload, wm_key),
    }


def stamp_json(json_path: Path, watermark: dict) -> None:
    """
    KG JSON 의 meta 필드 안에 워터마크를 삽입한다.
    nodes/edges 는 건드리지 않는다 (계획서 5.4).
    """
    doc = json.loads(json_path.read_text(encoding="utf-8"))
    if not isinstance(doc, dict):
        raise ValueError(f"{json_path.name}: 최상위가 JSON 객체가 아님")

    # meta 가 없으면 만들고, 그 안에 워터마크 필드 삽입.
    meta = doc.get("meta")
    if not isinstance(meta, dict):
        meta = {}
        doc["meta"] = meta
    meta[WM_FIELD] = watermark

    json_path.write_text(
        json.dumps(doc, ensure_ascii=False, indent=2),
        encoding="utf-8")


def stamp_ttl(ttl_path: Path, watermark: dict) -> None:
    """
    TTL 산출물 맨 위에 워터마크를 주석으로 삽입한다.
    Turtle 파서가 '#' 주석을 무시하므로 트리플 데이터에 영향 없음.
    """
    text = ttl_path.read_text(encoding="utf-8")
    # 기존 워터마크 주석이 있으면 제거 후 재삽입 (멱등)
    lines = [ln for ln in text.splitlines()
             if not ln.startswith(TTL_WM_PREFIX)]
    blob = json.dumps(watermark, ensure_ascii=False, separators=(",", ":"))
    header = f"{TTL_WM_PREFIX} {blob}"
    ttl_path.write_text(header + "\n" + "\n".join(lines) + "\n",
                        encoding="utf-8")


def extract_json(json_path: Path) -> dict | None:
    """KG JSON 에서 워터마크를 추출한다. 없으면 None."""
    try:
        doc = json.loads(json_path.read_text(encoding="utf-8"))
        return doc.get("meta", {}).get(WM_FIELD)
    except (json.JSONDecodeError, AttributeError):
        return None


def extract_ttl(ttl_path: Path) -> dict | None:
    """TTL 에서 워터마크 주석을 추출한다. 없으면 None."""
    for line in ttl_path.read_text(encoding="utf-8").splitlines():
        if line.startswith(TTL_WM_PREFIX):
            try:
                return json.loads(line[len(TTL_WM_PREFIX):].strip())
            except json.JSONDecodeError:
                return None
    return None


def verify_watermark(watermark: dict, wm_key: bytes) -> bool:
    """워터마크 서명이 유효한지 — 위변조 탐지."""
    if not isinstance(watermark, dict):
        return False
    payload = watermark.get("payload")
    signature = watermark.get("signature", "")
    if not isinstance(payload, dict) or not signature:
        return False
    expected = _sign(payload, wm_key)
    return hmac.compare_digest(expected, signature)


def load_key(key_file: Path) -> bytes:
    """워터마크 서명 키를 읽거나 새로 생성."""
    if key_file.exists():
        return key_file.read_bytes()
    key = os.urandom(32)
    key_file.write_bytes(key)
    try:
        os.chmod(key_file, 0o600)
    except OSError:
        pass
    print(f"  워터마크 키 신규 생성: {key_file}  (★ 키 저장소로 이동)")
    return key


def cmd_stamp(args: argparse.Namespace) -> int:
    wm_key = load_key(Path(args.wm_key))
    watermark = build_watermark(
        session=args.session, actor=args.actor,
        role=args.role, domain=args.domain, wm_key=wm_key)

    stamped = []
    if args.json:
        jp = Path(args.json)
        if not jp.exists():
            print(f"[ERROR] JSON 없음: {jp}", file=sys.stderr)
            return 1
        stamp_json(jp, watermark)
        stamped.append(str(jp))
    if args.ttl:
        tp = Path(args.ttl)
        if not tp.exists():
            print(f"[ERROR] TTL 없음: {tp}", file=sys.stderr)
            return 1
        stamp_ttl(tp, watermark)
        stamped.append(str(tp))

    if not stamped:
        print("[ERROR] --json 또는 --ttl 중 하나는 지정해야 합니다.",
              file=sys.stderr)
        return 1

    print("=" * 60)
    print("  산출물 워터마킹 (v4.1 Phase 3 작업 5)")
    print("=" * 60)
    print(f"  세션: {args.session}  사용자: {args.actor}  역할: {args.role}")
    for s in stamped:
        print(f"  [OK] {s}")
    print(f"  → 노드·엣지 데이터는 변경되지 않음 (계획서 5.4)")
    return 0


def cmd_extract(args: argparse.Namespace) -> int:
    wm_key = load_key(Path(args.wm_key)) if Path(args.wm_key).exists() else None

    target = Path(args.json or args.ttl)
    if not target.exists():
        print(f"[ERROR] 파일 없음: {target}", file=sys.stderr)
        return 1

    watermark = (extract_json(target) if args.json
                 else extract_ttl(target))

    print("=" * 60)
    print("  워터마크 추출 — 출처 추적")
    print("=" * 60)

    if watermark is None:
        print(f"  워터마크 없음 — {target.name} 에 SPILab 출처 정보가 없습니다.")
        return 2

    payload = watermark.get("payload", {})
    print(f"  세션:   {payload.get('session', '?')}")
    print(f"  사용자: {payload.get('actor', '?')}")
    print(f"  역할:   {payload.get('role', '?')}")
    print(f"  도메인: {payload.get('domain', '?')}")
    print(f"  빌드:   {payload.get('built_at', '?')}")

    if wm_key is not None:
        valid = verify_watermark(watermark, wm_key)
        print(f"  서명:   {'유효 (위변조 없음)' if valid else '★ 무효 — 워터마크 변조됨'}")
    else:
        print(f"  서명:   검증 생략 (--wm-key 미지정)")
    return 0


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description="KG 산출물 워터마킹 (v4.1 Phase 3 작업 5)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("stamp", help="산출물에 워터마크 삽입")
    s.add_argument("--json", help="KG JSON 경로")
    s.add_argument("--ttl",  help="KG TTL 경로")
    s.add_argument("--session", required=True, help="Secure-Verify 세션 ID")
    s.add_argument("--actor",   required=True, help="빌드 수행 사용자")
    s.add_argument("--role",    default="", help="권한 등급")
    s.add_argument("--domain",  default="", help="KG 도메인 (cmp/cs 등)")
    s.add_argument("--wm-key",  default="wm.key", help="워터마크 서명 키")
    s.set_defaults(func=cmd_stamp)

    e = sub.add_parser("extract", help="산출물에서 워터마크 추출")
    e.add_argument("--json", help="KG JSON 경로")
    e.add_argument("--ttl",  help="KG TTL 경로")
    e.add_argument("--wm-key", default="wm.key", help="워터마크 서명 키 (검증용)")
    e.set_defaults(func=cmd_extract)

    args = ap.parse_args(argv)
    if not getattr(args, "json", None) and not getattr(args, "ttl", None):
        print("[ERROR] --json 또는 --ttl 을 지정하세요.", file=sys.stderr)
        return 1
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
