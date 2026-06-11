// ════════════════════════════════════════════════════════════════════
// KgBuilderRunner.Dynamic.cs — v2.7.8
//
// v2.7.7 → v2.7.8 변경:
//   * OutputJsonPath(KgDomain), OutputTtlPath(KgDomain) — 도메인 메타 기반 경로.
//   * OutputGeneratedCsPath(KgDomain) — 도메인별 C# 메타 파일 경로 추가.
//     (빌트인 photo 의 PhotoEngineMeta.cs 와 호환되도록 photo 일 때만 그 이름 유지)
//   * RunAsync 의 도메인 인식 정확화:
//     기존 RunAsync(scriptPath, srcPath, ...) 는 스크립트 파일명에서 도메인 추정 →
//     사용자 등록 도메인을 못 다룸.
//     새 오버로드 RunAsync(scriptPath, srcPath, domainCode, ...) 를 추가하고
//     KgViewModel.Builder.cs 가 이것을 호출하도록 변경.
//   * 출력 폴더는 모두 OutputDir (= %APPDATA%\Roaming\SPILab\NowMoment\kg_builder)
//     으로 강제 — 사용자가 입력한 OutputBasename 이 절대경로여도 무시하고
//     도메인 코드 기반의 기본 파일명으로 OutputDir 안에 생성.
//
// 사용 조건:
//   * KgBuilderRunner.cs 의 partial class 선언 (이전 단계에서 적용됨)
// ════════════════════════════════════════════════════════════════════
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    public partial class KgBuilderRunner
    {
        // ── 도메인 메타 기반 출력 경로 ─────────────────────────
        //
        //   ★ v2.7.8 정책:
        //     출력은 항상 OutputDir 안에 생성. 사용자의 OutputBasename 이 절대경로면
        //     그 경로를 무시하고 도메인 코드 기반 파일명을 강제 (코드 충돌 방지 + 권한 안전).

        public static string OutputJsonPath(KgDomain d)
            => Path.Combine(OutputDir, BasenameFor(d) + ".json");

        public static string OutputTtlPath(KgDomain d)
            => Path.Combine(OutputDir, BasenameFor(d) + ".ttl");

        /// <summary>
        /// dump 단계 산출물 (C# 메타 파일) 경로.
        /// 빌트인 photo 는 PhotoEngineMeta.cs 유지, 사용자 도메인은 {basename}.meta.cs.
        /// </summary>
        public static string OutputGeneratedCsPath(KgDomain d)
            => d.Code == "photo"
                ? Path.Combine(OutputDir, "PhotoEngineMeta.cs")
                : Path.Combine(OutputDir, BasenameFor(d) + ".meta.cs");

        /// <summary>
        /// 빌트인 5종은 사용자가 OutputBasename 을 안 줘도 잘 정의된 이름이 있고,
        /// 사용자 등록 도메인은 OutputBasename 이 비었으면 "kg_{code}" 자동 사용.
        /// 절대경로가 들어와도 파일명만 추출 (Path.GetFileNameWithoutExtension).
        /// </summary>
        private static string BasenameFor(KgDomain d)
        {
            // 사용자 입력이 있으면 그것을 우선
            if (!string.IsNullOrWhiteSpace(d.OutputBasename))
            {
                // 절대경로면 파일명만, 확장자 제거
                var nameOnly = Path.GetFileNameWithoutExtension(d.OutputBasename.Trim());
                if (!string.IsNullOrEmpty(nameOnly)) return nameOnly;
            }
            // 빌트인 5종 폴백
            return d.Code switch
            {
                "photo"    => "kg_raypann_photo",
                "cmp"      => "kg_raypann_cmp",
                "etch"     => "kg_raypann_etch",
                "thinfilm" => "kg_raypann_thinfilm",
                "cs"       => "kg_raypann_cs",
                _          => $"kg_{d.Code}",
            };
        }

        // ── 빌드 스크립트 위치 (절대경로 우선) ─────────────────
        public static string? LocateScript(KgDomain d)
        {
            var s = d.BuilderScript;
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (Path.IsPathRooted(s))
                    return File.Exists(s) ? s : null;
                var p = LocateInBuilderFolder(s);
                if (p != null) return p;
            }
            return LocateScript(d.Code);
        }

        // ════════════════════════════════════════════════════════
        //  ★ v2.7.8: RunAsync 오버로드 — 도메인 메타를 명시적으로 받음
        //
        //  이 오버로드를 사용하면 출력 경로가 도메인 메타 기반으로 결정.
        //  기존 시그니처는 그대로 유지되어 빌트인 호출자(스크립트 파일명 추정) 호환.
        // ════════════════════════════════════════════════════════
        public Task<BuildResult> RunAsync(
            string scriptPath, string srcPath, KgDomain domain,
            string? customPython = null,
            System.IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            return RunAsyncCore(scriptPath, srcPath, domain, customPython, progress, ct);
        }

        private async Task<BuildResult> RunAsyncCore(
            string scriptPath, string srcPath, KgDomain domain,
            string? customPython,
            System.IProgress<string>? progress,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = new BuildResult { ScriptPath = scriptPath };

            // 1) Python 검증 — 기존 RunAsync 와 동일
            if (string.IsNullOrWhiteSpace(customPython) || !File.Exists(customPython))
            {
                result.Success = false;
                result.PythonUsed = "(none)";
                result.StdErr =
                    "Python 실행파일 경로가 지정되지 않았거나 존재하지 않습니다.\n" +
                    "이 메서드는 호출자가 customPython 절대경로를 전달해야 합니다.";
                progress?.Report(result.StdErr);
                return result;
            }
            string exe = customPython;
            result.PythonUsed = exe;

            // 출력 폴더 생성 보장
            try { Directory.CreateDirectory(OutputDir); } catch { /* 이미 존재 */ }

            // 2) 인자 구성 — 도메인 메타 기반
            var scriptName = Path.GetFileName(scriptPath).ToLowerInvariant();
            string outArgs;
            string expectedJson = "";
            string expectedTtl  = "";
            string expectedCs   = "";

            bool isDumpStage = scriptName.Contains("dump");
            if (isDumpStage)
            {
                // dump 단계 — C# 메타 파일 한 개 생성
                expectedCs = OutputGeneratedCsPath(domain);
                outArgs = $" --out \"{expectedCs}\"";
            }
            else
            {
                // build 단계 — JSON + TTL 생성
                expectedJson = OutputJsonPath(domain);
                expectedTtl  = OutputTtlPath(domain);
                outArgs = $" --out-json \"{expectedJson}\" --out-ttl \"{expectedTtl}\"";
            }

            var args = $"\"{scriptPath}\" --src \"{srcPath}\"{outArgs}";
            progress?.Report($"실행: {exe} {args}");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? "",
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            try
            {
                using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) { sbOut.AppendLine(e.Data); progress?.Report(e.Data); } };
                p.ErrorDataReceived  += (_, e) => { if (e.Data != null) { sbErr.AppendLine(e.Data); progress?.Report(e.Data); } };

                if (!p.Start())
                {
                    result.Success = false;
                    result.StdErr = "프로세스 시작 실패";
                    return result;
                }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync(ct).ConfigureAwait(false);

                result.ExitCode = p.ExitCode;
                result.Success  = (p.ExitCode == 0);
                result.StdOut   = sbOut.ToString();
                result.StdErr   = sbErr.ToString();
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.StdErr  = $"실행 중 예외: {ex.Message}\n{sbErr}";
            }

            // 3) 결과 경로 채우기
            if (isDumpStage)
            {
                result.JsonPath = expectedCs;   // dump 산출물은 .cs 지만 "주 산출물" 슬롯에 넣어 BuildResult 호환 유지
                result.TtlPath  = "";
            }
            else
            {
                result.JsonPath = expectedJson;
                result.TtlPath  = expectedTtl;
            }

            // 산출물 존재 확인 (빌드 성공 보고 vs 실제 파일 부재 불일치 검출)
            if (result.Success)
            {
                if (isDumpStage && !File.Exists(expectedCs))
                {
                    result.Success = false;
                    result.StdErr += $"\n\n[검증 실패] dump 산출물이 생성되지 않았습니다:\n  {expectedCs}";
                }
                else if (!isDumpStage && !File.Exists(expectedJson))
                {
                    result.Success = false;
                    result.StdErr += $"\n\n[검증 실패] build 산출물이 생성되지 않았습니다:\n  {expectedJson}";
                }
            }

            result.Elapsed = sw.Elapsed;
            return result;
        }
    }
}
