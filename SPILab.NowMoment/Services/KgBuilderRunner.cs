// ════════════════════════════════════════════════════════════════════
// KgBuilderRunner.cs (v2.4)
//
// kg_builder/build_kg.py 를 외부 프로세스로 실행.
//
// 동작:
//   1) build_kg.py 위치 자동 탐색 (프로젝트 루트의 kg_builder/ 우선)
//   2) python.exe 또는 py -3 로 실행
//   3) stdout/stderr 캡처 + 비동기 await
//   4) 결과: BuildResult { Success, ExitCode, StdOut, StdErr, JsonPath, TtlPath, Elapsed }
//
// UI 스레드를 막지 않도록 RunAsync 비동기 메서드만 노출.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SPILab.NowMoment.Services
{
    public class BuildResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public string JsonPath { get; set; } = "";
        public string TtlPath { get; set; } = "";
        public TimeSpan Elapsed { get; set; }
        public string? PythonUsed { get; set; }
        public string? ScriptPath { get; set; }
    }

    public partial class KgBuilderRunner
    {
        /// <summary>
        /// 프로젝트 루트(.csproj 위치)의 kg_builder/build_kg_*.py 경로를 찾는다.
        /// domain="cs" → build_kg_cs.py, domain="photo" → build_kg_photo.py
        /// 못 찾으면 build_kg.py(legacy) 폴백, 그것도 없으면 null.
        /// </summary>
        /// <summary>
        /// kg_builder 출력 파일들이 저장되는 데이터 폴더.
        /// %APPDATA%\SPILab\NowMoment\kg_builder\ — 사용자 쓰기 가능.
        /// 폴더가 없으면 생성한다.
        /// </summary>
        public static string OutputDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SPILab", "NowMoment", "kg_builder");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// 도메인별 출력 JSON 파일 경로 (데이터 폴더 기준).
        /// ★ v2.7.13: default 가 kg_raypann_cs.json 이었던 버그 수정.
        ///   사용자 등록 도메인은 'kg_{domain}.json' 으로 분리 — cs 와 충돌 안 함.
        /// </summary>
        public static string OutputJsonPath(string domain) => Path.Combine(OutputDir, domain switch
        {
            "photo"    => "kg_raypann_photo.json",
            "cmp"      => "kg_raypann_cmp.json",
            "etch"     => "kg_raypann_etch.json",
            "thinfilm" => "kg_raypann_thinfilm.json",
            "cs"       => "kg_raypann_cs.json",
            _          => $"kg_{domain}.json",
        });

        /// <summary>
        /// 도메인별 출력 TTL 파일 경로.
        /// ★ v2.7.13: default 동적 키.
        /// </summary>
        public static string OutputTtlPath(string domain) => Path.Combine(OutputDir, domain switch
        {
            "photo"    => "kg_raypann_photo.ttl",
            "cmp"      => "kg_raypann_cmp.ttl",
            "etch"     => "kg_raypann_etch.ttl",
            "thinfilm" => "kg_raypann_thinfilm.ttl",
            "cs"       => "kg_raypann_cs.ttl",
            _          => $"kg_{domain}.ttl",
        });

        /// <summary>
        /// SimPhoto dump 단계의 중간 산출물 (PhotoEngineMeta.cs) 경로 (빌트인 photo 전용).
        /// </summary>
        public static string OutputGeneratedCsPath() => Path.Combine(OutputDir, "PhotoEngineMeta.cs");

        /// <summary>
        /// ★ v2.7.21+: 도메인 코드 기반 dump 산출물 경로.
        /// 빌트인 photo: PhotoEngineMeta.cs 유지 (호환).
        /// 사용자 python_engine_folder: kg_{code}.meta.cs (도메인별 분리).
        /// </summary>
        public static string OutputGeneratedCsPath(string domain)
        {
            if (domain == "photo") return Path.Combine(OutputDir, "PhotoEngineMeta.cs");
            return Path.Combine(OutputDir, $"kg_{domain}.meta.cs");
        }

        public static string? LocateScript(string domain = "cs")
        {
            string scriptName = domain switch
            {
                "photo"    => "build_kg_photo.py",
                "cmp"      => "build_kg_cmp.py",
                "etch"     => "build_kg_etch.py",
                "thinfilm" => "build_kg_thinfilm.py",
                "cs"       => "build_kg_cs.py",
                _          => "build_kg_cs.py",
            };
            var path = LocateInBuilderFolder(scriptName);
            if (path != null) return path;
            // legacy 호환: build_kg.py 가 남아있으면 cs 도메인용으로 사용
            if (domain == "cs") return LocateInBuilderFolder("build_kg.py");
            return null;
        }

        /// <summary>
        /// SimPhoto 만 사용 — 1단계 dump 스크립트(.py 폴더 → .cs 파일) 위치.
        /// 다른 도메인은 null 반환.
        /// </summary>
        public static string? LocateDumpScript(string domain)
        {
            if (domain != "photo") return null;
            return LocateInBuilderFolder("dump_photo_to_csharp.py");
        }

        /// <summary>
        /// SimPhoto 의 dump 스크립트가 생성하는 중간 .cs 파일 위치.
        /// 데이터 폴더(%APPDATA%\...\kg_builder\PhotoEngineMeta.cs) 기준.
        /// </summary>
        public static string? LocateGeneratedCs(string domain)
        {
            if (domain != "photo") return null;
            var path = OutputGeneratedCsPath();
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// 빌더가 생성한 KG JSON 파일 경로 (데이터 폴더 기준).
        /// 파일이 실제로 존재하면 그 경로를, 없으면 null 반환.
        /// </summary>
        public static string? LocateJson(string domain = "cs")
        {
            var path = OutputJsonPath(domain);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// kg_builder 폴더 안의 임의 파일 경로 해석 — 환경별 단일 위치 탐색.
        ///
        /// 개발 환경 (.csproj 발견됨):
        ///   &lt;.csproj 폴더&gt;/kg_builder/&lt;file&gt; 만 검사.
        ///   bin\Debug\... 같은 빌드 출력 폴더에는 빌더가 복사되지 않으므로 검사하지 않는다.
        ///
        /// 설치본 환경 (.csproj 미발견):
        ///   &lt;exe 폴더&gt;/kg_builder/&lt;file&gt; 만 검사.
        ///   예) C:\Program Files\SPILab\NowMoment\kg_builder\build_kg_cs.py
        ///
        /// %APPDATA% 는 데이터 전용 폴더(json/ttl 출력)이므로 빌더 .py 탐색 대상이 아니다.
        /// </summary>
        private static string? LocateInBuilderFolder(string fileName)
        {
            foreach (var p in EnumerateBuilderCandidates(fileName))
                if (File.Exists(p)) return p;
            return null;
        }

        /// <summary>
        /// 빌더 파일을 찾기 위해 검사하는 후보 경로를 yield.
        /// 개발 환경이면 프로젝트 루트만, 설치본이면 실행파일 폴더만 — 정확히 하나만 반환.
        /// LocateInBuilderFolder 와 GetSearchedPaths 가 공유하는 단일 진실 공급원.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<string> EnumerateBuilderCandidates(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 개발 환경 우선 판정: baseDir 에서 위로 8단계까지 .csproj 폴더 탐색
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                bool hasCsproj = false;
                try { hasCsproj = dir.EnumerateFiles("*.csproj").Any(); }
                catch { /* 권한 문제 등은 무시 */ }
                if (!hasCsproj) continue;

                // 개발 환경 — 프로젝트 루트의 kg_builder 만 반환하고 종료
                yield return Path.Combine(dir.FullName, "kg_builder", fileName);
                yield break;
            }

            // .csproj 못 찾음 → 설치본 환경 — 실행파일 폴더의 kg_builder
            yield return Path.Combine(baseDir, "kg_builder", fileName);
        }

        /// <summary>
        /// 진단용: LocateInBuilderFolder 가 검사하는 후보 경로 목록을 반환.
        /// 빌더 스크립트를 찾지 못했을 때 사용자에게 "어디를 봤는지" 안내하기 위함.
        /// 개발 환경이면 프로젝트 루트 1개, 설치본이면 실행파일 폴더 1개를 반환.
        /// </summary>
        public static System.Collections.Generic.List<string> GetSearchedPaths(string fileName)
        {
            return EnumerateBuilderCandidates(fileName).ToList();
        }

        /// <summary>
        /// 현재 실행 환경이 개발 환경(.csproj 트리)인지 설치본인지 판정.
        /// 안내 메시지에서 환경별로 다른 안내를 표시하기 위해 사용.
        /// </summary>
        public static bool IsDevEnvironment()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                try { if (dir.EnumerateFiles("*.csproj").Any()) return true; }
                catch { /* 권한 문제 등은 무시 */ }
            }
            return false;
        }

        /// <summary>build_kg.py 실행.</summary>
        /// <param name="customPython">사용할 Python 실행파일 절대경로(예: 아나콘다 가상환경의 python.exe). 호출자가 사전에 확보·검증한 값을 넘겨야 한다. null/빈값이면 즉시 실패 반환.</param>
        public async Task<BuildResult> RunAsync(
            string scriptPath, string srcPath,
            string? customPython = null,
            IProgress<string>? progress = null,
            CancellationToken ct = default,
            string? explicitDomain = null)   // ★ v2.7.21: 호출 측이 도메인 코드 명시 가능
        {
            var sw = Stopwatch.StartNew();
            var result = new BuildResult { ScriptPath = scriptPath };

            // 1) Python 실행파일 결정 — customPython 만 사용 (자동 탐색 없음)
            //    호출 측(KgViewModel)이 사전에 경로를 확보·저장한 후 전달해야 한다.
            string exe;
            string argsPrefix = "";
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
            exe = customPython;
            result.PythonUsed = exe;

            // 2) 인자 구성
            //    출력 폴더는 데이터 폴더(%APPDATA%\SPILab\NowMoment\kg_builder)로 강제.
            //    설치 폴더(Program Files)는 권한 없어 쓰기 불가.
            //    스크립트별 출력 인자:
            //      build_kg_*.py     → --out-json, --out-ttl  (KG 산출)
            //      dump_*_to_csharp.py / dump_*.py → --out (.meta.cs 단일 출력)
            var scriptName = Path.GetFileName(scriptPath).ToLowerInvariant();
            string outArgs;

            // ★ v2.7.22: dump 분기 조건을 사용자 dump 스크립트도 인식하도록 보강.
            //   이전: scriptName.Contains("dump") && scriptName.Contains("photo")  ← photo 만
            //   현재: scriptName.StartsWith("dump_") || scriptName.StartsWith("dump")
            //         예) dump_photo_to_csharp.py, dump_test3_to_csharp.py, dump_battery.py 모두 dump 로 인식
            bool isDumpScript = scriptName.StartsWith("dump_") || scriptName.StartsWith("dump");

            if (isDumpScript)
            {
                // ★ v2.7.22: 도메인별로 분리된 .meta.cs 출력 (PhotoEngineMeta.cs 덮어쓰기 방지)
                //   빌트인 photo → PhotoEngineMeta.cs (호환)
                //   사용자 도메인 → kg_{code}.meta.cs
                string dumpDomain = !string.IsNullOrWhiteSpace(explicitDomain)
                    ? explicitDomain!
                    : (scriptName.Contains("photo") ? "photo" : DomainFromScriptName(scriptName));
                var outCs = OutputGeneratedCsPath(dumpDomain);
                outArgs = $" --out \"{outCs}\"";
            }
            else
            {
                // build_kg_*.py — ★ v2.7.21: 호출 측이 explicitDomain 줬으면 그대로 사용
                //                 (스크립트 파일명에서 추정하는 폴백 제거 가능)
                //   이렇게 하면 빌드 산출물 파일명이 항상 도메인 코드 기반 (kg_{code}.json) 으로 일관됨.
                //   사용자가 OutputBasename 에 다른 값을 입력해도 무시됨 — 안전.
                string domain = !string.IsNullOrWhiteSpace(explicitDomain)
                    ? explicitDomain!
                    : DomainFromScriptName(scriptName);
                var outJson = OutputJsonPath(domain);
                var outTtl  = OutputTtlPath(domain);
                outArgs = $" --out-json \"{outJson}\" --out-ttl \"{outTtl}\"";
            }

            var args = string.IsNullOrEmpty(argsPrefix)
                ? $"\"{scriptPath}\" --src \"{srcPath}\"{outArgs}"
                : $"{argsPrefix} \"{scriptPath}\" --src \"{srcPath}\"{outArgs}";

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

            // PYTHONIOENCODING=utf-8 — 콘솔 한글 깨짐 방지
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            try
            {
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    sbOut.AppendLine(e.Data);
                    progress?.Report(e.Data);
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    sbErr.AppendLine(e.Data);
                    progress?.Report("[stderr] " + e.Data);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // 비동기 대기 (취소 토큰 지원)
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                result.ExitCode = proc.ExitCode;
                result.StdOut = sbOut.ToString();
                result.StdErr = sbErr.ToString();
                result.Success = proc.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.StdErr = "사용자가 빌드를 취소했습니다.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StdErr = "프로세스 실행 실패: " + ex.Message;
            }

            // 3) 출력 파일 위치 — 데이터 폴더(%APPDATA%\...\kg_builder) 기준
            //    위에서 --out / --out-json / --out-ttl 로 명시한 그 경로와 동일.
            //    scriptName / isDumpScript 는 인자 구성 단계에서 이미 결정되어 있다.
            if (isDumpScript)
            {
                // ★ v2.7.22: dump 산출물 — 도메인별 분리 (인자 구성과 동일 규칙)
                string dumpDomain = !string.IsNullOrWhiteSpace(explicitDomain)
                    ? explicitDomain!
                    : (scriptName.Contains("photo") ? "photo" : DomainFromScriptName(scriptName));
                result.JsonPath = OutputGeneratedCsPath(dumpDomain);
                result.TtlPath  = "";
            }
            else
            {
                // build_kg_*.py — ★ v2.7.21: explicitDomain 우선 사용 (인자 구성 단계와 동일 규칙)
                string resultDomain = !string.IsNullOrWhiteSpace(explicitDomain)
                    ? explicitDomain!
                    : DomainFromScriptName(scriptName);
                result.JsonPath = OutputJsonPath(resultDomain);
                result.TtlPath  = OutputTtlPath(resultDomain);
            }

            // 성공 판정 보강: exit=0 + JSON 파일 실제 생성됨
            if (result.Success && !File.Exists(result.JsonPath))
            {
                result.Success = false;
                if (string.IsNullOrEmpty(result.StdErr))
                    result.StdErr = "빌더가 종료코드 0으로 끝났지만 출력 JSON 파일이 생성되지 않았습니다.";
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            return result;
        }

        /// <summary>
        /// 스크립트 파일명(소문자)에서 도메인 코드를 자동 감지한다.
        /// 예: build_kg_cmp.py → "cmp",  build_kg_thinfilm.py → "thinfilm",
        ///     build_kg_test2.py → "test2",  build_kg_battery_pf.py → "battery_pf".
        /// ★ v2.7.13: 사용자 등록 도메인 (build_kg_{code}.py) 도 코드 정확히 추출.
        ///   이전 버전은 매칭 안 되면 무조건 "cs" 폴백 → cs 도메인 출력 파일을 덮어쓰는 버그.
        /// </summary>
        private static string DomainFromScriptName(string lowerScriptName)
        {
            // 우선 빌트인 5종 (특수 명명 규칙)
            if (lowerScriptName.Contains("thinfilm")) return "thinfilm";
            if (lowerScriptName.Contains("photo"))    return "photo";
            if (lowerScriptName.Contains("cmp"))      return "cmp";
            if (lowerScriptName.Contains("etch"))     return "etch";

            // ★ v2.7.13: build_kg_{code}.py 패턴에서 {code} 추출
            //   파일명만 들어왔든 전체 경로의 파일명만 추출되었든 (호출 측에서 Path.GetFileName 거침)
            //   대소문자 무시 비교를 위해 lowerScriptName 사용.
            const string prefix = "build_kg_";
            const string suffix = ".py";
            if (lowerScriptName.StartsWith(prefix) && lowerScriptName.EndsWith(suffix))
            {
                int start = prefix.Length;
                int end   = lowerScriptName.Length - suffix.Length;
                if (end > start)
                {
                    var code = lowerScriptName.Substring(start, end - start);
                    // build_kg_cs.py → "cs" 빌트인 매칭. build_kg_test2.py → "test2" 그대로.
                    return code;
                }
            }

            // 예외 케이스 — 파일명 패턴이 다른 빌드 스크립트는 폴백
            return "cs";
        }

    }
}
