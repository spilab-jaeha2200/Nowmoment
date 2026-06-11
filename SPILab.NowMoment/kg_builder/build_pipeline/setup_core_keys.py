#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# ════════════════════════════════════════════════════════════════════
# setup_core_keys.py — NowMoment v4.1 운영 도구
#
# 개선 개발계획서 4.3 / 5.1 단계 5 / 8장:
#   Secure-Verify 가 실제로 작동하려면 %APPDATA%\SPILab\NowMoment 에
#   다음 파일이 배치되어야 한다. 이 도구가 그 배치를 자동화한다.
#
#     core_access.json     ← manage_access.py 로 별도 생성 (이 도구 아님)
#     SPILab.Core.spc      ← build_spc.py pack 산출물
#     sign_ed25519.pub     ← build_spc.py 가 생성하는 서명 검증 공개키
#     bundle.key.dpapi     ← bundle.key 를 DPAPI 보호 변환
#     core.key.dpapi       ← core.key 를 DPAPI 보호 변환
#     build_pipeline/      ← build_spc.py + 의존 스크립트 (무결성 검증용)
#
# ★ DPAPI 변환은 Windows 전용이다. 이 도구는 두 모드로 동작한다:
#     - Windows  : pywin32 의 win32crypt 로 실제 DPAPI 보호 수행
#     - 비Windows: DPAPI 변환을 건너뛰고, 평문 키를 그대로 복사하되
#                  "Windows 에서 dpapi-protect 단계를 실행하라"고 안내.
#   DpapiCoreKeyVault.cs 는 DPAPI 로 보호된 파일만 복호화하므로,
#   최종 배포 PC(Windows)에서 dpapi-protect 를 반드시 한 번 실행해야 한다.
#
# 사용법:
#   # 1) 키·번들 생성 후 %APPDATA% 에 배치 (개발/배포 PC, Windows 권장)
#   python setup_core_keys.py deploy \
#       --spc      SPILab.Core.spc \
#       --bundle-key bundle.key \
#       --core-key   core.key \
#       --sign-pub   sign_ed25519.pub \
#       --pipeline-dir .
#
#   # 2) 비Windows 에서 만든 평문 키를 Windows 배포 PC 에서 DPAPI 보호로 전환
#   python setup_core_keys.py dpapi-protect --target-dir "%APPDATA%\SPILab\NowMoment"
#
#   # 3) 현재 배치 상태 점검
#   python setup_core_keys.py check
# ════════════════════════════════════════════════════════════════════
"""NowMoment v4.1 Secure-Verify 키·번들 배치 도구."""

import argparse
import os
import shutil
import sys
from pathlib import Path

IS_WINDOWS = os.name == "nt"

# App.xaml.cs 의 BuildSecureVerifyBackend() 가 읽는 경로·파일명과 일치해야 한다.
APPDATA_SUBPATH = ("SPILab", "NowMoment")
F_ACCESS   = "core_access.json"
F_SPC      = "SPILab.Core.spc"
F_SIGNPUB  = "sign_ed25519.pub"
F_BUNDLE   = "bundle.key.dpapi"      # App.xaml.cs: bundleKeyPath
F_COREKEY  = "core.key.dpapi"        # App.xaml.cs: DpapiCoreKeyVault protectedKeyPath
D_PIPELINE = "build_pipeline"        # App.xaml.cs: pipelineDir (build_spc.py 위치)

# DPAPI 미적용 평문 키의 임시 파일명 (비Windows 산출 → Windows 에서 변환 대기)
F_BUNDLE_RAW = "bundle.key.raw"
F_COREKEY_RAW = "core.key.raw"


def appdata_dir() -> Path:
    """%APPDATA%\\SPILab\\NowMoment 경로. 비Windows 는 ~/.config 로 모의."""
    if IS_WINDOWS:
        base = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
    return base.joinpath(*APPDATA_SUBPATH)


# ──────────────────────────────────────────────────────────────────
# DPAPI 보호 (Windows 전용)
# ──────────────────────────────────────────────────────────────────
def dpapi_protect(raw: bytes) -> bytes:
    """평문 키를 DPAPI(CurrentUser)로 보호한다. DpapiCoreKeyVault.cs 와 호환."""
    if not IS_WINDOWS:
        raise RuntimeError("DPAPI 보호는 Windows 에서만 가능합니다.")
    try:
        import win32crypt  # pywin32
    except ImportError:
        sys.exit("[오류] pywin32 가 필요합니다:  pip install pywin32")
    # CryptProtectData — DataProtectionScope.CurrentUser 와 동일
    # 반환 형식이 ProtectedData.Protect() 와 동일하므로 C# 측이 그대로 Unprotect 한다.
    blob = win32crypt.CryptProtectData(raw, None, None, None, None, 0)
    return blob


def read_key_32(path: Path, label: str) -> bytes:
    """32바이트 키 파일을 읽고 길이를 검증한다."""
    if not path.exists():
        sys.exit(f"[오류] {label} 키 파일을 찾을 수 없습니다: {path}")
    raw = path.read_bytes()
    if len(raw) != 32:
        sys.exit(f"[오류] {label} 키는 32바이트여야 합니다 (실제 {len(raw)}바이트): {path}")
    return raw


# ──────────────────────────────────────────────────────────────────
# deploy — 키·번들을 %APPDATA% 에 배치
# ──────────────────────────────────────────────────────────────────
def cmd_deploy(args):
    dst = appdata_dir()
    dst.mkdir(parents=True, exist_ok=True)
    print(f"[배치 대상] {dst}")

    # 1) .spc 번들
    spc = Path(args.spc)
    if not spc.exists():
        sys.exit(f"[오류] .spc 번들을 찾을 수 없습니다: {spc}\n"
                 f"       먼저 build_spc.py pack 으로 생성하세요.")
    shutil.copy2(spc, dst / F_SPC)
    print(f"  ✓ {F_SPC}")

    # 2) 서명 검증 공개키
    pub = Path(args.sign_pub)
    if not pub.exists():
        sys.exit(f"[오류] 서명 공개키를 찾을 수 없습니다: {pub}")
    shutil.copy2(pub, dst / F_SIGNPUB)
    print(f"  ✓ {F_SIGNPUB}")

    # 3) bundle.key / core.key — DPAPI 보호 or 평문 대기
    bundle_raw = read_key_32(Path(args.bundle_key), "bundle")
    core_raw   = read_key_32(Path(args.core_key), "core")

    if IS_WINDOWS:
        (dst / F_BUNDLE).write_bytes(dpapi_protect(bundle_raw))
        (dst / F_COREKEY).write_bytes(dpapi_protect(core_raw))
        print(f"  ✓ {F_BUNDLE}   (DPAPI 보호 — 현재 Windows 사용자 전용)")
        print(f"  ✓ {F_COREKEY}   (DPAPI 보호 — 현재 Windows 사용자 전용)")
    else:
        # 비Windows — 평문 키를 .raw 로 두고 Windows 에서 변환하도록 안내
        (dst / F_BUNDLE_RAW).write_bytes(bundle_raw)
        (dst / F_COREKEY_RAW).write_bytes(core_raw)
        print(f"  △ {F_BUNDLE_RAW}  (평문 — Windows 에서 dpapi-protect 필요)")
        print(f"  △ {F_COREKEY_RAW}  (평문 — Windows 에서 dpapi-protect 필요)")

    # 4) build_pipeline — 무결성 검증용 (build_spc.py + 의존 스크립트)
    pipe_src = Path(args.pipeline_dir)
    pipe_dst = dst / D_PIPELINE
    pipe_dst.mkdir(exist_ok=True)
    copied = 0
    for name in ("build_spc.py", "rules_loader.py"):
        f = pipe_src / name
        if f.exists():
            shutil.copy2(f, pipe_dst / name)
            copied += 1
    print(f"  ✓ {D_PIPELINE}/  ({copied} 개 스크립트)")
    if copied == 0:
        print(f"    [주의] {pipe_src} 에서 build_spc.py 를 찾지 못했습니다.")

    print()
    if IS_WINDOWS:
        print("[완료] Secure-Verify 키·번들 배치가 끝났습니다.")
        print("       core_access.json 은 manage_access.py 로 별도 배치하세요.")
    else:
        print("[부분 완료] 비Windows 환경 — 평문 키가 .raw 로 배치되었습니다.")
        print("            배포 Windows PC 에서 다음을 반드시 실행하세요:")
        print(f"              python setup_core_keys.py dpapi-protect")
    cmd_check(args, silent_header=True)


# ──────────────────────────────────────────────────────────────────
# dpapi-protect — 평문 .raw 키를 DPAPI 보호로 전환 (Windows 전용)
# ──────────────────────────────────────────────────────────────────
def cmd_dpapi_protect(args):
    if not IS_WINDOWS:
        sys.exit("[오류] 이 명령은 Windows 배포 PC 에서만 실행합니다.")
    dst = Path(args.target_dir) if args.target_dir else appdata_dir()
    print(f"[대상] {dst}")

    for raw_name, out_name, label in (
        (F_BUNDLE_RAW, F_BUNDLE, "bundle"),
        (F_COREKEY_RAW, F_COREKEY, "core"),
    ):
        raw_path = dst / raw_name
        if not raw_path.exists():
            print(f"  - {raw_name} 없음 — 건너뜀")
            continue
        raw = raw_path.read_bytes()
        if len(raw) != 32:
            sys.exit(f"[오류] {raw_name} 가 32바이트가 아닙니다.")
        (dst / out_name).write_bytes(dpapi_protect(raw))
        # 평문 키는 즉시 삭제 — 디스크에 평문이 남지 않도록
        try:
            raw_path.unlink()
        except OSError:
            pass
        print(f"  ✓ {out_name}   (DPAPI 보호 완료, {raw_name} 삭제됨)")

    print("[완료] DPAPI 보호 전환이 끝났습니다.")


# ──────────────────────────────────────────────────────────────────
# check — 배치 상태 점검
# ──────────────────────────────────────────────────────────────────
def cmd_check(args, silent_header=False):
    dst = appdata_dir()
    if not silent_header:
        print(f"[점검] {dst}\n")

    required = [
        (F_ACCESS,  "권한 매트릭스 (manage_access.py 로 생성)"),
        (F_SPC,     "Core 암호화 번들"),
        (F_SIGNPUB, "번들 서명 검증 공개키"),
        (F_BUNDLE,  "DPAPI 보호 bundle.key"),
        (F_COREKEY, "DPAPI 보호 core.key"),
    ]
    all_ok = True
    print(f"  {'파일':<22}{'상태':<12}설명")
    print("  " + "-" * 64)
    for name, desc in required:
        p = dst / name
        if p.exists():
            mark = "✓ 있음"
        else:
            # .raw 평문 키가 있으면 '변환 대기' 로 구분 표시
            raw = {F_BUNDLE: F_BUNDLE_RAW, F_COREKEY: F_COREKEY_RAW}.get(name)
            if raw and (dst / raw).exists():
                mark = "△ 변환대기"
            else:
                mark = "✗ 없음"
                all_ok = False
        print(f"  {name:<22}{mark:<12}{desc}")

    pipe = dst / D_PIPELINE / "build_spc.py"
    print(f"  {D_PIPELINE + '/':<22}{'✓ 있음' if pipe.exists() else '✗ 없음':<12}"
          f"무결성 검증 도구 (build_spc.py)")
    if not pipe.exists():
        all_ok = False

    print("  " + "-" * 64)
    if all_ok:
        print("  → 모든 파일이 배치되었습니다. Secure-Verify 정식 인증이 작동합니다.")
    else:
        print("  → 누락 파일이 있습니다. 누락 시 해당 검증 단계에서 Core 가")
        print("    안전하게 거부됩니다(설계상 '데이터 미비 = 잠금').")
    return all_ok


# ──────────────────────────────────────────────────────────────────
def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        prog="setup_core_keys.py",
        description="NowMoment v4.1 Secure-Verify 키·번들 배치 도구",
    )
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("deploy", help="키·번들을 %APPDATA% 에 배치")
    p.add_argument("--spc", required=True, help="SPILab.Core.spc 경로")
    p.add_argument("--bundle-key", required=True, help="bundle.key (32바이트) 경로")
    p.add_argument("--core-key", required=True, help="core.key (32바이트) 경로")
    p.add_argument("--sign-pub", required=True, help="sign_ed25519.pub 경로")
    p.add_argument("--pipeline-dir", default=".",
                   help="build_spc.py 가 있는 폴더 (기본: 현재 폴더)")
    p.set_defaults(func=cmd_deploy)

    p = sub.add_parser("dpapi-protect",
                       help="평문 .raw 키를 DPAPI 보호로 전환 (Windows 전용)")
    p.add_argument("--target-dir", default="",
                   help="대상 폴더 (기본: %%APPDATA%%\\SPILab\\NowMoment)")
    p.set_defaults(func=cmd_dpapi_protect)

    p = sub.add_parser("check", help="배치 상태 점검")
    p.set_defaults(func=cmd_check)

    return ap


def main():
    args = build_parser().parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
