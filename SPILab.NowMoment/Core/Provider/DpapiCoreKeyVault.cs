// ════════════════════════════════════════════════════════════════════
// DpapiCoreKeyVault.cs — NowMoment v4.1 Phase 3 (작업 5)
//
// 개선 개발계획서 5.1 단계 5·7 / 4.3:
//   ICoreKeyVault 의 구체 구현. .spc 번들·RULES 복호화에 필요한 키를
//   인가 통과 후 세션 단위로 발급하고, 세션 종료 시 폐기한다.
//
//   계획서 4.3: "복호화 키는 번들에 포함하지 않는다. Secure-Verify 가
//   인증을 마친 뒤 키 저장소에서 일시적으로 메모리에 로드하며,
//   프로세스 종료 시 폐기한다."
//
// 저장 방식:
//   키 파일은 Windows DPAPI(ProtectedData, CurrentUser 범위)로
//   암호화해 디스크에 둔다. 같은 Windows 사용자 계정에서만 복호화
//   가능 — 파일을 복사해 가도 다른 PC·계정에서는 못 푼다.
//
// 세션 수명:
//   IssueForSession() 이 DPAPI 복호화로 키를 메모리에 올리고,
//   rules_loader.py 가 읽을 수 있도록 환경변수 SPILAB_CORE_KEY 에
//   주입한다. RevokeSession() 이 메모리와 환경변수를 모두 비운다.
//
// ※ DPAPI 는 Windows 전용 API 다. 본 파일은 net8.0-windows 대상이며,
//   Linux 빌드 대상이 아니다 (WPF 앱과 동일 제약).
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Security.Cryptography;
using SPILab.NowMoment.Core.Security;

namespace SPILab.NowMoment.Core.Provider
{
    /// <summary>
    /// DPAPI 로 보호된 Core 키를 세션 단위로 발급·폐기한다.
    /// </summary>
    public sealed class DpapiCoreKeyVault : ICoreKeyVault
    {
        // rules_loader.py 가 키를 읽는 환경변수 이름 (build_pipeline 과 동일).
        private const string EnvKeyName = "SPILAB_CORE_KEY";

        private readonly string _protectedKeyPath;  // DPAPI 암호화된 키 파일
        private string? _activeSession;
        private byte[]? _liveKey;                    // 발급 중인 키 (메모리)

        public DpapiCoreKeyVault(string protectedKeyPath)
        {
            _protectedKeyPath = protectedKeyPath;
        }

        /// <summary>
        /// 세션용 키를 발급한다. DPAPI 복호화 → 메모리 로드 →
        /// 환경변수 주입. 성공 시 true.
        /// </summary>
        public bool IssueForSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return false;
            if (!File.Exists(_protectedKeyPath)) return false;

            try
            {
                byte[] protectedBytes = File.ReadAllBytes(_protectedKeyPath);

                // DPAPI 복호화 — 현재 Windows 사용자만 가능
                byte[] key = ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                if (key.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(key);
                    return false;
                }

                // 이전 세션 키가 남아 있으면 먼저 폐기
                ClearLiveKey();

                _liveKey = key;
                _activeSession = sessionId;

                // rules_loader.py 가 읽도록 hex 로 환경변수 주입.
                // 프로세스 환경 한정 — 자식 Python 프로세스가 상속받는다.
                Environment.SetEnvironmentVariable(
                    EnvKeyName, Convert.ToHexString(key),
                    EnvironmentVariableTarget.Process);

                return true;
            }
            catch (CryptographicException)
            {
                // DPAPI 복호화 실패 — 다른 사용자/PC 의 키이거나 손상됨
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>세션 키를 폐기한다 (계획서 5.1 단계 7).</summary>
        public void RevokeSession(string sessionId)
        {
            // 세션 ID 가 일치할 때만 폐기 (다른 세션 키를 실수로 지우지 않도록)
            if (_activeSession != null
                && !string.Equals(_activeSession, sessionId, StringComparison.Ordinal))
                return;

            ClearLiveKey();
            _activeSession = null;
            Environment.SetEnvironmentVariable(
                EnvKeyName, null, EnvironmentVariableTarget.Process);
        }

        private void ClearLiveKey()
        {
            if (_liveKey != null)
            {
                CryptographicOperations.ZeroMemory(_liveKey);
                _liveKey = null;
            }
        }

        /// <summary>
        /// 평문 키를 DPAPI 로 암호화해 키 파일을 만든다 (운영 도구).
        /// build_spc.py 가 생성한 bundle.key / core.key 를 이 메서드로
        /// DPAPI 보호 형태로 변환해 배치한다.
        /// </summary>
        public static void ProtectKeyFile(byte[] rawKey, string outputPath)
        {
            if (rawKey == null || rawKey.Length != 32)
                throw new ArgumentException("키는 32바이트여야 합니다.", nameof(rawKey));

            byte[] protectedBytes = ProtectedData.Protect(
                rawKey,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            File.WriteAllBytes(outputPath, protectedBytes);
        }
    }
}
