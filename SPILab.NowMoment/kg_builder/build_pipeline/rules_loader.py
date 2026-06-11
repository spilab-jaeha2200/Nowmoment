#!/usr/bin/env python3
# ════════════════════════════════════════════════════════════════════
# rules_loader.py — NowMoment v4.1 Phase 3 (작업 1)
#
# 개선 개발계획서 4.2 / 4.3:
#   Cython 컴파일된 빌더(build_kg_*.stripped → .pyd)가 런타임에
#   호출하는 RULES 복호화 로더.
#
#   extract_rules.py 가 만든 rules/<domain>.enc (AES-256-GCM) 를
#   복호화하여 RULES 리스트를 돌려준다.
#
# 키 입수 경로 (우선순위):
#   1. 환경변수 SPILAB_CORE_KEY (hex 64자) — Secure-Verify(Phase 3
#      작업 3)가 인증 통과 후 프로세스 환경에 주입한다.
#   2. 환경변수 SPILAB_CORE_KEY_FILE 가 가리키는 키 파일.
#   → 키를 찾지 못하면 RuntimeError. 키 없이는 RULES 를 로드할 수
#     없다 = 무단 실행 차단 (계획서 4.1 L3).
#
# 보안 주의:
#   - 이 모듈 자체는 키를 담지 않는다. .pyd 와 함께 .spc 에 봉인되며,
#     키는 항상 외부(Secure-Verify)에서 주입된다.
#   - 복호화된 RULES 는 프로세스 메모리에만 존재하고 디스크에
#     평문으로 쓰지 않는다.
# ════════════════════════════════════════════════════════════════════
from __future__ import annotations

import json
import os
from functools import lru_cache
from pathlib import Path

try:
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
except ImportError as e:  # pragma: no cover
    raise RuntimeError("cryptography 패키지가 필요합니다") from e


NONCE_BYTES = 12
_ASSOCIATED_DATA = b"NowMomentRULESv1"  # extract_rules.py 와 동일해야 함

# .enc 파일이 놓이는 폴더. .spc 번들 전개 시 이 모듈과 같은 위치의
# rules/ 하위에 배치된다. 환경변수로 재지정 가능.
_RULES_DIR_ENV = "SPILAB_CORE_RULES_DIR"


def _resolve_key() -> bytes:
    """Secure-Verify 가 주입한 복호화 키를 환경에서 가져온다."""
    hex_key = os.environ.get("SPILAB_CORE_KEY")
    if hex_key:
        try:
            key = bytes.fromhex(hex_key.strip())
        except ValueError as e:
            raise RuntimeError("SPILAB_CORE_KEY 형식 오류 (hex 64자 필요)") from e
        if len(key) != 32:
            raise RuntimeError(f"키 길이 오류: {len(key)} bytes (32 필요)")
        return key

    key_file = os.environ.get("SPILAB_CORE_KEY_FILE")
    if key_file:
        p = Path(key_file)
        if not p.exists():
            raise RuntimeError(f"키 파일 없음: {key_file}")
        key = p.read_bytes()
        if len(key) != 32:
            raise RuntimeError(f"키 길이 오류: {len(key)} bytes (32 필요)")
        return key

    raise RuntimeError(
        "Core 복호화 키를 찾을 수 없습니다. "
        "Secure-Verify 인증을 통과해야 RULES 를 로드할 수 있습니다 "
        "(SPILAB_CORE_KEY 미설정)."
    )


def _rules_dir() -> Path:
    """.enc 파일 폴더 경로."""
    env = os.environ.get(_RULES_DIR_ENV)
    if env:
        return Path(env)
    return Path(__file__).parent / "rules"


@lru_cache(maxsize=8)
def load_rules(domain: str) -> list:
    """
    도메인(cs / cmp / etch / thinfilm)의 RULES 리스트를 복호화해 반환.

    stripped 빌더가 모듈 로드 시점에 한 번 호출한다. lru_cache 로
    같은 도메인 재호출 시 복호화를 반복하지 않는다.
    """
    enc_path = _rules_dir() / f"{domain}.enc"
    if not enc_path.exists():
        raise RuntimeError(f"암호화 RULES 파일 없음: {enc_path}")

    blob = enc_path.read_bytes()
    if len(blob) <= NONCE_BYTES:
        raise RuntimeError(f"{domain}.enc 손상 (크기 부족)")

    nonce, ciphertext = blob[:NONCE_BYTES], blob[NONCE_BYTES:]
    key = _resolve_key()

    try:
        aesgcm = AESGCM(key)
        plaintext = aesgcm.decrypt(nonce, ciphertext, _ASSOCIATED_DATA)
    except Exception as e:  # noqa: BLE001 - 복호화/인증 실패 일괄 처리
        raise RuntimeError(
            f"{domain}.enc 복호화 실패 — 키 불일치 또는 번들 변조 "
            f"(GCM 인증 태그 검증 실패)"
        ) from e

    rules = json.loads(plaintext.decode("utf-8"))
    if not isinstance(rules, list):
        raise RuntimeError(f"{domain} RULES 역직렬화 결과가 list 가 아님")
    return rules


def clear_cache() -> None:
    """복호화 캐시를 비운다 (세션 종료 시 호출 — 계획서 5.1 단계 7)."""
    load_rules.cache_clear()
