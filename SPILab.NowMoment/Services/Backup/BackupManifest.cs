// ════════════════════════════════════════════════════════════
// BackupManifest.cs — v3.0 F-003 DB 백업
//
// 백업 zip 내부에 함께 들어가는 메타데이터 파일.
// 사용자가 zip 파일만 보고도 "언제 만든 것인지, 어떤 NowMoment 버전인지,
// 자산이 몇 건이 들어있는지" 를 식별할 수 있도록 한다.
//
// 무결성 보장: SHA256Hex 가 nowmoment.db 파일의 SHA-256 체크섬과 일치하는지
// 사용자가 수동 검증 가능 (PowerShell `Get-FileHash`, OS sha256sum 등).
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SPILab.NowMoment.Services.Backup
{
    public class BackupManifest
    {
        /// <summary>NowMoment 앱 버전 (예: "3.0.0")</summary>
        public string AppVersion { get; set; } = "";

        /// <summary>백업 생성 시각 (UTC)</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>백업 생성 시각 (로컬 표시용)</summary>
        public string CreatedAtLocal { get; set; } = "";

        /// <summary>백업 시 자산 카운트 (소스코드/모델/문서/특허/실험)</summary>
        public Dictionary<string, int> AssetCounts { get; set; } = new();

        /// <summary>nowmoment.db 파일의 SHA-256 체크섬 (16진 소문자)</summary>
        public string Sha256Hex { get; set; } = "";

        /// <summary>nowmoment.db 파일 크기 (바이트)</summary>
        public long FileSizeBytes { get; set; }

        /// <summary>원본 DB 절대 경로 (디버그·기록용, 복원에는 사용하지 않음)</summary>
        public string OriginalDbPath { get; set; } = "";

        /// <summary>백업 형식 버전 (향후 호환성 검사를 위해)</summary>
        public int ManifestVersion { get; set; } = 1;

        // ── 헬퍼 ─────────────────────────────────────────
        public string ToJson()
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            return JsonSerializer.Serialize(this, opts);
        }

        public static BackupManifest? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<BackupManifest>(json); }
            catch { return null; }
        }

        /// <summary>지정된 파일의 SHA-256 체크섬을 16진 소문자 문자열로 반환.
        /// FileShare.ReadWrite — SQLite 의 잔여 락이 있더라도 읽기 가능하도록.</summary>
        public static string ComputeSha256Hex(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
            var hash = sha.ComputeHash(fs);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
