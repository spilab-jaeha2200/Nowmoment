// ════════════════════════════════════════════════════════════════════
// SpcBundleVerifier.cs — NowMoment v4.1 Phase 3 (작업 5)
//
// 개선 개발계획서 5.1 단계 4 "무결성 검증":
//   ICoreBundleVerifier 의 구체 구현. .spc 번들의 서명·해시를 검증한다.
//
//   검증 로직(AES-256-GCM 복호화 + Ed25519 서명 + 파일 해시 대조)은
//   이미 build_spc.py 의 verify 서브커맨드에 구현돼 있다. C# 에서
//   동일 로직을 재구현하면 두 곳을 동기화해야 하는 부담이 생기므로,
//   본 구현은 build_spc.py verify 를 외부 프로세스로 호출하고 종료
//   코드로 통과/실패를 판정한다 (단일 진실 출처).
//
//   build_spc.py 가 없는 외부 배포본에서는 어차피 Core 페이로드도
//   없어 이 검증 경로에 도달하지 않는다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SPILab.NowMoment.Core.Security;

namespace SPILab.NowMoment.Core.Provider
{
    /// <summary>
    /// build_spc.py verify 를 호출해 .spc 번들 무결성을 검증한다.
    /// </summary>
    public sealed class SpcBundleVerifier : ICoreBundleVerifier
    {
        private readonly string _pythonExe;       // python 실행 파일
        private readonly string _buildSpcScript;  // build_spc.py 경로
        private readonly string _bundleKeyPath;   // 번들 복호화 키
        private readonly int _timeoutMs;

        public SpcBundleVerifier(
            string pythonExe,
            string buildSpcScript,
            string bundleKeyPath,
            int timeoutMs = 30_000)
        {
            _pythonExe      = pythonExe;
            _buildSpcScript = buildSpcScript;
            _bundleKeyPath  = bundleKeyPath;
            _timeoutMs      = timeoutMs;
        }

        public BundleIntegrityResult Verify(string spcPath, string verifyKeyPath)
        {
            // 사전 조건 점검 — 도구·키 파일이 모두 있어야 검증 가능
            if (!File.Exists(_buildSpcScript))
                return BundleIntegrityResult.Fail(
                    $"검증 도구를 찾을 수 없습니다: {_buildSpcScript}");
            if (!File.Exists(_bundleKeyPath))
                return BundleIntegrityResult.Fail(
                    $"번들 복호화 키를 찾을 수 없습니다: {_bundleKeyPath}");
            if (!File.Exists(verifyKeyPath))
                return BundleIntegrityResult.Fail(
                    $"서명 검증 공개키를 찾을 수 없습니다: {verifyKeyPath}");
            if (!File.Exists(spcPath))
                return BundleIntegrityResult.Fail(
                    $".spc 번들을 찾을 수 없습니다: {spcPath}");

            // ★ v4.1: bundle.key 가 DPAPI 로 보호된 경우(.dpapi) 복호화 처리.
            //   build_spc.py verify 는 --bundle-key 파일을 평문 32바이트 AES
            //   키로 그대로 읽는다. .dpapi 파일을 그대로 넘기면 DPAPI 암호문을
            //   키로 쓰게 되어 GCM 인증이 실패한다("키 불일치 또는 변조").
            //   따라서 .dpapi 면 ProtectedData.Unprotect 로 평문 키를 복원해
            //   짧은 수명의 임시 파일에 쓰고, 검증이 끝나면 즉시 삭제한다.
            string keyArgPath = _bundleKeyPath;
            string? tempKeyPath = null;
            try
            {
                if (_bundleKeyPath.EndsWith(".dpapi",
                        StringComparison.OrdinalIgnoreCase))
                {
                    byte[] rawKey = ProtectedData.Unprotect(
                        File.ReadAllBytes(_bundleKeyPath),
                        optionalEntropy: null,
                        scope: DataProtectionScope.CurrentUser);
                    tempKeyPath = Path.Combine(
                        Path.GetTempPath(),
                        "nm_bk_" + Guid.NewGuid().ToString("N") + ".key");
                    File.WriteAllBytes(tempKeyPath, rawKey);
                    keyArgPath = tempKeyPath;
                }
            }
            catch (Exception ex)
            {
                return BundleIntegrityResult.Fail(
                    $"번들 키(DPAPI) 복호화에 실패했습니다: {ex.Message}");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = _pythonExe,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    // ★ v4.1: 자식 Python 의 입출력을 UTF-8 로 고정한다.
                    //   기본값이면 Windows 에서 stdout 이 cp949 로 잡혀,
                    //   build_spc.py 가 '—' 등 비ASCII 메시지를 출력할 때
                    //   UnicodeEncodeError 로 검증이 실패한다.
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.ArgumentList.Add(_buildSpcScript);
                psi.ArgumentList.Add("verify");
                psi.ArgumentList.Add("--spc");
                psi.ArgumentList.Add(spcPath);
                psi.ArgumentList.Add("--bundle-key");
                psi.ArgumentList.Add(keyArgPath);
                psi.ArgumentList.Add("--verify-key");
                psi.ArgumentList.Add(verifyKeyPath);

                using var proc = Process.Start(psi);
                if (proc == null)
                    return BundleIntegrityResult.Fail("검증 프로세스를 시작할 수 없습니다.");

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();

                if (!proc.WaitForExit(_timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return BundleIntegrityResult.Fail("번들 검증 시간 초과.");
                }

                // build_spc.py verify 는 통과 시 0, 실패 시 1 을 반환한다.
                if (proc.ExitCode == 0)
                    return BundleIntegrityResult.Pass();

                // 실패 — stderr 의 사유를 그대로 전달
                string reason = string.IsNullOrWhiteSpace(stderr)
                    ? $"검증 실패 (종료 코드 {proc.ExitCode})"
                    : stderr.Trim();
                return BundleIntegrityResult.Fail(reason);
            }
            catch (Exception ex)
            {
                return BundleIntegrityResult.Fail(
                    $"번들 검증 중 오류: {ex.Message}");
            }
            finally
            {
                // 평문 키 임시 파일은 검증 직후 즉시 삭제 (디스크 잔존 최소화)
                if (tempKeyPath != null)
                {
                    try { File.Delete(tempKeyPath); } catch { }
                }
            }
        }
    }
}
