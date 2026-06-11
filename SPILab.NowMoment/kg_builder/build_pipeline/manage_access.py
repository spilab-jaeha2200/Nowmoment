#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ════════════════════════════════════════════════════════════════════
# manage_access.py — NowMoment v4.1 운영 도구
#
# 개선 개발계획서 5.2 "권한 등급" / 8장 권고:
#   Secure-Verify 의 권한 매트릭스 파일(core_access.json)을 생성·편집한다.
#   누구에게 어떤 Core 역할을 줄지는 조직이 결정하는 사항이므로,
#   코드가 아닌 외부 JSON 파일로 분리되어 있다(CoreAccessMatrix.cs 참조).
#
# ★ 이 도구는 CoreAccessMatrix.cs 와 바이트 단위로 호환된다:
#   - PBKDF2-HMAC-SHA256, 기본 200,000 회
#   - salt 16 바이트, hash 32 바이트, 모두 base64
#   - JSON 스키마/필드명 동일 (employeeId, displayName, role, pbkdf2)
#   C# Rfc2898DeriveBytes.Pbkdf2(SHA256) 와 Python hashlib.pbkdf2_hmac
#   은 동일 알고리즘이므로 한쪽에서 만든 해시를 다른 쪽이 검증한다.
#
# 사용법:
#   python manage_access.py init   --out core_access.json
#   python manage_access.py add    --file core_access.json \
#       --id SPL-001 --name "홍길동" --role CoreOwner
#   python manage_access.py set-role  --file core_access.json --id SPL-042 --role CoreRunner
#   python manage_access.py passwd    --file core_access.json --id SPL-042
#   python manage_access.py remove    --file core_access.json --id SPL-042
#   python manage_access.py list      --file core_access.json
#   python manage_access.py verify    --file core_access.json --id SPL-001
#
# 비밀번호는 입력 시 화면에 표시되지 않으며(getpass), 평문은 어디에도
# 저장되지 않는다 — salt+hash 만 JSON 에 기록된다.
# ════════════════════════════════════════════════════════════════════
"""NowMoment v4.1 Secure-Verify 권한 매트릭스(core_access.json) 관리 CLI."""

import argparse
import base64
import getpass
import hashlib
import hmac
import json
import os
import sys
from pathlib import Path

# CoreAccessMatrix.cs 의 CreateHash() 기본값과 반드시 일치해야 한다.
PBKDF2_ITERATIONS = 200_000
PBKDF2_SALT_BYTES = 16
PBKDF2_HASH_BYTES = 32
SCHEMA_VERSION = "1.0"

# SecureVerifyGate.cs 의 enum CoreRole 과 일치하는 4개 역할.
VALID_ROLES = ("CoreOwner", "CoreDeveloper", "CoreRunner", "ShellOnly")

ROLE_DESC = {
    "CoreOwner":     "번들 생성·서명·룰 편집·키 관리 전체 (CTO / Core 책임자)",
    "CoreDeveloper": "Core 코드 열람·수정·로컬 빌드 (Core 담당 개발자)",
    "CoreRunner":    "Core 기능 실행만 — KG 빌드 호출 (Shell 개발자, QA)",
    "ShellOnly":     "Core 접근 불가 — Shell 기능만 (외부 협력사, 일반 사용자)",
}


# ──────────────────────────────────────────────────────────────────
# PBKDF2 해시 — CoreAccessMatrix.CreateHash() 와 동등
# ──────────────────────────────────────────────────────────────────
def make_pbkdf2(password: str, iterations: int = PBKDF2_ITERATIONS) -> dict:
    """비밀번호를 PBKDF2-SHA256 해시로 변환한다. C# 측과 호환되는 dict 반환."""
    salt = os.urandom(PBKDF2_SALT_BYTES)
    digest = hashlib.pbkdf2_hmac(
        "sha256", password.encode("utf-8"), salt, iterations, PBKDF2_HASH_BYTES
    )
    return {
        "salt": base64.b64encode(salt).decode("ascii"),
        "hash": base64.b64encode(digest).decode("ascii"),
        "iterations": iterations,
    }


def verify_pbkdf2(password: str, pbkdf2: dict) -> bool:
    """비밀번호를 저장된 PBKDF2 해시와 고정시간 비교한다."""
    if not pbkdf2 or not pbkdf2.get("salt") or not pbkdf2.get("hash"):
        return False
    try:
        salt = base64.b64decode(pbkdf2["salt"])
        expected = base64.b64decode(pbkdf2["hash"])
    except (ValueError, KeyError):
        return False
    iterations = int(pbkdf2.get("iterations", PBKDF2_ITERATIONS))
    actual = hashlib.pbkdf2_hmac(
        "sha256", password.encode("utf-8"), salt, iterations, len(expected)
    )
    # 타이밍 공격 방지 — CryptographicOperations.FixedTimeEquals 와 동일 의도
    return hmac.compare_digest(actual, expected)


# ──────────────────────────────────────────────────────────────────
# core_access.json 입출력
# ──────────────────────────────────────────────────────────────────
def load_doc(path: Path) -> dict:
    """core_access.json 을 읽는다. 없으면 빈 문서."""
    if not path.exists():
        return {"schema": SCHEMA_VERSION, "entries": []}
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as ex:
        sys.exit(f"[오류] {path} 가 올바른 JSON 이 아닙니다: {ex}")
    doc.setdefault("schema", SCHEMA_VERSION)
    doc.setdefault("entries", [])
    return doc


def save_doc(path: Path, doc: dict) -> None:
    """core_access.json 을 저장한다. 기존 파일은 .bak 로 백업."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        backup = path.with_suffix(path.suffix + ".bak")
        backup.write_bytes(path.read_bytes())
    path.write_text(
        json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def find_entry(doc: dict, emp_id: str):
    """사번으로 항목을 찾는다 (대소문자 무시 — C# OrdinalIgnoreCase 와 일치)."""
    for e in doc["entries"]:
        if e.get("employeeId", "").lower() == emp_id.lower():
            return e
    return None


def prompt_password(emp_id: str) -> str:
    """비밀번호를 두 번 입력받아 일치를 확인한다."""
    while True:
        pw1 = getpass.getpass(f"  '{emp_id}' 의 새 비밀번호: ")
        if not pw1:
            sys.exit("[취소] 비밀번호가 비어 있습니다.")
        if len(pw1) < 8:
            print("  [주의] 8자 이상을 권장합니다. 다시 입력하세요.")
            continue
        pw2 = getpass.getpass("  비밀번호 확인: ")
        if pw1 != pw2:
            print("  [불일치] 두 입력이 다릅니다. 다시 입력하세요.")
            continue
        return pw1


# ──────────────────────────────────────────────────────────────────
# 서브커맨드
# ──────────────────────────────────────────────────────────────────
def cmd_init(args):
    path = Path(args.out)
    if path.exists() and not args.force:
        sys.exit(f"[중단] {path} 가 이미 존재합니다. 덮어쓰려면 --force.")
    save_doc(path, {"schema": SCHEMA_VERSION, "entries": []})
    print(f"[완료] 빈 권한 매트릭스를 생성했습니다: {path}")
    print("       이제 add 명령으로 사용자를 추가하세요.")


def cmd_add(args):
    path = Path(args.file)
    doc = load_doc(path)

    if args.role not in VALID_ROLES:
        sys.exit(f"[오류] 역할은 {VALID_ROLES} 중 하나여야 합니다.")
    if find_entry(doc, args.id):
        sys.exit(f"[중단] 사번 '{args.id}' 가 이미 있습니다. set-role/passwd 를 쓰세요.")

    entry = {
        "employeeId": args.id,
        "displayName": args.name,
        "role": args.role,
    }
    # ShellOnly 는 Core 접근이 없으므로 비밀번호 해시가 불필요하다.
    # 그 외 역할은 로컬 인증을 위해 비밀번호를 받는다(SSO 사용 시 생략 가능).
    if args.role == "ShellOnly":
        print("  [정보] ShellOnly 는 Core 접근이 없어 비밀번호를 두지 않습니다.")
    elif args.no_password:
        print("  [정보] --no-password — SSO 전용 계정으로 등록합니다 "
              "(로컬 비밀번호 인증 불가).")
    else:
        pw = prompt_password(args.id)
        entry["pbkdf2"] = make_pbkdf2(pw, args.iterations)

    doc["entries"].append(entry)
    save_doc(path, doc)
    print(f"[완료] '{args.id}' ({args.name}, {args.role}) 추가됨 → {path}")


def cmd_set_role(args):
    path = Path(args.file)
    doc = load_doc(path)
    if args.role not in VALID_ROLES:
        sys.exit(f"[오류] 역할은 {VALID_ROLES} 중 하나여야 합니다.")
    entry = find_entry(doc, args.id)
    if not entry:
        sys.exit(f"[오류] 사번 '{args.id}' 를 찾을 수 없습니다.")
    old = entry.get("role", "?")
    entry["role"] = args.role
    # ShellOnly 로 강등되면 비밀번호 해시는 의미가 없으므로 제거.
    if args.role == "ShellOnly" and "pbkdf2" in entry:
        del entry["pbkdf2"]
        print("  [정보] ShellOnly 강등 — 비밀번호 해시를 제거했습니다.")
    save_doc(path, doc)
    print(f"[완료] '{args.id}' 역할 변경: {old} → {args.role}")


def cmd_passwd(args):
    path = Path(args.file)
    doc = load_doc(path)
    entry = find_entry(doc, args.id)
    if not entry:
        sys.exit(f"[오류] 사번 '{args.id}' 를 찾을 수 없습니다.")
    if entry.get("role") == "ShellOnly":
        sys.exit("[중단] ShellOnly 계정에는 비밀번호를 둘 수 없습니다.")
    pw = prompt_password(args.id)
    entry["pbkdf2"] = make_pbkdf2(pw, args.iterations)
    save_doc(path, doc)
    print(f"[완료] '{args.id}' 의 비밀번호를 변경했습니다.")


def cmd_remove(args):
    path = Path(args.file)
    doc = load_doc(path)
    entry = find_entry(doc, args.id)
    if not entry:
        sys.exit(f"[오류] 사번 '{args.id}' 를 찾을 수 없습니다.")
    doc["entries"].remove(entry)
    save_doc(path, doc)
    print(f"[완료] '{args.id}' 를 삭제했습니다.")


def cmd_list(args):
    path = Path(args.file)
    doc = load_doc(path)
    entries = doc["entries"]
    if not entries:
        print(f"(비어 있음) {path}")
        return
    print(f"권한 매트릭스: {path}  (schema {doc.get('schema')})")
    print(f"{'사번':<14}{'이름':<16}{'역할':<16}{'로컬인증':<10}")
    print("-" * 56)
    for e in entries:
        has_pw = "있음" if e.get("pbkdf2") else "—(SSO/없음)"
        print(f"{e.get('employeeId',''):<14}{e.get('displayName',''):<16}"
              f"{e.get('role',''):<16}{has_pw:<10}")
    print("-" * 56)
    print(f"총 {len(entries)} 명")


def cmd_verify(args):
    """비밀번호 검증을 즉석에서 테스트한다 (운영 점검용)."""
    path = Path(args.file)
    doc = load_doc(path)
    entry = find_entry(doc, args.id)
    if not entry:
        sys.exit(f"[오류] 사번 '{args.id}' 를 찾을 수 없습니다.")
    if not entry.get("pbkdf2"):
        sys.exit(f"[정보] '{args.id}' 는 로컬 비밀번호가 없습니다 (SSO/ShellOnly).")
    pw = getpass.getpass(f"  '{args.id}' 의 비밀번호 입력: ")
    ok = verify_pbkdf2(pw, entry["pbkdf2"])
    if ok:
        print(f"[통과] 비밀번호가 일치합니다. 역할: {entry.get('role')}")
    else:
        sys.exit("[실패] 비밀번호가 일치하지 않습니다.")


# ──────────────────────────────────────────────────────────────────
def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        prog="manage_access.py",
        description="NowMoment v4.1 Secure-Verify 권한 매트릭스 관리 도구",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="역할:\n  " + "\n  ".join(f"{r:<14}{ROLE_DESC[r]}" for r in VALID_ROLES),
    )
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("init", help="빈 core_access.json 생성")
    p.add_argument("--out", default="core_access.json", help="출력 경로")
    p.add_argument("--force", action="store_true", help="기존 파일 덮어쓰기")
    p.set_defaults(func=cmd_init)

    p = sub.add_parser("add", help="사용자 추가")
    p.add_argument("--file", default="core_access.json")
    p.add_argument("--id", required=True, help="사번 (예: SPL-001)")
    p.add_argument("--name", required=True, help="표시 이름")
    p.add_argument("--role", required=True, help=f"{VALID_ROLES}")
    p.add_argument("--no-password", action="store_true",
                   help="SSO 전용 계정 — 로컬 비밀번호 없이 등록")
    p.add_argument("--iterations", type=int, default=PBKDF2_ITERATIONS)
    p.set_defaults(func=cmd_add)

    p = sub.add_parser("set-role", help="역할 변경")
    p.add_argument("--file", default="core_access.json")
    p.add_argument("--id", required=True)
    p.add_argument("--role", required=True, help=f"{VALID_ROLES}")
    p.set_defaults(func=cmd_set_role)

    p = sub.add_parser("passwd", help="비밀번호 변경")
    p.add_argument("--file", default="core_access.json")
    p.add_argument("--id", required=True)
    p.add_argument("--iterations", type=int, default=PBKDF2_ITERATIONS)
    p.set_defaults(func=cmd_passwd)

    p = sub.add_parser("remove", help="사용자 삭제")
    p.add_argument("--file", default="core_access.json")
    p.add_argument("--id", required=True)
    p.set_defaults(func=cmd_remove)

    p = sub.add_parser("list", help="전체 사용자 목록")
    p.add_argument("--file", default="core_access.json")
    p.set_defaults(func=cmd_list)

    p = sub.add_parser("verify", help="비밀번호 검증 테스트")
    p.add_argument("--file", default="core_access.json")
    p.add_argument("--id", required=True)
    p.set_defaults(func=cmd_verify)

    return ap


def main():
    args = build_parser().parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
