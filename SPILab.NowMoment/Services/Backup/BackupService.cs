// ════════════════════════════════════════════════════════════
// BackupService.cs — v3.0 F-003 DB 백업 (hotfix v2)
//
// nowmoment.db 파일을 zip 으로 압축하고 manifest.json 을 함께 묶는다.
// 외부 NuGet 의존성 없음 — .NET 8 표준 System.IO.Compression 사용.
//
// hotfix v2 변경: SQLite 가 NowMoment 앱에 의해 잠겨있는 상태에서도
// 안전하게 일관된 스냅샷을 만들기 위해 SqliteConnection.BackupDatabase()
// (SQLite 표준 BACKUP API) 를 사용하도록 변경.
//
// 사용 예:
//   var svc = new BackupService(db.DbPath, db.ConnStrPublic, () => db.GetStats());
//   svc.CreateBackup(@"C:\backups\nowmoment_backup_20260508_104412.zip");
//
// 출력 zip 구조:
//   nowmoment_backup_YYYYMMDD_HHMMSS.zip
//     ├── nowmoment.db        (SQLite BACKUP API 로 추출한 일관 스냅샷)
//     └── manifest.json       (BackupManifest 직렬화)
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SPILab.NowMoment.Services.Backup
{
    public class BackupService
    {
        private readonly string _dbPath;
        private readonly string _connStr;
        private readonly Func<Dictionary<string, int>>? _getCounts;

        /// <param name="dbPath">백업할 SQLite DB 파일 절대 경로 (manifest 기록용)</param>
        /// <param name="connStr">소스 SQLite 연결 문자열 (BACKUP API 호출용)</param>
        /// <param name="getCounts">자산 카운트를 반환하는 콜백 (manifest 기록용, null 가능)</param>
        public BackupService(string dbPath, string connStr,
                             Func<Dictionary<string, int>>? getCounts = null)
        {
            _dbPath    = dbPath;
            _connStr   = connStr;
            _getCounts = getCounts;
        }

        /// <summary>기본 백업 파일명 — nowmoment_backup_20260508_104412.zip</summary>
        public static string BuildDefaultFileName()
            => $"nowmoment_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

        /// <summary>지정된 zip 경로로 백업을 생성한다.
        /// 성공 시 manifest 를 반환, 실패 시 예외를 그대로 throw.</summary>
        public BackupManifest CreateBackup(string targetZipPath)
        {
            if (string.IsNullOrWhiteSpace(targetZipPath))
                throw new ArgumentException("백업 파일 경로가 비어있습니다.", nameof(targetZipPath));

            if (!File.Exists(_dbPath))
                throw new FileNotFoundException(
                    $"백업할 DB 파일을 찾을 수 없습니다: {_dbPath}", _dbPath);

            // 같은 이름의 zip 이 이미 있으면 덮어쓴다
            if (File.Exists(targetZipPath))
                File.Delete(targetZipPath);

            // 1) 임시 파일에 SQLite BACKUP API 로 일관 스냅샷 생성
            //    File.Copy 는 SQLite 가 잠근 .db 를 못 읽지만, BackupDatabase 는
            //    트랜잭션 일관성을 유지한 채 새 SQLite 파일을 만들어준다.
            string tempDb = Path.Combine(Path.GetTempPath(),
                $"nowmoment_backup_tmp_{Guid.NewGuid():N}.db");
            string tempConnStr = $"Data Source={tempDb}";
            try
            {
                using (var src = new SqliteConnection(_connStr))
                {
                    src.Open();
                    using (var dst = new SqliteConnection(tempConnStr))
                    {
                        dst.Open();
                        src.BackupDatabase(dst);
                    }
                }
                // Microsoft.Data.Sqlite 는 connection pooling 으로 dst 닫힌 후에도
                // 파일 핸들을 풀에 잡고 있어 다음 FileStream 읽기에서 락 충돌이 발생.
                // 풀을 명시적으로 비워 핸들을 해제해야 한다.
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // 2) manifest 작성 (해시는 임시 스냅샷 파일 기준)
                var fi = new FileInfo(tempDb);
                var manifest = new BackupManifest
                {
                    AppVersion     = GetAppVersion(),
                    CreatedAtUtc   = DateTime.UtcNow,
                    CreatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    AssetCounts    = _getCounts?.Invoke() ?? new Dictionary<string, int>(),
                    Sha256Hex      = BackupManifest.ComputeSha256Hex(tempDb),
                    FileSizeBytes  = fi.Length,
                    OriginalDbPath = _dbPath,
                    ManifestVersion= 1,
                };

                // 3) zip 생성
                using (var fs = new FileStream(targetZipPath, FileMode.Create, FileAccess.Write))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // 3-a) DB 스냅샷 추가
                    //      FileShare.ReadWrite — Microsoft.Data.Sqlite 의 잔여 핸들이
                    //      혹시 살아있더라도 read 가 가능하도록 공유 모드로 연다.
                    var dbEntry = archive.CreateEntry("nowmoment.db", CompressionLevel.Optimal);
                    using (var es = dbEntry.Open())
                    using (var srcFs = new FileStream(tempDb, FileMode.Open, FileAccess.Read,
                                                       FileShare.ReadWrite | FileShare.Delete))
                        srcFs.CopyTo(es);

                    // 3-b) manifest.json 추가
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var es = manifestEntry.Open())
                    using (var sw = new StreamWriter(es, new UTF8Encoding(false)))
                        sw.Write(manifest.ToJson());
                }

                return manifest;
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { /* 무시 */ }
            }
        }

        /// <summary>백업 zip 의 manifest 를 읽어 반환한다 (검증·진단용, 실제 복원은 수행하지 않음).</summary>
        public static BackupManifest? ReadManifest(string zipPath)
        {
            if (!File.Exists(zipPath)) return null;
            try
            {
                using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
                using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = archive.GetEntry("manifest.json");
                if (entry == null) return null;
                using var es = entry.Open();
                using var sr = new StreamReader(es, Encoding.UTF8);
                return BackupManifest.FromJson(sr.ReadToEnd());
            }
            catch
            {
                return null;
            }
        }

        // ── 내부 ─────────────────────────────────────────
        private static string GetAppVersion()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                return ver?.ToString(3) ?? "unknown";
            }
            catch { return "unknown"; }
        }
    }
}
