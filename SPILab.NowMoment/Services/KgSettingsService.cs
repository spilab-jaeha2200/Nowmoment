// ════════════════════════════════════════════════════════════════════
// KgSettingsService.cs (v2.7.12 — Dynamic.cs 공존 버전)
//
// v2.7.11 → v2.7.12 변경 (★ 중요한 버그 수정):
//   * KeyForDomain(domain) 의 default case 가 KEY_BUILDER_SRC_CS 였는데,
//     이로 인해 사용자가 등록한 모든 도메인의 src 경로가 'cs' 도메인의
//     키와 같은 슬롯에 저장되어 서로 덮어쓰는 문제가 있었음.
//   * 이제 default case 는 도메인 코드 기반으로 동적 키 생성:
//     "builder_src_path_{code}"  (예: "builder_src_path_tact1_1")
//   * 빌트인 5종은 기존 상수 키 유지 — 마이그레이션 없이 호환.
//
// 주의 — Dynamic.cs 공존:
//   * 본 파일은 'partial' 키워드를 유지하여 KgSettingsService.Dynamic.cs 와 공존.
//   * KeyForDomainDynamic / KeyForLastImport 는 Dynamic.cs 에 있는 정의를 그대로 사용.
//     (중복 정의 방지)
// ════════════════════════════════════════════════════════════════════
using Microsoft.Data.Sqlite;

namespace SPILab.NowMoment.Services
{
    public partial class KgSettingsService
    {
        private readonly string _dbPath;
        private string ConnStr => $"Data Source={_dbPath}";

        public const string KEY_BUILDER_SRC    = "builder_src_path";        // (legacy, CS 호환)
        public const string KEY_BUILDER_SRC_CS       = "builder_src_path_cs";
        public const string KEY_BUILDER_SRC_PHOTO    = "builder_src_path_photo";
        public const string KEY_BUILDER_SRC_CMP      = "builder_src_path_cmp";
        public const string KEY_BUILDER_SRC_ETCH     = "builder_src_path_etch";
        public const string KEY_BUILDER_SRC_THINFILM = "builder_src_path_thinfilm";
        public const string KEY_PYTHON_EXE           = "python_exe_path";

        /// <summary>
        /// 도메인별 src 경로 키 반환.
        /// 빌트인 5종은 기존 상수 키, 사용자 등록 도메인은 'builder_src_path_{code}'.
        /// ★ v2.7.12: default 가 KEY_BUILDER_SRC_CS 였던 버그 수정.
        /// </summary>
        public static string KeyForDomain(string domain) => domain switch
        {
            "photo"    => KEY_BUILDER_SRC_PHOTO,
            "cmp"      => KEY_BUILDER_SRC_CMP,
            "etch"     => KEY_BUILDER_SRC_ETCH,
            "thinfilm" => KEY_BUILDER_SRC_THINFILM,
            "cs"       => KEY_BUILDER_SRC_CS,
            // ★ v2.7.12: 사용자 도메인은 코드 기반 동적 키 (이전엔 KEY_BUILDER_SRC_CS 와 충돌했음)
            _          => $"builder_src_path_{domain}",
        };

        // ──────────────────────────────────────────────────────────
        // 주의: KeyForDomainDynamic / KeyForLastImport 는
        //       KgSettingsService.Dynamic.cs 에서 정의됩니다 (중복 방지).
        // ──────────────────────────────────────────────────────────

        public KgSettingsService(string dbPath)
        {
            _dbPath = dbPath;
            EnsureTable();
        }

        private void EnsureTable()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            const string sql = @"
CREATE TABLE IF NOT EXISTS kg_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL DEFAULT '',
    updated_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public string? Get(string key)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT value FROM kg_settings WHERE key = $k", conn);
            cmd.Parameters.AddWithValue("$k", key);
            var v = cmd.ExecuteScalar();
            return v == null ? null : v.ToString();
        }

        public void Set(string key, string value)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
INSERT INTO kg_settings(key, value, updated_at)
VALUES ($k, $v, datetime('now','localtime'))
ON CONFLICT(key) DO UPDATE SET
  value = excluded.value,
  updated_at = excluded.updated_at;", conn);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        public void Delete(string key)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "DELETE FROM kg_settings WHERE key = $k", conn);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.ExecuteNonQuery();
        }
    }
}
