// ════════════════════════════════════════════════════════════
// FolderScanner.cs — v3.0 F-002 폴더 임포트 (Step 2.1)
//
// 사용자가 지정한 루트 폴더를 재귀 스캔하여 자산 후보가 될 수 있는
// 항목 (.git 디렉터리, *.pt/.pdf 등 파일) 을 수집한다.
//
// 분류는 하지 않고 "원시 발견 항목" 만 반환 — 분류는 AssetClassifier 가 담당.
//
// 안전 장치:
//   - 깊이 제한 (기본 5단계) — 무한 루프 / 거대 폴더 방지
//   - 예외 디렉터리 무시 (.git 내부, node_modules, __pycache__, bin, obj, .venv 등)
//   - 권한 거부 / 심볼릭 링크 등 예외는 스킵하고 로그만 남김
//   - CancellationToken 으로 사용자 취소 가능
//   - 진행률 콜백 (Action<int, string> currentCount, currentPath)
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SPILab.NowMoment.Services.Import
{
    /// <summary>스캔된 1개 항목 — 파일 또는 분류용 마커 디렉터리(.git 같은).</summary>
    public class ScannedItem
    {
        public string FullPath  { get; set; } = "";
        public bool   IsDirectory { get; set; }
        public long   SizeBytes { get; set; }
        public DateTime LastWriteTime { get; set; }

        /// <summary>파일이면 확장자 (소문자, 점 포함: ".pt"). 디렉터리면 "".</summary>
        public string Extension { get; set; } = "";

        /// <summary>파일/폴더명 (경로 제외).</summary>
        public string Name { get; set; } = "";

        /// <summary>루트로부터의 상대 깊이 (0 = 루트 직속).</summary>
        public int Depth { get; set; }
    }

    public class FolderScanner
    {
        // 무시할 디렉터리 이름 (재귀 진입 안 함)
        private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg",
            "node_modules", "__pycache__", ".venv", "venv", "env",
            "bin", "obj", ".vs", ".idea",
            "build", "dist", ".next", ".nuxt",
            ".pytest_cache", ".mypy_cache", ".tox",
        };

        // 무시할 파일 이름
        private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Thumbs.db", ".DS_Store", "desktop.ini",
        };

        public int MaxDepth { get; set; } = 5;

        /// <summary>지정 루트를 재귀 스캔한다.
        /// .git 디렉터리를 만나면 스캔 결과에 포함하되 내부로 진입하지 않는다.</summary>
        public List<ScannedItem> Scan(string rootPath,
                                       CancellationToken ct = default,
                                       Action<int, string>? progress = null)
        {
            var result = new List<ScannedItem>();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return result;

            // 정규화
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);

            ScanRecursive(root, root, depth: 0, ct, progress, result);
            return result;
        }

        private void ScanRecursive(string root, string currentDir, int depth,
                                    CancellationToken ct,
                                    Action<int, string>? progress,
                                    List<ScannedItem> output)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > MaxDepth) return;

            // 현재 디렉터리의 파일들 수집
            string[] files;
            try { files = Directory.GetFiles(currentDir); }
            catch { files = Array.Empty<string>(); }   // 권한 거부 등은 스킵

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(file);
                    if (SkipFileNames.Contains(fi.Name)) continue;
                    if ((fi.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;

                    output.Add(new ScannedItem
                    {
                        FullPath      = fi.FullName,
                        IsDirectory   = false,
                        SizeBytes     = fi.Length,
                        LastWriteTime = fi.LastWriteTime,
                        Extension     = fi.Extension.ToLowerInvariant(),
                        Name          = fi.Name,
                        Depth         = depth,
                    });

                    if (output.Count % 50 == 0)
                        progress?.Invoke(output.Count, fi.FullName);
                }
                catch { /* 개별 파일 액세스 실패는 무시 */ }
            }

            // 하위 디렉터리 처리
            string[] dirs;
            try { dirs = Directory.GetDirectories(currentDir); }
            catch { dirs = Array.Empty<string>(); }

            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();

                string name;
                try
                {
                    var di = new DirectoryInfo(dir);
                    name = di.Name;
                    if ((di.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden
                        && !name.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                        continue;   // .git 은 hidden 이지만 분류 마커로 필요
                }
                catch { continue; }

                bool isMarker = name.Equals(".git", StringComparison.OrdinalIgnoreCase);

                // 스캔 결과에 디렉터리도 추가 (분류기가 .git 디렉터리를 신호로 사용)
                if (isMarker)
                {
                    output.Add(new ScannedItem
                    {
                        FullPath      = dir,
                        IsDirectory   = true,
                        Name          = name,
                        Depth         = depth + 1,
                        LastWriteTime = SafeGetLastWrite(dir),
                    });
                    // .git 내부는 진입하지 않음
                    continue;
                }

                if (SkipDirNames.Contains(name)) continue;

                ScanRecursive(root, dir, depth + 1, ct, progress, output);
            }
        }

        private static DateTime SafeGetLastWrite(string path)
        {
            try { return Directory.GetLastWriteTime(path); }
            catch { return DateTime.MinValue; }
        }
    }
}
