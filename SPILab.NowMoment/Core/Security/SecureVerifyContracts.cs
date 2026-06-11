// ════════════════════════════════════════════════════════════════════
// SecureVerifyContracts.cs — NowMoment v4.1 Phase 3 (작업 3)
//
// 개선 개발계획서 5장 "보안 확인 모드":
//   ISecureVerifyBackend 의 실제 구현에 필요한 자격증명 모델과
//   SSO 확장 계약을 정의한다.
//
//   계획서 5.1 은 인증 수단으로 "사번+비밀번호 또는 사내 SSO"를
//   제시한다. SSO 는 SPILab 사내 인증 시스템에 종속되므로, 본
//   파일은 SSO 를 직접 구현하지 않고 ISsoProvider 확장점만 둔다.
//   조직이 사내 SSO 어댑터를 작성해 LocalSecureVerifyBackend 에
//   주입하면, 게이트(SecureVerifyGate)·인가·감사 흐름은 그대로
//   두고 인증 단계만 SSO 로 대체된다 (계획서 8장 "백엔드만 교체").
// ════════════════════════════════════════════════════════════════════
using System;

namespace SPILab.NowMoment.Core.Security
{
    /// <summary>사용자가 입력한 인증 자격증명 (사번 + 비밀번호).</summary>
    public sealed class CoreCredential
    {
        public string EmployeeId { get; init; } = "";
        public string Password   { get; init; } = "";

        /// <summary>2차 인증 코드 (TOTP 등). 정책상 요구될 때만 채워진다.</summary>
        public string? SecondFactor { get; init; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(EmployeeId) || string.IsNullOrEmpty(Password);
    }

    /// <summary>인증(authentication) 단계의 결과 — 신원 확인까지만.</summary>
    public sealed class AuthOutcome
    {
        public bool Authenticated { get; init; }
        public string EmployeeId  { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string FailReason  { get; init; } = "";

        /// <summary>인증 수단 표기 (감사 로그용): "local" | "sso" 등.</summary>
        public string Method { get; init; } = "local";

        public static AuthOutcome Fail(string reason) =>
            new() { Authenticated = false, FailReason = reason };

        public static AuthOutcome Ok(string empId, string name, string method) =>
            new()
            {
                Authenticated = true,
                EmployeeId    = empId,
                DisplayName   = name,
                Method        = method,
            };
    }

    /// <summary>
    /// 사내 SSO 연동 확장점.
    ///
    /// 기본 제공 구현은 없다 — 조직이 사내 인증 시스템(예: Azure AD,
    /// 사내 LDAP, OAuth)에 맞는 어댑터를 작성해 주입한다.
    /// 미주입 시 LocalSecureVerifyBackend 는 로컬 비밀번호 검증으로
    /// 동작한다.
    /// </summary>
    public interface ISsoProvider
    {
        /// <summary>SSO 제공자 식별명 (감사 로그 Method 에 기록).</summary>
        string Name { get; }

        /// <summary>
        /// 자격증명으로 SSO 인증을 수행한다.
        /// 2차 인증이 SSO 측에서 처리되면 cred.SecondFactor 를 활용한다.
        /// </summary>
        AuthOutcome Authenticate(CoreCredential cred);
    }

    /// <summary>
    /// 2차 인증 검증 확장점 (TOTP/푸시 등).
    /// 로컬 백엔드에서 정책상 2FA 가 필요할 때 사용한다.
    /// SSO 가 2FA 를 자체 처리하면 이 확장점은 쓰지 않는다.
    /// </summary>
    public interface ISecondFactorVerifier
    {
        /// <summary>해당 사번에 대해 제출된 2차 인증 코드를 검증.</summary>
        bool Verify(string employeeId, string? code);
    }
}
