#!/usr/bin/env python3
# ════════════════════════════════════════════════════════════════════
# build_cython.py — NowMoment v4.1 Phase 3 (작업 1)
#
# 개선 개발계획서 4.2 1순위 / 6.3:
#   RULES 가 분리된 build_kg_*.stripped.py 를 Cython 으로 컴파일하여
#   네이티브 확장(Windows .pyd / Linux .so)으로 만든다.
#
#   파이프라인 전체:
#     build_kg_*.py
#       │  [extract_rules.py]
#       ├─→ build_kg_*.stripped.py ──[본 도구]──→ build_kg_*.pyd
#       └─→ rules/*.enc
#
#   .pyd 는 .spc 번들의 builders/ 에, .enc 는 rules/ 에 담긴다
#   (계획서 4.3 번들 구조).
#
# 사용:
#   python build_cython.py --src-dir <stripped 폴더> --out-dir <pyd 폴더>
#
# ★ v4.1 패치 — C 컴파일러 호출 방식 변경:
#   이전 버전은 Windows 에서 cl.exe 를 직접 subprocess 로 호출했다.
#   그러나 cl.exe 는 일반 PATH 에 없고 "x64 Native Tools Command
#   Prompt" 등 전용 환경에서만 잡히므로, Visual Studio Build Tools 가
#   설치돼 있어도 FileNotFoundError(WinError 2) 가 났다.
#
#   본 버전은 setuptools 의 build_ext 를 사용한다. setuptools 는
#   레지스트리에서 Visual Studio(또는 Build Tools) 설치 위치를 찾아
#   MSVC 환경을 자동으로 구성하므로, 일반 명령창·Anaconda Prompt
#   어디서 실행해도 cl.exe 를 찾을 수 있다. PATH 설정 불필요.
#
# 요구:
#   pip install cython setuptools
#   Windows: Visual Studio Build Tools — "C++를 사용한 데스크톱 개발"
#   Linux  : gcc
# ════════════════════════════════════════════════════════════════════
from __future__ import annotations

import argparse
import io
import os
import shutil
import sys
import tempfile
from pathlib import Path

# ★ v4.1: 출력 인코딩 UTF-8 고정 (앱이 외부 프로세스로 호출할 때
#   stdout 이 cp949 로 잡혀 '—' 등에서 UnicodeEncodeError 나는 것 방지).
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except (AttributeError, ValueError):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

STRIPPED_SUFFIX = ".stripped.py"


def check_tooling() -> None:
    """Cython / setuptools / C 컴파일러 가용성을 확인한다."""
    try:
        import Cython  # noqa: F401
    except ImportError:
        print("[ERROR] Cython 미설치: pip install cython", file=sys.stderr)
        sys.exit(2)

    try:
        import setuptools  # noqa: F401
    except ImportError:
        print("[ERROR] setuptools 미설치: pip install setuptools", file=sys.stderr)
        sys.exit(2)

    if sys.platform == "win32":
        # Windows: setuptools 가 MSVC 를 레지스트리에서 찾을 수 있는지 점검.
        if not _detect_msvc():
            print("[ERROR] Visual Studio Build Tools(MSVC)를 찾을 수 없습니다.",
                  file=sys.stderr)
            print("        Visual Studio Build Tools 를 설치하고 워크로드",
                  file=sys.stderr)
            print("        'C++를 사용한 데스크톱 개발' 을 선택하십시오.",
                  file=sys.stderr)
            print("        설치 후 명령창을 새로 열고 다시 실행하십시오.",
                  file=sys.stderr)
            sys.exit(2)
    else:
        if shutil.which("gcc") is None:
            print("[ERROR] gcc 를 찾을 수 없습니다.", file=sys.stderr)
            sys.exit(2)


def _detect_msvc() -> bool:
    """setuptools 경로로 MSVC(x64) 환경 검출. setuptools 버전차를 흡수."""
    # setuptools 신/구 버전 모두 시도
    for modpath in (
        "setuptools._distutils._msvccompiler",
        "distutils._msvccompiler",
    ):
        try:
            mod = __import__(modpath, fromlist=["_get_vc_env"])
            env = mod._get_vc_env("x64")
            if env and ("path" in env or "PATH" in env):
                return True
        except Exception:
            continue
    return False


def find_stripped(src_dir: Path) -> list[Path]:
    """build_kg_*.stripped.py 목록."""
    return sorted(src_dir.glob(f"build_kg_*{STRIPPED_SUFFIX}"))


def build_one(stripped: Path, work_dir: Path, out_dir: Path) -> Path:
    """
    .stripped.py -> .pyd(Windows) / .so(Linux).

    setuptools 의 build_ext 로 Cython 컴파일 + C 컴파일을 한 번에 수행한다.
    모듈명은 .stripped 를 떼어낸 이름(build_kg_cmp)으로 맞춘다 — 그래야
    PyInit_build_kg_cmp 가 생성되어 'build_kg_cmp' 로 import 된다.

    ★ v4.1 패치 — 경로 길이(WinError 206) 회피:
      Cython 의 build_dir 에 절대경로 소스를 넘기면, Cython 이
      build_dir + 소스절대경로 를 이어 붙여 중간 산출물 경로가 두 배로
      길어진다. Windows MAX_PATH(260자)를 넘기기 쉬우므로, 작업
      디렉터리를 work_dir 로 옮긴 뒤 *파일명만*(상대경로) 넘긴다.
      work_dir 자체도 호출부에서 짧은 임시 폴더로 잡는다.
    """
    from setuptools import Extension, Distribution
    from Cython.Build import cythonize

    module_name = stripped.name[: -len(STRIPPED_SUFFIX)]  # build_kg_cmp

    # chdir 이후에도 유효하도록 입력 경로를 미리 절대경로로 고정
    stripped_abs = stripped.resolve()
    out_dir_abs = out_dir.resolve()

    prev_cwd = os.getcwd()
    try:
        # 작업 디렉터리를 work_dir 로 — 이후 모든 경로를 상대로 처리
        os.chdir(work_dir)

        # 컴파일 입력은 모듈명.py 로 복사 (Cython 이 파일명으로 PyInit 결정)
        src_name = f"{module_name}.py"
        shutil.copyfile(stripped_abs, work_dir / src_name)

        # Cython: .py -> .c  — 상대경로(파일명)만 넘겨 경로 중첩 방지
        ext_modules = cythonize(
            [Extension(module_name, [src_name])],
            compiler_directives={"language_level": "3"},
            quiet=True,
            build_dir="_cyc",
        )

        # setuptools build_ext: .c -> .pyd/.so
        #   build_ext 가 MSVC(Windows) / gcc(Linux) 를 자동으로 찾아 호출한다.
        dist = Distribution({"name": module_name, "ext_modules": ext_modules})
        dist.script_args = ["build_ext"]
        cmd = dist.get_command_obj("build_ext")
        cmd.build_lib = "_lib"
        cmd.build_temp = "_tmp"
        cmd.inplace = 0
        cmd.ensure_finalized()
        cmd.run()

        # 생성된 .pyd/.so 를 out_dir 로 옮긴다
        ext = ".pyd" if sys.platform == "win32" else ".so"
        produced = None
        for p in Path("_lib").rglob(f"{module_name}*{ext}"):
            produced = p.resolve()
            break
        if produced is None:
            raise RuntimeError(
                f"{module_name}: 컴파일 산출물({ext})을 찾을 수 없습니다.")

        out_path = out_dir_abs / f"{module_name}{ext}"
        if out_path.exists():
            out_path.unlink()
        shutil.move(str(produced), str(out_path))
        return out_path
    finally:
        os.chdir(prev_cwd)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description="stripped 빌더를 Cython 컴파일 (v4.1 Phase 3 작업 1)")
    ap.add_argument("--src-dir", type=Path, required=True,
                    help="build_kg_*.stripped.py 가 있는 폴더 "
                         "(extract_rules.py 의 --out-dir)")
    ap.add_argument("--out-dir", type=Path, required=True,
                    help=".pyd/.so 출력 폴더")
    ap.add_argument("--keep-c", action="store_true",
                    help="중간 빌드 폴더 보존 (디버깅용)")
    args = ap.parse_args(argv)

    check_tooling()
    args.out_dir.mkdir(parents=True, exist_ok=True)

    # ★ v4.1 패치 — 작업 폴더를 시스템 임시 폴더(%TEMP%)에 둔다.
    #   프로젝트 경로가 길면(work-260420\...\build_pipeline\build_out)
    #   그 아래에 Cython 중간 산출물을 만들 때 Windows MAX_PATH(260자)를
    #   초과한다(WinError 206). %TEMP% 는 보통 짧으므로 여유가 생긴다.
    work_dir = Path(tempfile.mkdtemp(prefix="nm_cyc_"))

    stripped_files = find_stripped(args.src_dir)
    if not stripped_files:
        print(f"[ERROR] {args.src_dir} 에 *.stripped.py 가 없습니다. "
              f"extract_rules.py 를 먼저 실행하세요.", file=sys.stderr)
        return 1

    print("=" * 60)
    print(f"  Cython 컴파일 (v4.1 Phase 3 작업 1) — 플랫폼: {sys.platform}")
    print("=" * 60)

    built = []
    for stripped in stripped_files:
        name = stripped.name[: -len(STRIPPED_SUFFIX)]
        try:
            out_path = build_one(stripped, work_dir, args.out_dir)
            built.append(out_path)
            print(f"  [OK] {name}  ->  {out_path.name}")
        except Exception as e:
            print(f"  [FAIL] {name}: {e}", file=sys.stderr)
            return 1

    print()
    print("  [INFO] build_kg_photo.py 는 RULES 분리 대상이 아니므로")
    print("         --src-dir 에 build_kg_photo.stripped.py 를 함께 두면")
    print("         같이 컴파일됩니다. (photo 만 원본을 .stripped.py 로")
    print("          복사해 두십시오 - extract_rules 가 photo 는 만들지 않음.)")

    if not args.keep_c:
        shutil.rmtree(work_dir, ignore_errors=True)
    else:
        print(f"\n  중간 빌드 폴더 보존: {work_dir}")

    print()
    print(f"  완료 - {len(built)}개 네이티브 빌더 생성: {args.out_dir}")
    print("  * 다음: build_spc.py 로 .pyd + rules/*.enc 를 .spc 번들로 봉인")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
