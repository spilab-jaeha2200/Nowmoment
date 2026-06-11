// ════════════════════════════════════════════════════════════════════
// SecureVerifyGate.cs — NowMoment v4.1 (Phase 3 골격)
//
// 개선 개발계획서 5장 "보안 확인 모드(Secure-Verify Mode)" 의 골격 구현.
//
// ★ 범위 안내:
//   본 파일은 인증·인가·감사의 *흐름과 계약*을 제공한다.
//   실제 사내 SSO 연동, 2차 인증, AES-256-GCM .spc 번들 복호화는
//   조직의 인증서·키 저장소가 확정된 후 ResolveBackend() 에 백엔드를
//   끼워 넣어 완성한다(계획서 8장 권고).
//
//   백엔드가 없으면 이 게이트는 항상 거부한다 (Core 비활성).
//   Core 접근에는 예외 없이 정식 Secure-Verify 백엔드가 필요하다 —
//   환경변수 등의 우회 경로는 두지 않는다.
// ════════════════════════════════════════════════════════════════════
using System;

namespace SPILab.NowMoment.Core.Security
{
    /// <summary>계획서 5.2 권한 등급.</summary>
    public enum CoreRole
    {
        ShellOnly,       // Core 접근 불가 (외부 협력사·일반 사용자)
        CoreRunner,      // Core 기능 실행만 (KG 빌드 호출)
        CoreDeveloper,   // Core 코드 열람·수정·로컬 빌드
        CoreOwner,       // 번들 생성·서명·키 관리 전체
    }

    /// <summary>Secure-Verify 1회 인증 결과 = 한 세션.</summary>
    public sealed class CoreSession
    {
        public bool Granted { get; init; }
        public string SessionId { get; init; } = "";
        public string Actor { get; init; } = "";
        public CoreRole Role { get; init; } = CoreRole.ShellOnly;
        public DateTime IssuedAt { get; init; } = DateTime.Now;
        public string DenyReason { get; init; } = "";

        public static CoreSession Denied(string reason) =>
            new() { Granted = false, DenyReason = reason };
    }

    /// <summary>
    /// 실제 인증·인가·키 발급을 수행하는 백엔드 계약.
    /// Phase 3 에서 SSO/2FA/키저장소 연동 구현체를 여기에 끼운다.
    /// </summary>
    public interface ISecureVerifyBackend
    {
        CoreSession Verify();
    }

    /// <summary>
    /// Secure-Verify 게이트. Core 접근 진입점.
    /// AuditSink 를 통해 모든 시도(성공/실패/거부)를 감사 로그에 적재한다.
    /// </summary>
    public sealed class SecureVerifyGate
    {
        private readonly ISecureVerifyBackend? _backend;
        private readonly Action<string, string, string>? _audit; // action, actor, detail
        private CoreSession? _current;

        /// <param name="backend">Phase 3 인증 백엔드. null 이면 기본(잠금) 정책.</param>
        /// <param name="auditSink">감사 로그 적재 콜백 (action, actorRole, detail).</param>
        public SecureVerifyGate(
            ISecureVerifyBackend? backend = null,
            Action<string, string, string>? auditSink = null)
        {
            _backend = backend;
            _audit = auditSink;
        }

        /// <summary>현재 세션이 인가되어 Core 사용이 가능한지.</summary>
        public bool IsUnlocked => _current?.Granted == true;

        public CoreSession? Current => _current;

        /// <summary>
        /// Core 접근 인증을 수행한다. 앱 실행당 1회만 호출하면 세션이 유지된다
        /// (계획서 7.2 — 인증 피로 완화: 세션 단위 인증).
        /// </summary>
        public CoreSession Unlock()
        {
            if (IsUnlocked) return _current!;

            CoreSession session;
            if (_backend != null)
            {
                // Phase 3: 실제 SSO/2FA 백엔드 경유
                session = _backend.Verify();
            }
            else
            {
                // 백엔드 미구성 — Core 접근 거부. 우회 경로는 없다.
                session = CoreSession.Denied(
                    "Secure-Verify 인증 백엔드가 구성되지 않았습니다. " +
                    "Core 기능은 사용할 수 없습니다.");
            }

            _current = session;

            // 계획서 5.3 — 모든 인증 시도를 audit_log 에 적재
            if (session.Granted)
                _audit?.Invoke("core.verify", session.Role.ToString(),
                    $"session={session.SessionId} actor={session.Actor}");
            else
                _audit?.Invoke("core.denied", "ShellOnly",
                    $"reason={session.DenyReason}");

            return session;
        }

        /// <summary>세션 종료 — 복호화 데이터·키 폐기 (계획서 5.1 단계 6).</summary>
        public void Lock()
        {
            if (_current?.Granted == true)
                _audit?.Invoke("core.lock", _current.Role.ToString(),
                    $"session={_current.SessionId}");
            _current = null;
        }
    }
}
