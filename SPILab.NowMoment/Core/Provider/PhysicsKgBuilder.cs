// ════════════════════════════════════════════════════════════════════
// PhysicsKgBuilder.cs — NowMoment v4.1 (Core 분리 / Phase 2)
//
// IKgBuilder 의 실제 구현체 = "SPILab Core" 측 코드.
//
// 이 클래스 자체는 IP 가 아니다 — 진짜 IP 는 kg_builder/ 폴더의
// Python 빌더(build_kg_*.py)와 그 안의 167 룰·수식·인용이다.
// PhysicsKgBuilder 는 그 페이로드를 "찾고 / 보안 게이트를 통과시키고 /
// 실행하는" 어댑터일 뿐이다.
//
// v4.0 의 검증된 KgBuilderRunner.RunAsync 로직을 그대로 재사용하므로
// 빌드 동작·산출물은 v4.0 과 100% 동일하다 (계획서 7.1 호환성).
//
// 보안 흐름 (계획서 5.1):
//   BuildAsync 호출 → SecureVerifyGate.Unlock() → 인가 확인 →
//   kg_builder 페이로드 존재·무결성 확인 → 실행 → 감사 로그 적재.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SPILab.NowMoment.Core.Contracts;
using SPILab.NowMoment.Core.Security;
using SPILab.NowMoment.Services;
using RunnerResult = SPILab.NowMoment.Services.BuildResult;
// 모호성 제거: 아래 코드의 BuildResult 는 모두 계약(Core.Contracts) 타입을 의미한다.
using BuildResult = SPILab.NowMoment.Core.Contracts.BuildResult;

namespace SPILab.NowMoment.Core.Provider
{
    public sealed class PhysicsKgBuilder : IKgBuilder
    {
        private static readonly string[] BuiltinDomains =
            { "cs", "photo", "cmp", "etch", "thinfilm" };

        private readonly SecureVerifyGate _gate;
        private readonly Action<string, string, string>? _audit;
        private readonly KgBuilderRunner _runner = new();

        public PhysicsKgBuilder(
            SecureVerifyGate gate,
            Action<string, string, string>? auditSink = null)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _audit = auditSink;
        }

        public IReadOnlyList<string> SupportedDomains => BuiltinDomains;

        public bool IsAvailable => true; // 페이로드 유무는 BuildAsync 시점에 도메인별로 판정

        public string UnavailableReason => "";

        public async Task<BuildResult> BuildAsync(
            BuildRequest request,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (request == null)
                return BuildResult.Fail("BuildRequest 가 null 입니다.");

            // ── 1) Secure-Verify 게이트 통과 (계획서 5.1) ──────────
            var session = _gate.Unlock();
            if (!session.Granted)
            {
                progress?.Report("[Secure-Verify] 인증 실패 — 빌드를 거부합니다.");
                return BuildResult.SkippedCore(
                    "Core 접근이 인가되지 않았습니다: " + session.DenyReason);
            }

            // CoreRunner 이상 권한만 빌드 실행 가능 (계획서 5.2)
            if (session.Role == CoreRole.ShellOnly)
            {
                _audit?.Invoke("core.denied", session.Role.ToString(),
                    "build rejected — insufficient role");
                return BuildResult.SkippedCore("권한 부족: KG 빌드는 Core-Runner 이상이 필요합니다.");
            }

            // ── 2) 빌더 스크립트(페이로드) 위치 확인 ───────────────
            string? script = request.ScriptPath
                              ?? KgBuilderRunner.LocateScript(request.Domain);
            if (script == null || !File.Exists(script))
            {
                return BuildResult.SkippedCore(
                    $"[{request.Domain}] 빌더 페이로드(kg_builder/build_kg_{request.Domain}.py) 를 " +
                    "찾을 수 없습니다. 외부 배포본에는 Core 페이로드가 포함되지 않습니다.");
            }

            // ── 3) Python 실행파일 확인 ────────────────────────────
            if (string.IsNullOrWhiteSpace(request.PythonExe) || !File.Exists(request.PythonExe))
                return BuildResult.Fail("Python 실행파일 경로가 지정되지 않았거나 존재하지 않습니다.");

            // ── 4) 빌드 실행 — v4.0 검증 로직(KgBuilderRunner) 재사용 ──
            _audit?.Invoke("core.build", session.Role.ToString(),
                $"domain={request.Domain} session={session.SessionId}");

            RunnerResult runnerResult;
            try
            {
                // 2단계 빌드 (dump → build) — photo 등
                if (!string.IsNullOrWhiteSpace(request.DumpScriptPath)
                    && File.Exists(request.DumpScriptPath))
                {
                    progress?.Report("1/2: C# 메타 생성 중...");
                    var dump = await _runner.RunAsync(
                        request.DumpScriptPath!, request.SourcePath, request.PythonExe,
                        progress, ct, explicitDomain: request.Domain).ConfigureAwait(false);
                    if (!dump.Success)
                        return Adapt(dump, session, request.Domain);

                    var generatedCs = KgBuilderRunner.OutputGeneratedCsPath(request.Domain);
                    if (!File.Exists(generatedCs))
                        return BuildResult.Fail("Dump 단계는 성공했으나 C# 메타 파일이 생성되지 않았습니다.");

                    progress?.Report("2/2: C# 메타 → KG JSON 변환 중...");
                    runnerResult = await _runner.RunAsync(
                        script, generatedCs, request.PythonExe,
                        progress, ct, explicitDomain: request.Domain).ConfigureAwait(false);
                }
                else
                {
                    // 1단계 빌드 — cs/cmp/etch/thinfilm + 사용자 cs_file
                    runnerResult = await _runner.RunAsync(
                        script, request.SourcePath, request.PythonExe,
                        progress, ct, explicitDomain: request.Domain).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return BuildResult.Fail("사용자가 빌드를 취소했습니다.");
            }
            catch (Exception ex)
            {
                return BuildResult.Fail("빌드 중 예외: " + ex.Message);
            }

            // ── 5) 산출물 워터마킹 (계획서 5.4) ────────────────────
            //   빌드 성공 시 KG JSON/TTL 에 세션·사용자·시각을 비가시
            //   인코딩한다. 노드/엣지 데이터는 건드리지 않는다.
            //   워터마킹 실패가 빌드 자체를 실패시키지는 않는다.
            if (runnerResult.Success)
            {
                try
                {
                    StampWatermark(runnerResult, session, request);
                }
                catch (Exception wex)
                {
                    progress?.Report("[워터마킹] 경고 — 산출물 워터마크 삽입 실패: "
                                     + wex.Message);
                }
            }

            return Adapt(runnerResult, session, request.Domain);
        }

        /// <summary>
        /// build_pipeline/watermark.py 를 호출해 KG JSON/TTL 에 출처
        /// 워터마크를 삽입한다 (계획서 5.4).
        /// </summary>
        private void StampWatermark(
            RunnerResult result, CoreSession session, BuildRequest request)
        {
            // watermark.py 는 build_kg_*.py 와 같은 빌드 파이프라인에 있다.
            string? script = request.ScriptPath
                              ?? KgBuilderRunner.LocateScript(request.Domain);
            if (script == null) return;

            string pipelineDir = Path.Combine(
                Path.GetDirectoryName(script) ?? ".", "build_pipeline");
            string wmScript = Path.Combine(pipelineDir, "watermark.py");
            string wmKey    = Path.Combine(pipelineDir, "wm.key");
            if (!File.Exists(wmScript)) return;   // 워터마킹 도구 없으면 조용히 생략

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = request.PythonExe,
                UseShellExecute = false,
                CreateNoWindow  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add(wmScript);
            psi.ArgumentList.Add("stamp");
            if (!string.IsNullOrEmpty(result.JsonPath) && File.Exists(result.JsonPath))
            {
                psi.ArgumentList.Add("--json");
                psi.ArgumentList.Add(result.JsonPath);
            }
            if (!string.IsNullOrEmpty(result.TtlPath) && File.Exists(result.TtlPath))
            {
                psi.ArgumentList.Add("--ttl");
                psi.ArgumentList.Add(result.TtlPath);
            }
            psi.ArgumentList.Add("--session"); psi.ArgumentList.Add(session.SessionId);
            psi.ArgumentList.Add("--actor");   psi.ArgumentList.Add(session.Actor);
            psi.ArgumentList.Add("--role");    psi.ArgumentList.Add(session.Role.ToString());
            psi.ArgumentList.Add("--domain");  psi.ArgumentList.Add(request.Domain);
            psi.ArgumentList.Add("--wm-key");  psi.ArgumentList.Add(wmKey);

            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(15_000);

            _audit?.Invoke("core.watermark", session.Role.ToString(),
                $"domain={request.Domain} session={session.SessionId}");
        }

        /// <summary>v4.0 KgBuilderRunner.BuildResult → 계약 BuildResult 변환.</summary>
        private static BuildResult Adapt(
            RunnerResult r, CoreSession session, string domain)
        {
            return new BuildResult
            {
                Success     = r.Success,
                ExitCode    = r.ExitCode,
                StdOut      = r.StdOut,
                StdErr      = r.StdErr,
                JsonPath    = r.JsonPath,
                TtlPath     = r.TtlPath,
                Elapsed     = r.Elapsed,
                PythonUsed  = r.PythonUsed,
                ScriptPath  = r.ScriptPath,
                CoreSession = session.SessionId,
                RuleCount   = -1, // 회귀 검증 단계에서 JSON 파싱으로 집계 (Phase 4)
            };
        }
    }
}
