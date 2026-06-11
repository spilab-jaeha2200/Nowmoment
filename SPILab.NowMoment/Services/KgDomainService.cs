// ════════════════════════════════════════════════════════════════════
// KgDomainService.cs (v2.7.8)
//
// v2.7.7 → v2.7.8 변경:
//   * dump_script 컬럼 추가 (python_engine_folder 의 dump 단계 .py 절대경로)
//   * 기존 테이블에 컬럼이 없으면 ALTER TABLE 로 추가하는 마이그레이션
//   * 빌트인 photo 시드는 builder_kind 를 "python_engine_folder" 로 변경
//     (구버전 "python_folder" 도 정규화)
//   * Add/Update/Get 모두 dump_script 처리
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    public class KgDomainService
    {
        private readonly string _dbPath;
        private string ConnStr => $"Data Source={_dbPath}";

        public KgDomainService(string dbPath)
        {
            _dbPath = dbPath;
            EnsureTable();
            MigrateAddDumpScriptColumn();
            SeedBuiltins();
        }

        private void EnsureTable()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            const string sql = @"
CREATE TABLE IF NOT EXISTS kg_domain (
    code            TEXT PRIMARY KEY,
    label           TEXT NOT NULL,
    builder_kind    TEXT NOT NULL DEFAULT 'none',
    builder_script  TEXT NOT NULL DEFAULT '',
    dump_script     TEXT NOT NULL DEFAULT '',
    output_basename TEXT NOT NULL DEFAULT '',
    is_builtin      INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// v2.7.7 이전에 만든 DB 에 dump_script 컬럼이 없으면 추가.
        /// 이미 있으면 건너뛴다.
        /// </summary>
        private void MigrateAddDumpScriptColumn()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            // 컬럼 존재 확인
            using (var pragma = new SqliteCommand("PRAGMA table_info(kg_domain)", conn))
            using (var r = pragma.ExecuteReader())
            {
                while (r.Read())
                {
                    var col = r.GetString(1);
                    if (string.Equals(col, "dump_script", StringComparison.OrdinalIgnoreCase))
                        return; // 이미 존재
                }
            }
            // 컬럼 추가
            using var alter = new SqliteCommand(
                "ALTER TABLE kg_domain ADD COLUMN dump_script TEXT NOT NULL DEFAULT ''", conn);
            alter.ExecuteNonQuery();
        }

        // ── 시드: 빌트인 5종 ─────────────────────────────
        private void SeedBuiltins()
        {
            var seeds = new (string code, string label, string kind, string script, string dumpScript, string basename)[]
            {
                ("cs",       "SimCS — GaN/SiC",            "cs_file",              "build_kg_cs.py",       "",                          "kg_raypann_cs"),
                ("photo",    "SimPhoto — 포토리소그래피", "python_engine_folder", "build_kg_photo.py",    "dump_photo_to_csharp.py",   "kg_raypann_photo"),
                ("cmp",      "SimCMP — CMP 공정",          "cs_file",              "build_kg_cmp.py",      "",                          "kg_raypann_cmp"),
                ("etch",     "SimEtch — 식각 공정",        "cs_file",              "build_kg_etch.py",     "",                          "kg_raypann_etch"),
                ("thinfilm", "SimThinFilm — 박막증착",     "cs_file",              "build_kg_thinfilm.py", "",                          "kg_raypann_thinfilm"),
            };

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
INSERT OR IGNORE INTO kg_domain(code,label,builder_kind,builder_script,dump_script,output_basename,is_builtin)
VALUES(@c,@l,@k,@s,@d,@b,1)", conn);

            foreach (var s in seeds)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@c", s.code);
                cmd.Parameters.AddWithValue("@l", s.label);
                cmd.Parameters.AddWithValue("@k", s.kind);
                cmd.Parameters.AddWithValue("@s", s.script);
                cmd.Parameters.AddWithValue("@d", s.dumpScript);
                cmd.Parameters.AddWithValue("@b", s.basename);
                cmd.ExecuteNonQuery();
            }

            // 구버전 photo 시드의 builder_kind 정규화 ("python_folder" → "python_engine_folder")
            using var fix = new SqliteCommand(
                "UPDATE kg_domain SET builder_kind='python_engine_folder' " +
                "WHERE code='photo' AND builder_kind='python_folder'", conn);
            fix.ExecuteNonQuery();
        }

        // ── 조회 ─────────────────────────────────────────
        public List<KgDomain> GetAll()
        {
            var list = new List<KgDomain>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
SELECT code,label,builder_kind,builder_script,dump_script,output_basename,is_builtin,created_at
FROM kg_domain
ORDER BY is_builtin DESC, created_at ASC", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRow(r));
            return list;
        }

        public KgDomain? Get(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
SELECT code,label,builder_kind,builder_script,dump_script,output_basename,is_builtin,created_at
FROM kg_domain WHERE code=@c", conn);
            cmd.Parameters.AddWithValue("@c", code);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return ReadRow(r);
        }

        private static KgDomain ReadRow(IDataReader r)
        {
            // builder_kind 정규화: 구버전 "python_folder" → "python_engine_folder"
            var kind = r.GetString(2);
            if (kind == "python_folder") kind = "python_engine_folder";
            return new KgDomain
            {
                Code           = r.GetString(0),
                Label          = r.GetString(1),
                BuilderKind    = kind,
                BuilderScript  = r.GetString(3),
                DumpScript     = r.GetString(4),
                OutputBasename = r.GetString(5),
                IsBuiltIn      = r.GetInt32(6) != 0,
                CreatedAt      = DateTime.Parse(r.GetString(7)),
            };
        }

        // ── 등록 / 수정 / 삭제 ───────────────────────────
        public void Add(KgDomain d)
        {
            ValidateCode(d.Code);
            if (string.IsNullOrWhiteSpace(d.Label))
                throw new ArgumentException("라벨이 비어 있습니다.");
            if (d.BuilderKind != "none"
                && d.BuilderKind != "cs_file"
                && d.BuilderKind != "python_engine_folder")
                throw new ArgumentException($"지원하지 않는 builder_kind: {d.BuilderKind}");

            var basename = string.IsNullOrWhiteSpace(d.OutputBasename)
                ? $"kg_{d.Code}"
                : d.OutputBasename;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
INSERT INTO kg_domain(code,label,builder_kind,builder_script,dump_script,output_basename,is_builtin)
VALUES(@c,@l,@k,@s,@d,@b,0)", conn);
            cmd.Parameters.AddWithValue("@c", d.Code);
            cmd.Parameters.AddWithValue("@l", d.Label);
            cmd.Parameters.AddWithValue("@k", d.BuilderKind);
            cmd.Parameters.AddWithValue("@s", d.BuilderScript ?? "");
            cmd.Parameters.AddWithValue("@d", d.DumpScript ?? "");
            cmd.Parameters.AddWithValue("@b", basename);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException($"이미 존재하는 도메인 코드입니다: {d.Code}");
            }
        }

        public void Update(KgDomain d)
        {
            ValidateCode(d.Code);
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
UPDATE kg_domain
SET label=@l, builder_kind=@k, builder_script=@s, dump_script=@d, output_basename=@b
WHERE code=@c", conn);
            cmd.Parameters.AddWithValue("@c", d.Code);
            cmd.Parameters.AddWithValue("@l", d.Label);
            cmd.Parameters.AddWithValue("@k", d.BuilderKind);
            cmd.Parameters.AddWithValue("@s", d.BuilderScript ?? "");
            cmd.Parameters.AddWithValue("@d", d.DumpScript ?? "");
            cmd.Parameters.AddWithValue("@b", d.OutputBasename ?? $"kg_{d.Code}");
            cmd.ExecuteNonQuery();
        }

        public void Delete(string code)
        {
            var d = Get(code) ?? throw new InvalidOperationException($"도메인 없음: {code}");
            if (d.IsBuiltIn) throw new InvalidOperationException("빌트인 도메인은 삭제할 수 없습니다.");
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM kg_domain WHERE code=@c", conn);
            cmd.Parameters.AddWithValue("@c", code);
            cmd.ExecuteNonQuery();
        }

        public static void ValidateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("도메인 코드가 비어 있습니다.");
            if (!Regex.IsMatch(code, @"^[a-z][a-z0-9_]{1,31}$"))
                throw new ArgumentException(
                    "코드는 영문 소문자로 시작하는 2~32자의 [a-z0-9_] 만 허용됩니다.");
        }
    }
}
