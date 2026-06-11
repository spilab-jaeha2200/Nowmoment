// ════════════════════════════════════════════════════════════════════
// DatabaseService.V4.cs — v4 확장 (Phase 2)
//
// 기존 Insert*/Update*/DeleteAsset 는 호환을 위해 그대로 둔다.
// 본 파일은 Audit 통합에 필요한 보조 메서드만 추가:
//   - InsertCodeReturnId 등 신규 ID 반환 버전
//   - GetCodeById 등 단건 조회 (Audit diff 비교용 before-image)
//   - DeleteAssetsBatch (일괄 삭제 + 트랜잭션)
//   - GetLastInsertId (마지막 INSERT 의 rowid)
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    public partial class DatabaseService
    {
        // ── 마지막 INSERT 의 rowid ─────────────────────────
        public long GetLastInsertId(string table)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand($"SELECT MAX(id) FROM {table}", conn);
            var v = cmd.ExecuteScalar();
            return (v == null || v == DBNull.Value) ? 0L : Convert.ToInt64(v);
        }

        // ── 단건 조회 (Audit before-image용) ────────────────
        public AssetCode? GetCodeById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT c.id,c.name,c.repo_url,c.language,c.version,
                       c.project_id,COALESCE(p.name,''),c.tags,c.description,c.created_at
                FROM asset_code c
                LEFT JOIN project p ON p.id=c.project_id
                WHERE c.id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new AssetCode
            {
                Id = r.GetInt32(0), Name = r.GetString(1),
                RepoUrl = r.IsDBNull(2) ? "" : r.GetString(2),
                Language = r.IsDBNull(3) ? "" : r.GetString(3),
                Version = r.IsDBNull(4) ? "" : r.GetString(4),
                ProjectId = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                ProjectName = r.GetString(6),
                Tags = r.IsDBNull(7) ? "" : r.GetString(7),
                Description = r.IsDBNull(8) ? "" : r.GetString(8),
                CreatedAt = DateTime.Parse(r.GetString(9))
            };
        }

        public AssetModel? GetModelById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT m.id,m.name,m.framework,m.accuracy,m.file_path,
                       m.project_id,COALESCE(p.name,''),m.base_model,m.description,m.created_at
                FROM asset_model m
                LEFT JOIN project p ON p.id=m.project_id
                WHERE m.id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new AssetModel
            {
                Id = r.GetInt32(0), Name = r.GetString(1),
                Framework = r.IsDBNull(2) ? "" : r.GetString(2),
                Accuracy = r.IsDBNull(3) ? null : r.GetDouble(3),
                FilePath = r.IsDBNull(4) ? "" : r.GetString(4),
                ProjectId = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                ProjectName = r.GetString(6),
                BaseModel = r.IsDBNull(7) ? "" : r.GetString(7),
                Description = r.IsDBNull(8) ? "" : r.GetString(8),
                CreatedAt = DateTime.Parse(r.GetString(9))
            };
        }

        public AssetDocument? GetDocumentById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT d.id,d.title,d.doc_type,d.file_path,d.project_id,
                       COALESCE(p.name,''),d.version,d.summary,d.created_at
                FROM asset_document d
                LEFT JOIN project p ON p.id=d.project_id
                WHERE d.id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new AssetDocument
            {
                Id = r.GetInt32(0), Title = r.GetString(1),
                DocType = r.IsDBNull(2) ? "" : r.GetString(2),
                FilePath = r.IsDBNull(3) ? "" : r.GetString(3),
                ProjectId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                ProjectName = r.GetString(5),
                Version = r.IsDBNull(6) ? "" : r.GetString(6),
                Summary = r.IsDBNull(7) ? "" : r.GetString(7),
                CreatedAt = DateTime.Parse(r.GetString(8))
            };
        }

        public AssetPatent? GetPatentById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT id,title,application_no,status,filing_date,inventors,description,created_at
                FROM asset_patent WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new AssetPatent
            {
                Id = r.GetInt32(0), Title = r.GetString(1),
                ApplicationNo = r.IsDBNull(2) ? "" : r.GetString(2),
                Status = r.IsDBNull(3) ? "" : r.GetString(3),
                FilingDate = r.IsDBNull(4) ? null : DateTime.Parse(r.GetString(4)),
                Inventors = r.IsDBNull(5) ? "" : r.GetString(5),
                Description = r.IsDBNull(6) ? "" : r.GetString(6),
                CreatedAt = DateTime.Parse(r.GetString(7))
            };
        }

        public AssetExperiment? GetExperimentById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT id,name,asset_ref,params,metrics,result_path,status,created_at
                FROM asset_experiment WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new AssetExperiment
            {
                Id = r.GetInt32(0), Name = r.GetString(1),
                AssetRef = r.IsDBNull(2) ? "" : r.GetString(2),
                Params = r.IsDBNull(3) ? "{}" : r.GetString(3),
                Metrics = r.IsDBNull(4) ? "{}" : r.GetString(4),
                ResultPath = r.IsDBNull(5) ? "" : r.GetString(5),
                Status = r.IsDBNull(6) ? "" : r.GetString(6),
                CreatedAt = DateTime.Parse(r.GetString(7))
            };
        }

        // ── 일괄 삭제 (트랜잭션) ────────────────────────────
        /// <summary>같은 테이블에서 여러 ID를 한 트랜잭션으로 삭제.</summary>
        public int DeleteAssetsBatch(string table, IEnumerable<long> ids)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                int deleted = 0;
                using var cmd = new SqliteCommand($"DELETE FROM {table} WHERE id=@id", conn, tx);
                var p = cmd.Parameters.Add("@id", SqliteType.Integer);
                foreach (var id in ids)
                {
                    p.Value = id;
                    deleted += cmd.ExecuteNonQuery();
                }
                tx.Commit();
                return deleted;
            }
            catch { tx.Rollback(); throw; }
        }

        // ── updated_at 자동 갱신 ────────────────────────────
        /// <summary>UPDATE 직후 호출하여 updated_at 컬럼을 현재시각으로.</summary>
        public void TouchUpdatedAt(string table, long id)
        {
            try
            {
                using var conn = new SqliteConnection(ConnStr);
                conn.Open();
                using var cmd = new SqliteCommand(
                    $"UPDATE {table} SET updated_at=datetime('now','localtime') WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch { /* updated_at 컬럼이 없는 환경에서도 실패해서는 안 됨 */ }
        }

        // ── 프로젝트 (CRUD 보강) ────────────────────────────
        public Project? GetProjectById(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT id,name,client,type,status,start_date,end_date,created_at FROM project WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new Project
            {
                Id = r.GetInt32(0), Name = r.GetString(1),
                Client = r.IsDBNull(2) ? "" : r.GetString(2),
                Type = r.IsDBNull(3) ? "" : r.GetString(3),
                Status = r.IsDBNull(4) ? "" : r.GetString(4),
                StartDate = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
                EndDate = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
                CreatedAt = DateTime.Parse(r.GetString(7))
            };
        }

        public void UpdateProject(Project p)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE project
                SET name=@n, client=@c, type=@t, status=@s,
                    start_date=@sd, end_date=@ed
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@n",  p.Name);
            cmd.Parameters.AddWithValue("@c",  p.Client);
            cmd.Parameters.AddWithValue("@t",  p.Type);
            cmd.Parameters.AddWithValue("@s",  p.Status);
            cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", p.Id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>프로젝트 삭제. 소속 자산의 project_id 는 NULL 로 설정 (자산은 보존).</summary>
        public void DeleteProject(long id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var t in new[] { "asset_code", "asset_model", "asset_document" })
                {
                    using var unlink = new SqliteCommand(
                        $"UPDATE {t} SET project_id=NULL WHERE project_id=@id", conn, tx);
                    unlink.Parameters.AddWithValue("@id", id);
                    unlink.ExecuteNonQuery();
                }
                using var del = new SqliteCommand("DELETE FROM project WHERE id=@id", conn, tx);
                del.Parameters.AddWithValue("@id", id);
                del.ExecuteNonQuery();
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        /// <summary>프로젝트별 자산 카운트 (5종 합산).</summary>
        public Dictionary<string,int> GetAssetCountsByProject(long projectId)
        {
            var d = new Dictionary<string,int>
            {
                ["code"]=0, ["model"]=0, ["document"]=0, ["patent"]=0, ["experiment"]=0
            };
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            foreach (var (key, table) in new[]
            {
                ("code","asset_code"), ("model","asset_model"),
                ("document","asset_document"),
                // patent / experiment 는 project_id 컬럼이 없음 → 0 유지
            })
            {
                using var cmd = new SqliteCommand(
                    $"SELECT COUNT(*) FROM {table} WHERE project_id=@id", conn);
                cmd.Parameters.AddWithValue("@id", projectId);
                d[key] = Convert.ToInt32(cmd.ExecuteScalar() ?? 0L);
            }
            return d;
        }

        /// <summary>프로젝트별 태그 빈도 (상위 N).</summary>
        public List<(string name, int count)> GetTopTagsForProject(long projectId, int limit = 10)
        {
            var list = new List<(string, int)>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT t.name, COUNT(*) AS cnt
                FROM asset_tag at
                JOIN tag t ON t.id = at.tag_id
                WHERE at.asset_type='asset_code'
                  AND at.asset_id IN (SELECT id FROM asset_code WHERE project_id=@id)
                GROUP BY t.id
                ORDER BY cnt DESC
                LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@id",  projectId);
            cmd.Parameters.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1)));
            return list;
        }
    }
}
