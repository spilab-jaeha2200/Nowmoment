// ════════════════════════════════════════════════════════════════════
// ClassifierUtil.cs — NowMoment v4.1 (Core 분리 / Phase 2 마무리)
//
// 자산 분류에 쓰이는 "IP 가 아닌" 유틸리티 모음.
//
//   계획서 2.2 의 보호 대상은 C4 = "분류 규칙·신뢰도 임계값" 이다.
//   아래 메서드들은 단순 파일 파싱(git config, pyproject.toml, VERSION,
//   언어 분포 카운팅 등)으로, 도메인 노하우가 아니다. 따라서 Shell 에
//   평문으로 남아도 무방하며, 외부 배포본에도 포함된다.
//
//   Core 휴리스틱(AssetClassifierCore) 과 폴백(AssetClassifierFallback)
//   이 모두 이 클래스를 주입받아 재사용한다 — 코드 중복 방지.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SPILab.NowMoment.Services.Import
{
    /// <summary>분류기 공용 유틸 — 메타데이터 추출·파일 읽기 헬퍼 (IP 아님).</summary>
    public sealed class ClassifierUtil
    {
        // ── 코드 언어 추정용 확장자 (자명한 매핑 — IP 아님) ──
        private static readonly Dictionary<string, string> LanguageByExt =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { ".py", "Python" }, { ".ipynb", "Python" },
            { ".cs", "C#" },
            { ".cpp", "C++" }, { ".cc", "C++" }, { ".cxx", "C++" }, { ".h", "C++" }, { ".hpp", "C++" },
            { ".c", "C" },
            { ".js", "JavaScript" }, { ".jsx", "JavaScript" },
            { ".ts", "TypeScript" }, { ".tsx", "TypeScript" },
            { ".java", "Java" },
            { ".rs", "Rust" },
            { ".go", "Go" },
            { ".m", "MATLAB" },
            { ".jl", "Julia" },
            { ".r", "R" },
        };

        /// <summary>git config 에서 origin remote 의 URL 을 추출. 없으면 null.</summary>
        public string? TryReadGitOrigin(string gitConfigPath)
        {
            try
            {
                if (!File.Exists(gitConfigPath)) return null;
                var lines = File.ReadAllLines(gitConfigPath);
                bool inOriginSection = false;
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (t.StartsWith("[remote ", StringComparison.OrdinalIgnoreCase))
                        inOriginSection = t.Contains("\"origin\"", StringComparison.OrdinalIgnoreCase);
                    else if (t.StartsWith("[")) inOriginSection = false;
                    else if (inOriginSection && t.StartsWith("url", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = t.IndexOf('=');
                        if (idx > 0) return t.Substring(idx + 1).Trim();
                    }
                }
            }
            catch { /* 무시 */ }
            return null;
        }

        /// <summary>디렉터리 안의 우세 언어를 파일 확장자 분포로 추정. 기본값 Python.</summary>
        public string GuessLanguage(string root)
        {
            try
            {
                var counts = new Dictionary<string, int>();
                int total = 0;

                void Count(string dir, int depth)
                {
                    if (depth > 2) return;
                    string[] files;
                    try { files = Directory.GetFiles(dir); } catch { return; }
                    foreach (var f in files)
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (LanguageByExt.TryGetValue(ext, out var lang))
                        {
                            counts[lang] = counts.GetValueOrDefault(lang) + 1;
                            total++;
                            if (total >= 200) return;
                        }
                    }
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(dir); } catch { return; }
                    foreach (var d in dirs)
                    {
                        if (SkipDir(Path.GetFileName(d))) continue;
                        Count(d, depth + 1);
                    }
                }
                Count(root, 0);

                if (counts.Count == 0) return "Python";
                return counts.OrderByDescending(kv => kv.Value).First().Key;
            }
            catch { return "Python"; }
        }

        private static bool SkipDir(string name) =>
            name is "node_modules" or "__pycache__" or ".venv" or "venv" or "env"
                 or "bin" or "obj" or ".git" or ".vs";

        /// <summary>VERSION 파일 / package.json / pyproject.toml 에서 버전 추출. 기본값 1.0.0.</summary>
        public string GuessVersion(string root)
        {
            try
            {
                foreach (var name in new[] { "VERSION", "version.txt", "VERSION.txt" })
                {
                    var p = Path.Combine(root, name);
                    if (File.Exists(p))
                    {
                        var v = File.ReadAllText(p).Trim();
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }

                var pkg = Path.Combine(root, "package.json");
                if (File.Exists(pkg))
                {
                    var v = ExtractJsonField(pkg, "version");
                    if (!string.IsNullOrEmpty(v)) return v;
                }

                var toml = Path.Combine(root, "pyproject.toml");
                if (File.Exists(toml))
                {
                    foreach (var line in File.ReadLines(toml).Take(50))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("version", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = t.IndexOf('=');
                            if (idx > 0)
                            {
                                var v = t.Substring(idx + 1).Trim().Trim('"', '\'');
                                if (!string.IsNullOrEmpty(v)) return v;
                            }
                        }
                    }
                }
            }
            catch { }
            return "1.0.0";
        }

        private static string ExtractJsonField(string path, string field)
        {
            try
            {
                var json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(field, out var prop)
                    && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                    return prop.GetString() ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>파일의 첫 비어있지 않은 줄을 읽는다 (주석 기호 제거). 길면 잘라낸다.</summary>
        public string? TryReadFirstLine(string path, int maxLen = 200)
        {
            try
            {
                using var sr = new StreamReader(path);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    var t = line.TrimStart('#', ' ', '\t').Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    return t.Length <= maxLen ? t : t.Substring(0, maxLen) + "…";
                }
            }
            catch { }
            return null;
        }

        /// <summary>파일 전체를 읽되 maxBytes 초과 시 null.</summary>
        public string? TryReadAll(string path, int maxBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > maxBytes) return null;
                return File.ReadAllText(path);
            }
            catch { return null; }
        }

        /// <summary>바이트 수를 사람이 읽는 크기 문자열로.</summary>
        public string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
