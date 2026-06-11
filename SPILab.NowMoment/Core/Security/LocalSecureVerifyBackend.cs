// ════════════════════════════════════════════════════════════════════
// LocalSecureVerifyBackend.cs — NowMoment v4.1 Phase 3 (작업 3)
//
// 개선 개발계획서 5.1 "동작 시나리오":
//   ISecureVerifyBackend 의 실제 구현. SecureVerifyGate 가 Unlock()
//   시 호출하며, 계획서 5.1 의 2~6단계를 수행한다.
//
//   2. 개발자 인증  — SSO 주입 시 SSO, 아니면 로컬 비밀번호(+2FA)
//   3. 인가 확인    — CoreAccessMatrix 로 역할 조회, Shell-Only 거부
//   4. 무결성 검증  — .spc 번들 서명·해시 확인 (build_spc.py verify)
//   5. 키 발급      — 인증 통과 후에만 번들 키를 발급
//   6. 세션 수립    — CoreSession 반환
//
//   인증 단계만 ISsoProvider 주입으로 교체 가능하다. 미주입 시
//   로컬 비밀번호 검증으로 동작하므로, SSO 확정 전에도 Core 인증이
//   완결된다 (계획서 8장 "백엔드만 교체" 의도).
//
// 키 발급 경로:
//   .spc 복호화에 필요한 bundle.key 는 인가 통과 후 KeyVault 에서
//   읽어 CoreSession 에 싣는다. KeyVault 는 OS 보호 저장소(예:
//   Windows DPAPI 로 암호화된 파일)를 추상화한다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;

namespace SPILab.NowMoment.Core.Security
{
    /// <summary>
    /// LocalSecureVerifyBackend 구성 옵션. App 시작 시 경로·정책을 주입.
    /// </summary>
    public sealed class SecureVerifyOptions
    {
        /// <summary>권한 매트릭스 JSON 경로 (core_access.json).</summary>
        public string AccessMatrixPath { get; init; } = "";

        /// <summary>검증할 .spc 번들 경로. 비어 있으면 무결성 검증 생략.</summary>
        public string SpcBundlePath { get; init; } = "";

        /// <summary>.spc 번들 서명 검증용 공개키 경로 (sign_ed25519.pub).</summary>
        public string VerifyKeyPath { get; init; } = "";

        /// <summary>2차 인증 강제 여부. true 면 SecondFactorVerifier 필수.</summary>
        public bool RequireSecondFactor { get; init; }
    }

    /// <summary>인증 자격증명을 UI 등에서 수집하는 콜백 계약.</summary>
    public interface ICredentialPrompt
    {
        /// <summary>사용자에게 자격증명을 요청한다. 취소 시 null.</summary>
        CoreCredential? Prompt(bool requireSecondFactor);
    }

    /// <summary>
    /// 로컬 파일기반 Secure-Verify 백엔드.
    /// </summary>
    public sealed class LocalSecureVerifyBackend : ISecureVerifyBackend
    {
        private readonly SecureVerifyOptions _opt;
        private readonly ICredentialPrompt _prompt;
        private readonly ISsoProvider? _sso;                 // 선택 — 미주입 시 로컬 인증
        private readonly ISecondFactorVerifier? _secondFactor; // 선택
        private readonly ICoreBundleVerifier? _bundleVerifier; // 선택 — .spc 무결성
        private readonly ICoreKeyVault? _keyVault;             // 선택 — 키 발급

        public LocalSecureVerifyBackend(
            SecureVerifyOptions options,
            ICredentialPrompt credentialPrompt,
            ISsoProvider? sso = null,
            ISecondFactorVerifier? secondFactor = null,
            ICoreBundleVerifier? bundleVerifier = null,
            ICoreKeyVault? keyVault = null)
        {
            _opt            = options ?? throw new ArgumentNullException(nameof(options));
            _prompt         = credentialPrompt
                              ?? throw new ArgumentNullException(nameof(credentialPrompt));
            _sso            = sso;
            _secondFactor   = secondFactor;
            _bundleVerifier = bundleVerifier;
            _keyVault       = keyVault;
        }

        /// <summary>계획서 5.1 의 2~6단계를 수행한다.</summary>
        public CoreSession Verify()
        {
            // ── 2단계: 자격증명 수집 ──
            var cred = _prompt.Prompt(_opt.RequireSecondFactor);
            if (cred == null)
                return CoreSession.Denied("사용자가 인증을 취소했습니다.");
            if (cred.IsEmpty)
                return CoreSession.Denied("사번 또는 비밀번호가 비어 있습니다.");

            // ── 2단계: 인증 (SSO 주입 시 SSO, 아니면 로컬) ──
            AuthOutcome auth = (_sso != null)
                ? _sso.Authenticate(cred)
                : AuthenticateLocally(cred);

            if (!auth.Authenticated)
                return CoreSession.Denied($"인증 실패: {auth.FailReason}");

            // ── 2단계(보강): 2차 인증 ──
            if (_opt.RequireSecondFactor && _sso == null)
            {
                if (_secondFactor == null)
                    return CoreSession.Denied(
                        "2차 인증이 요구되지만 검증기가 구성되지 않았습니다.");
                if (!_secondFactor.Verify(auth.EmployeeId, cred.SecondFactor))
                    return CoreSession.Denied("2차 인증 코드가 올바르지 않습니다.");
            }

            // ── 3단계: 인가 — 권한 매트릭스 조회 ──
            var matrix = CoreAccessMatrix.Load(_opt.AccessMatrixPath);
            CoreRole role = matrix.RoleOf(auth.EmployeeId);
            if (role == CoreRole.ShellOnly)
                return CoreSession.Denied(
                    $"인가 거부: '{auth.EmployeeId}' 는 Core 접근 권한이 없습니다 " +
                    $"(역할: Shell-Only).");

            // ── 4단계: 무결성 검증 — .spc 번들 서명·해시 ──
            if (!string.IsNullOrEmpty(_opt.SpcBundlePath) && _bundleVerifier != null)
            {
                if (!File.Exists(_opt.SpcBundlePath))
                    return CoreSession.Denied(
                        $"Core 번들을 찾을 수 없습니다: {_opt.SpcBundlePath}");

                var integrity = _bundleVerifier.Verify(
                    _opt.SpcBundlePath, _opt.VerifyKeyPath);
                if (!integrity.Ok)
                    return CoreSession.Denied(
                        $"번들 무결성 검증 실패: {integrity.Reason}");
            }

            // ── 5단계: 키 발급 — 인가 통과 후에만 ──
            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            if (_keyVault != null)
            {
                if (!_keyVault.IssueForSession(sessionId))
                    return CoreSession.Denied(
                        "Core 복호화 키 발급에 실패했습니다 (키 저장소 접근 불가).");
            }

            // ── 6단계: 세션 수립 ──
            return new CoreSession
            {
                Granted   = true,
                SessionId = sessionId,
                Actor     = string.IsNullOrEmpty(auth.DisplayName)
                              ? auth.EmployeeId
                              : auth.DisplayName,
                Role      = role,
                IssuedAt  = DateTime.Now,
            };
        }

        /// <summary>SSO 미주입 시의 로컬 비밀번호 인증.</summary>
        private AuthOutcome AuthenticateLocally(CoreCredential cred)
        {
            var matrix = CoreAccessMatrix.Load(_opt.AccessMatrixPath);

            if (!matrix.Contains(cred.EmployeeId))
                return AuthOutcome.Fail("등록되지 않은 사번입니다.");

            if (!matrix.VerifyPassword(cred.EmployeeId, cred.Password))
                return AuthOutcome.Fail("비밀번호가 올바르지 않습니다.");

            return AuthOutcome.Ok(
                cred.EmployeeId,
                matrix.DisplayNameOf(cred.EmployeeId),
                method: "local");
        }
    }

    /// <summary>.spc 번들 무결성 검증 결과.</summary>
    public sealed class BundleIntegrityResult
    {
        public bool Ok { get; init; }
        public string Reason { get; init; } = "";

        public static BundleIntegrityResult Pass() => new() { Ok = true };
        public static BundleIntegrityResult Fail(string reason) =>
            new() { Ok = false, Reason = reason };
    }

    /// <summary>
    /// .spc 번들 무결성 검증 계약.
    /// 구현체는 build_spc.py 의 verify 와 동등한 검사(복호화·Ed25519
    /// 서명·해시 대조)를 수행한다 — 보통 외부 프로세스로 호출한다.
    /// </summary>
    public interface ICoreBundleVerifier
    {
        BundleIntegrityResult Verify(string spcPath, string verifyKeyPath);
    }

    /// <summary>
    /// Core 복호화 키 저장소 계약.
    /// 인가 통과 후 세션 단위로 키를 발급하고, 세션 종료 시 폐기한다
    /// (계획서 5.1 단계 7 — 세션 종료 시 키 폐기).
    /// </summary>
    public interface ICoreKeyVault
    {
        /// <summary>세션용 키를 발급(메모리 로드)한다. 성공 시 true.</summary>
        bool IssueForSession(string sessionId);

        /// <summary>세션 키를 폐기한다.</summary>
        void RevokeSession(string sessionId);
    }
}
