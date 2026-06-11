#!/usr/bin/env python3
# ════════════════════════════════════════════════════════════════════
# build_spc.py — NowMoment v4.1 Phase 3 (작업 2)
#
# 개선 개발계획서 4.3 / 6.3:
#   Cython 컴파일된 빌더와 암호화된 RULES 를 단일 .spc(SPILab Core)
#   번들로 봉인한다.
#
#   .spc 번들 구조 (계획서 4.3):
#     SPILab.Core.spc            ← AES-256-GCM 으로 암호화된 ZIP
#     └ (복호화 후 내부 ZIP):
#        ├─ manifest.json        버전·도메인 목록·해시·서명메타
#        ├─ builders/            build_kg_*.pyd  (Cython 컴파일)
#        ├─ rules/               *.enc           (룰 메타데이터)
#        ├─ rules_loader.py      런타임 RULES 복호화 로더
#        └─ SIGNATURE            번들 서명 (Ed25519)
#
#   ※ 계획서 4.3 원안의 classifier/AssetClassifierCore.dll 은
#     v4.1 Phase 2 에서 설계가 바뀌었다. 분류 휴리스틱은 C# 소스
#     (kg_builder/classifier/AssetClassifierCore.cs)로 Shell 내부
#     빌드 시 컴파일되며, .spc 대상이 아니다. 따라서 본 번들은
#     Python 빌더(.pyd + .enc)만 봉인한다.
#
# 보호 계층 (계획서 4.1):
#   L2 패키징 — .pyd 컴파일 + RULES 암호화 (작업 1 완료분)
#   L3 인증   — 번들 AES-256-GCM 암호화. 복호화 키는 Secure-Verify 발급
#   L4 무결성 — manifest 의 SHA-256 해시 + Ed25519 서명. 변조 시 거부
#
# 사용:
#   # 번들 생성
#   python build_spc.py pack --native-dir <pyd폴더> --rules-dir <enc폴더> \
#       --loader rules_loader.py --out SPILab.Core.spc \
#       --bundle-key bundle.key --sign-key sign_ed25519.key
#
#   # 번들 검증 (서명·해시만 확인, 복호화 X)
#   python build_spc.py verify --spc SPILab.Core.spc --bundle-key bundle.key \
#       --verify-key sign_ed25519.pub
#
# 보안 주의:
#   - bundle.key (번들 복호화 키), sign_*.key (서명 개인키)는
#     .spc 에 절대 포함하지 않는다. 키 저장소에서 별도 관리한다.
#   - Secure-Verify(작업 3)가 인증 후 bundle.key 를 메모리에 발급한다.
# ════════════════════════════════════════════════════════════════════
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

# ★ v4.1: 출력 인코딩을 UTF-8 로 고정한다.
#   이 스크립트는 NowMoment 앱(C#)이 외부 프로세스로 호출하기도 한다.
#   그때 stdout 이 Windows 기본 코드페이지(cp949)로 잡히면, 메시지에
#   포함된 '—'(em dash) 등 비ASCII 문자를 출력하다 UnicodeEncodeError
#   가 발생해 검증이 실패한다. 어느 환경에서 호출되든 안전하도록
#   stdout/stderr 를 UTF-8 로 재설정한다.
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except (AttributeError, ValueError):
    # 구버전 Python 등 reconfigure 미지원 환경 — 래퍼로 대체
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

try:
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
    from cryptography.hazmat.primitives.asymmetric.ed25519 import (
        Ed25519PrivateKey, Ed25519PublicKey,
    )
    from cryptography.exceptions import InvalidSignature
except ImportError:
    print("[ERROR] cryptography 패키지가 필요합니다: pip install cryptography",
          file=sys.stderr)
    sys.exit(2)


SPC_MAGIC = b"SPILABSPC1"     # .spc 파일 식별자 (10 bytes)
NONCE_BYTES = 12
KEY_BYTES = 32
BUNDLE_FORMAT_VERSION = "1.0"
_AAD = b"NowMomentSPCv1"      # AES-GCM associated data


# ──────────────────────────────────────────────────────────────
# 키 유틸
# ──────────────────────────────────────────────────────────────
def load_or_create_aes_key(key_file: Path) -> bytes:
    if key_file.exists():
        key = key_file.read_bytes()
        if len(key) != KEY_BYTES:
            raise ValueError(f"번들 키 길이 오류: {len(key)} (32 필요)")
        return key
    key = os.urandom(KEY_BYTES)
    key_file.write_bytes(key)
    try:
        os.chmod(key_file, 0o600)
    except OSError:
        pass
    print(f"  번들 키 신규 생성: {key_file}  (★ 키 저장소로 이동)")
    return key


def load_or_create_sign_key(key_file: Path) -> Ed25519PrivateKey:
    """Ed25519 서명 개인키 로드 또는 생성. 공개키는 .pub 로 함께 저장."""
    if key_file.exists():
        return Ed25519PrivateKey.from_private_bytes(key_file.read_bytes())
    priv = Ed25519PrivateKey.generate()
    raw = priv.private_bytes_raw()
    key_file.write_bytes(raw)
    try:
        os.chmod(key_file, 0o600)
    except OSError:
        pass
    pub_file = key_file.with_suffix(".pub")
    pub_file.write_bytes(priv.public_key().public_bytes_raw())
    print(f"  서명 키 신규 생성: {key_file} / 공개키: {pub_file}")
    return priv


# ──────────────────────────────────────────────────────────────
# pack — 번들 생성
# ──────────────────────────────────────────────────────────────
def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def collect_payload(native_dir: Path, rules_dir: Path,
                    loader: Path) -> tuple[dict, dict]:
    """
    번들에 담을 파일들을 수집한다.
    반환: (arcname → bytes 매핑, 파일별 sha256 매핑)
    """
    files: dict[str, bytes] = {}

    # builders/*.pyd 또는 *.so
    builders = sorted(native_dir.glob("build_kg_*.pyd")) + \
               sorted(native_dir.glob("build_kg_*.so"))
    if not builders:
        raise FileNotFoundError(
            f"{native_dir} 에 build_kg_*.pyd/.so 가 없습니다. "
            f"build_cython.py 를 먼저 실행하세요.")
    for b in builders:
        files[f"builders/{b.name}"] = b.read_bytes()

    # rules/*.enc
    encs = sorted(rules_dir.glob("*.enc"))
    if not encs:
        raise FileNotFoundError(
            f"{rules_dir} 에 *.enc 가 없습니다. "
            f"extract_rules.py 를 먼저 실행하세요.")
    for e in encs:
        files[f"rules/{e.name}"] = e.read_bytes()

    # rules_loader.py
    if not loader.exists():
        raise FileNotFoundError(f"로더 파일 없음: {loader}")
    files["rules_loader.py"] = loader.read_bytes()

    hashes = {name: _sha256(data) for name, data in files.items()}
    return files, hashes


def build_manifest(files: dict, hashes: dict) -> dict:
    domains = sorted(
        name.split("/")[-1].removesuffix(".enc")
        for name in files if name.startswith("rules/")
    )
    builders = sorted(
        name.split("/")[-1] for name in files if name.startswith("builders/")
    )
    return {
        "format": "SPILab.Core.spc",
        "format_version": BUNDLE_FORMAT_VERSION,
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "domains": domains,
        "builders": builders,
        "file_count": len(files),
        "files": hashes,            # arcname → sha256
    }


def pack(args: argparse.Namespace) -> int:
    native_dir = Path(args.native_dir)
    rules_dir = Path(args.rules_dir)
    loader = Path(args.loader)
    out_path = Path(args.out)

    print("=" * 60)
    print("  .spc 번들 빌드 (v4.1 Phase 3 작업 2)")
    print("=" * 60)

    files, hashes = collect_payload(native_dir, rules_dir, loader)
    manifest = build_manifest(files, hashes)
    print(f"  수집: 빌더 {len(manifest['builders'])}개, "
          f"룰 {len(manifest['domains'])}개 도메인, 로더 1개")

    # 서명 — manifest 의 정규화 JSON 에 Ed25519 서명
    sign_key = load_or_create_sign_key(Path(args.sign_key))
    manifest_bytes = json.dumps(manifest, ensure_ascii=False,
                                sort_keys=True).encode("utf-8")
    signature = sign_key.sign(manifest_bytes)

    # 내부 ZIP 구성: manifest.json + payload + SIGNATURE
    inner = io.BytesIO()
    with zipfile.ZipFile(inner, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("manifest.json", manifest_bytes)
        zf.writestr("SIGNATURE", signature)
        for arcname, data in files.items():
            zf.writestr(arcname, data)
    inner_bytes = inner.getvalue()

    # 외부 암호화: AES-256-GCM (계획서 4.3)
    bundle_key = load_or_create_aes_key(Path(args.bundle_key))
    nonce = os.urandom(NONCE_BYTES)
    ciphertext = AESGCM(bundle_key).encrypt(nonce, inner_bytes, _AAD)

    # .spc 파일 = MAGIC + nonce + ciphertext
    out_path.write_bytes(SPC_MAGIC + nonce + ciphertext)

    print(f"\n  번들 생성: {out_path} ({out_path.stat().st_size:,} bytes)")
    print(f"  내부 평문 ZIP: {len(inner_bytes):,} bytes → AES-256-GCM 암호화")
    print(f"  서명: Ed25519 (manifest 무결성)")
    print()
    print("  ★ bundle.key / sign_ed25519.key 는 .spc 와 분리 보관")
    print("  ★ sign_ed25519.pub 는 Shell 의 Provider 로더에 내장 → 검증용")
    return 0


# ──────────────────────────────────────────────────────────────
# verify — 번들 무결성 검증 (복호화 후 서명·해시 확인)
# ──────────────────────────────────────────────────────────────
def open_spc(spc_path: Path, bundle_key: bytes) -> bytes:
    """.spc 를 복호화하여 내부 ZIP bytes 를 반환."""
    blob = spc_path.read_bytes()
    if not blob.startswith(SPC_MAGIC):
        raise ValueError("유효한 .spc 파일이 아닙니다 (매직 불일치)")
    body = blob[len(SPC_MAGIC):]
    nonce, ciphertext = body[:NONCE_BYTES], body[NONCE_BYTES:]
    try:
        return AESGCM(bundle_key).decrypt(nonce, ciphertext, _AAD)
    except Exception as e:  # noqa: BLE001
        raise ValueError(
            "번들 복호화 실패 — 키 불일치 또는 변조 (GCM 인증 실패)") from e


def verify(args: argparse.Namespace) -> int:
    spc_path = Path(args.spc)
    bundle_key = Path(args.bundle_key).read_bytes()

    print("=" * 60)
    print("  .spc 번들 검증")
    print("=" * 60)

    # 1) 복호화
    inner_bytes = open_spc(spc_path, bundle_key)
    print("  [1] 복호화 OK (AES-256-GCM 인증 통과)")

    # 2) ZIP 전개 + manifest/서명 추출
    with zipfile.ZipFile(io.BytesIO(inner_bytes)) as zf:
        names = set(zf.namelist())
        if "manifest.json" not in names or "SIGNATURE" not in names:
            raise ValueError("번들에 manifest.json 또는 SIGNATURE 누락")
        manifest_bytes = zf.read("manifest.json")
        signature = zf.read("SIGNATURE")
        manifest = json.loads(manifest_bytes)

        # 3) 서명 검증
        pub = Ed25519PublicKey.from_public_bytes(
            Path(args.verify_key).read_bytes())
        canonical = json.dumps(manifest, ensure_ascii=False,
                               sort_keys=True).encode("utf-8")
        try:
            pub.verify(signature, canonical)
            print("  [2] Ed25519 서명 검증 OK (manifest 무변조)")
        except InvalidSignature:
            raise ValueError("서명 검증 실패 — manifest 변조됨")

        # 4) 파일별 SHA-256 대조
        mismatch = 0
        for arcname, expected in manifest["files"].items():
            if arcname not in names:
                print(f"      [누락] {arcname}")
                mismatch += 1
                continue
            actual = _sha256(zf.read(arcname))
            if actual != expected:
                print(f"      [해시불일치] {arcname}")
                mismatch += 1
        if mismatch:
            raise ValueError(f"무결성 검증 실패 — {mismatch}개 파일 불일치/누락")
        print(f"  [3] 페이로드 해시 검증 OK "
              f"({len(manifest['files'])}개 파일 SHA-256 일치)")

    print()
    print(f"  번들 정상 — 도메인 {manifest['domains']}, "
          f"포맷 v{manifest['format_version']}")
    return 0


# ──────────────────────────────────────────────────────────────
def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=".spc 번들 빌드/검증 (v4.1 Phase 3 작업 2)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("pack", help="번들 생성")
    p.add_argument("--native-dir", required=True, help=".pyd/.so 폴더")
    p.add_argument("--rules-dir", required=True, help="*.enc 폴더")
    p.add_argument("--loader", default="rules_loader.py", help="로더 .py")
    p.add_argument("--out", required=True, help="출력 .spc 경로")
    p.add_argument("--bundle-key", default="bundle.key",
                   help="번들 AES-256 키 (없으면 생성)")
    p.add_argument("--sign-key", default="sign_ed25519.key",
                   help="Ed25519 서명 개인키 (없으면 생성)")

    v = sub.add_parser("verify", help="번들 무결성 검증")
    v.add_argument("--spc", required=True, help="검증할 .spc")
    v.add_argument("--bundle-key", required=True, help="번들 AES-256 키")
    v.add_argument("--verify-key", required=True,
                   help="Ed25519 공개키 (.pub)")

    args = ap.parse_args(argv)
    try:
        if args.cmd == "pack":
            return pack(args)
        if args.cmd == "verify":
            return verify(args)
    except (ValueError, FileNotFoundError) as e:
        print(f"\n[ERROR] {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
