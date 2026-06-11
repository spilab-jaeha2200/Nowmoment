using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    /// <summary>
    /// SQLite 기반 로컬 데이터베이스 서비스
    /// DB 파일: %APPDATA%\SPILab\NowMoment\nowmoment.db
    /// </summary>
    public partial class DatabaseService
    {
        private readonly string _dbPath;
        private string ConnStr => $"Data Source={_dbPath}";

        /// <summary>v3.0 F-003: 백업 서비스가 SQLite BACKUP API 호출 시 사용.</summary>
        public string ConnectionString => ConnStr;

        public DatabaseService()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SPILab", "NowMoment");
            Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, "nowmoment.db");
            InitializeDatabase();
        }

        // ── DB 초기화 (테이블 생성) ──────────────────────
        private void InitializeDatabase()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS project (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    client      TEXT DEFAULT '',
    type        TEXT DEFAULT 'internal',
    status      TEXT DEFAULT 'active',
    start_date  TEXT,
    end_date    TEXT,
    created_at  TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS asset_code (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    repo_url    TEXT DEFAULT '',
    language    TEXT DEFAULT 'Python',
    version     TEXT DEFAULT '1.0.0',
    project_id  INTEGER REFERENCES project(id),
    tags        TEXT DEFAULT '',
    description TEXT DEFAULT '',
    created_at  TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS asset_model (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    framework   TEXT DEFAULT 'PyTorch',
    accuracy    REAL,
    file_path   TEXT DEFAULT '',
    project_id  INTEGER REFERENCES project(id),
    base_model  TEXT DEFAULT '',
    description TEXT DEFAULT '',
    created_at  TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS asset_document (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT NOT NULL,
    doc_type    TEXT DEFAULT 'document',
    file_path   TEXT DEFAULT '',
    project_id  INTEGER REFERENCES project(id),
    version     TEXT DEFAULT '1.0',
    summary     TEXT DEFAULT '',
    created_at  TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS asset_patent (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    title           TEXT NOT NULL,
    application_no  TEXT DEFAULT '',
    status          TEXT DEFAULT 'applied',
    filing_date     TEXT,
    inventors       TEXT DEFAULT '',
    description     TEXT DEFAULT '',
    created_at      TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS asset_experiment (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    asset_ref   TEXT DEFAULT '',
    params      TEXT DEFAULT '{}',
    metrics     TEXT DEFAULT '{}',
    result_path TEXT DEFAULT '',
    status      TEXT DEFAULT 'completed',
    created_at  TEXT DEFAULT (datetime('now','localtime'))
);";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();

            SeedDefaultProjects(conn);

            // v4 마이그레이션 (멱등 — 기존 DB 안전하게 업그레이드)
            MigrateToV4(conn);
        }

        // ── 기본 프로젝트 시드 데이터 ───────────────────
        private void SeedDefaultProjects(SqliteConnection conn)
        {
            using var check = new SqliteCommand("SELECT COUNT(*) FROM project", conn);
            var cnt = (long)(check.ExecuteScalar() ?? 0L);
            if (cnt > 0) return;

            var projects = new[]
            {
                ("Q-AI 코팅품질관리 (WCP CPA3)",      "WCP",    "commercial"),
                ("레이판Sim 광식각 시뮬레이터",         "KANC",   "commercial"),
                ("레이판Sim ALD 시뮬레이터",            "KANC",   "commercial"),
                ("레이판 ESS (배터리 SoH/RUL)",         "내부",   "internal"),
                ("레이판 Drone (화재감지)",              "내부",   "internal"),
                ("PSA 산소발생기 예지보전 (NEO2)",       "NEO2",   "commercial"),
                ("HIPIF 논문 (CAV 2026)",               "내부",   "internal"),
                ("PLHA 논문 (ECCV 2026)",               "내부",   "internal"),
                ("AI바우처 금강방재",                    "금강방재","govt"),
                ("레이판Sim CS Edition (GaN/SiC)",      "KANC",   "commercial"),
            };
            foreach (var (name, client, type) in projects)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO project (name,client,type) VALUES (@n,@c,@t)", conn);
                ins.Parameters.AddWithValue("@n", name);
                ins.Parameters.AddWithValue("@c", client);
                ins.Parameters.AddWithValue("@t", type);
                ins.ExecuteNonQuery();
            }
        }

        // ── Project ─────────────────────────────────────
        public List<Project> GetProjects()
        {
            var list = new List<Project>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT id,name,client,type,status,start_date,end_date,created_at FROM project ORDER BY id", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new Project
                {
                    Id = r.GetInt32(0), Name = r.GetString(1),
                    Client = r.IsDBNull(2) ? "" : r.GetString(2),
                    Type = r.IsDBNull(3) ? "" : r.GetString(3),
                    Status = r.IsDBNull(4) ? "" : r.GetString(4),
                    StartDate = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
                    EndDate = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            return list;
        }

        public void InsertProject(Project p)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO project (name,client,type,status,start_date,end_date)
                VALUES (@n,@c,@t,@s,@sd,@ed)", conn);
            cmd.Parameters.AddWithValue("@n", p.Name);
            cmd.Parameters.AddWithValue("@c", p.Client);
            cmd.Parameters.AddWithValue("@t", p.Type);
            cmd.Parameters.AddWithValue("@s", p.Status);
            cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // ── AssetCode ────────────────────────────────────
        public List<AssetCode> GetCodes(string keyword = "")
        {
            var list = new List<AssetCode>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT c.id,c.name,c.repo_url,c.language,c.version,
                       c.project_id,COALESCE(p.name,''),c.tags,c.description,c.created_at
                FROM asset_code c
                LEFT JOIN project p ON p.id=c.project_id
                WHERE (@kw='' OR c.name LIKE @kw OR c.tags LIKE @kw OR c.description LIKE @kw OR c.language LIKE @kw OR c.repo_url LIKE @kw OR c.version LIKE @kw)
                ORDER BY c.created_at DESC";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetCode
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
                });
            return list;
        }

        public void InsertCode(AssetCode a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO asset_code (name,repo_url,language,version,project_id,tags,description)
                VALUES (@n,@ru,@la,@v,@pi,@tg,@de)", conn);
            cmd.Parameters.AddWithValue("@n", a.Name);
            cmd.Parameters.AddWithValue("@ru", a.RepoUrl);
            cmd.Parameters.AddWithValue("@la", a.Language);
            cmd.Parameters.AddWithValue("@v",  a.Version);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@tg", a.Tags);
            cmd.Parameters.AddWithValue("@de", a.Description);
            cmd.ExecuteNonQuery();
        }

        public void UpdateCode(AssetCode a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE asset_code
                SET name=@n,repo_url=@ru,language=@la,version=@v,
                    project_id=@pi,tags=@tg,description=@de
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@n", a.Name);
            cmd.Parameters.AddWithValue("@ru", a.RepoUrl);
            cmd.Parameters.AddWithValue("@la", a.Language);
            cmd.Parameters.AddWithValue("@v",  a.Version);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@tg", a.Tags);
            cmd.Parameters.AddWithValue("@de", a.Description);
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAsset(string table, int id)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand($"DELETE FROM {table} WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ── AssetModel ───────────────────────────────────
        public List<AssetModel> GetModels(string keyword = "")
        {
            var list = new List<AssetModel>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT m.id,m.name,m.framework,m.accuracy,m.file_path,
                       m.project_id,COALESCE(p.name,''),m.base_model,m.description,m.created_at
                FROM asset_model m
                LEFT JOIN project p ON p.id=m.project_id
                WHERE (@kw='' OR m.name LIKE @kw OR m.description LIKE @kw OR m.framework LIKE @kw OR m.base_model LIKE @kw)
                ORDER BY m.created_at DESC";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetModel
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
                });
            return list;
        }

        public void InsertModel(AssetModel a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO asset_model (name,framework,accuracy,file_path,project_id,base_model,description)
                VALUES (@n,@fw,@ac,@fp,@pi,@bm,@de)", conn);
            cmd.Parameters.AddWithValue("@n",  a.Name);
            cmd.Parameters.AddWithValue("@fw", a.Framework);
            cmd.Parameters.AddWithValue("@ac", (object?)a.Accuracy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fp", a.FilePath);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@bm", a.BaseModel);
            cmd.Parameters.AddWithValue("@de", a.Description);
            cmd.ExecuteNonQuery();
        }

        public void UpdateModel(AssetModel a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE asset_model
                SET name=@n,framework=@fw,accuracy=@ac,file_path=@fp,
                    project_id=@pi,base_model=@bm,description=@de
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@n",  a.Name);
            cmd.Parameters.AddWithValue("@fw", a.Framework);
            cmd.Parameters.AddWithValue("@ac", (object?)a.Accuracy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fp", a.FilePath);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@bm", a.BaseModel);
            cmd.Parameters.AddWithValue("@de", a.Description);
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.ExecuteNonQuery();
        }

        // ── AssetDocument ────────────────────────────────
        public List<AssetDocument> GetDocuments(string keyword = "")
        {
            var list = new List<AssetDocument>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT d.id,d.title,d.doc_type,d.file_path,
                       d.project_id,COALESCE(p.name,''),d.version,d.summary,d.created_at
                FROM asset_document d
                LEFT JOIN project p ON p.id=d.project_id
                WHERE (@kw='' OR d.title LIKE @kw OR d.summary LIKE @kw OR d.doc_type LIKE @kw OR d.version LIKE @kw)
                ORDER BY d.created_at DESC";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetDocument
                {
                    Id = r.GetInt32(0), Title = r.GetString(1),
                    DocType = r.IsDBNull(2) ? "" : r.GetString(2),
                    FilePath = r.IsDBNull(3) ? "" : r.GetString(3),
                    ProjectId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    ProjectName = r.GetString(5),
                    Version = r.IsDBNull(6) ? "" : r.GetString(6),
                    Summary = r.IsDBNull(7) ? "" : r.GetString(7),
                    CreatedAt = DateTime.Parse(r.GetString(8))
                });
            return list;
        }

        public void InsertDocument(AssetDocument a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO asset_document (title,doc_type,file_path,project_id,version,summary)
                VALUES (@ti,@dt,@fp,@pi,@v,@su)", conn);
            cmd.Parameters.AddWithValue("@ti", a.Title);
            cmd.Parameters.AddWithValue("@dt", a.DocType);
            cmd.Parameters.AddWithValue("@fp", a.FilePath);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@v",  a.Version);
            cmd.Parameters.AddWithValue("@su", a.Summary);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDocument(AssetDocument a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE asset_document
                SET title=@ti,doc_type=@dt,file_path=@fp,
                    project_id=@pi,version=@v,summary=@su
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@ti", a.Title);
            cmd.Parameters.AddWithValue("@dt", a.DocType);
            cmd.Parameters.AddWithValue("@fp", a.FilePath);
            cmd.Parameters.AddWithValue("@pi", a.ProjectId == 0 ? DBNull.Value : a.ProjectId);
            cmd.Parameters.AddWithValue("@v",  a.Version);
            cmd.Parameters.AddWithValue("@su", a.Summary);
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.ExecuteNonQuery();
        }

        // ── AssetPatent ──────────────────────────────────
        public List<AssetPatent> GetPatents(string keyword = "")
        {
            var list = new List<AssetPatent>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT id,title,application_no,status,filing_date,inventors,description,created_at
                FROM asset_patent
                WHERE (@kw='' OR title LIKE @kw OR application_no LIKE @kw OR inventors LIKE @kw OR status LIKE @kw OR description LIKE @kw)
                ORDER BY created_at DESC";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetPatent
                {
                    Id = r.GetInt32(0), Title = r.GetString(1),
                    ApplicationNo = r.IsDBNull(2) ? "" : r.GetString(2),
                    Status = r.IsDBNull(3) ? "" : r.GetString(3),
                    FilingDate = r.IsDBNull(4) ? null : DateTime.Parse(r.GetString(4)),
                    Inventors = r.IsDBNull(5) ? "" : r.GetString(5),
                    Description = r.IsDBNull(6) ? "" : r.GetString(6),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            return list;
        }

        public void InsertPatent(AssetPatent a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO asset_patent (title,application_no,status,filing_date,inventors,description)
                VALUES (@ti,@an,@st,@fd,@inv,@de)", conn);
            cmd.Parameters.AddWithValue("@ti",  a.Title);
            cmd.Parameters.AddWithValue("@an",  a.ApplicationNo);
            cmd.Parameters.AddWithValue("@st",  a.Status);
            cmd.Parameters.AddWithValue("@fd",  (object?)a.FilingDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@inv", a.Inventors);
            cmd.Parameters.AddWithValue("@de",  a.Description);
            cmd.ExecuteNonQuery();
        }

        public void UpdatePatent(AssetPatent a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE asset_patent
                SET title=@ti,application_no=@an,status=@st,
                    filing_date=@fd,inventors=@inv,description=@de
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@ti",  a.Title);
            cmd.Parameters.AddWithValue("@an",  a.ApplicationNo);
            cmd.Parameters.AddWithValue("@st",  a.Status);
            cmd.Parameters.AddWithValue("@fd",  (object?)a.FilingDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@inv", a.Inventors);
            cmd.Parameters.AddWithValue("@de",  a.Description);
            cmd.Parameters.AddWithValue("@id",  a.Id);
            cmd.ExecuteNonQuery();
        }

        // ── AssetExperiment ──────────────────────────────
        public List<AssetExperiment> GetExperiments(string keyword = "")
        {
            var list = new List<AssetExperiment>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT id,name,asset_ref,params,metrics,result_path,status,created_at
                FROM asset_experiment
                WHERE (@kw='' OR name LIKE @kw OR asset_ref LIKE @kw OR metrics LIKE @kw OR status LIKE @kw OR params LIKE @kw)
                ORDER BY created_at DESC";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", string.IsNullOrWhiteSpace(keyword) ? "" : $"%{keyword}%");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssetExperiment
                {
                    Id = r.GetInt32(0), Name = r.GetString(1),
                    AssetRef = r.IsDBNull(2) ? "" : r.GetString(2),
                    Params = r.IsDBNull(3) ? "{}" : r.GetString(3),
                    Metrics = r.IsDBNull(4) ? "{}" : r.GetString(4),
                    ResultPath = r.IsDBNull(5) ? "" : r.GetString(5),
                    Status = r.IsDBNull(6) ? "" : r.GetString(6),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            return list;
        }

        public void InsertExperiment(AssetExperiment a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO asset_experiment (name,asset_ref,params,metrics,result_path,status)
                VALUES (@n,@ar,@pa,@me,@rp,@st)", conn);
            cmd.Parameters.AddWithValue("@n",  a.Name);
            cmd.Parameters.AddWithValue("@ar", a.AssetRef);
            cmd.Parameters.AddWithValue("@pa", a.Params);
            cmd.Parameters.AddWithValue("@me", a.Metrics);
            cmd.Parameters.AddWithValue("@rp", a.ResultPath);
            cmd.Parameters.AddWithValue("@st", a.Status);
            cmd.ExecuteNonQuery();
        }

        public void UpdateExperiment(AssetExperiment a)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                UPDATE asset_experiment
                SET name=@n,asset_ref=@ar,params=@pa,
                    metrics=@me,result_path=@rp,status=@st
                WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@n",  a.Name);
            cmd.Parameters.AddWithValue("@ar", a.AssetRef);
            cmd.Parameters.AddWithValue("@pa", a.Params);
            cmd.Parameters.AddWithValue("@me", a.Metrics);
            cmd.Parameters.AddWithValue("@rp", a.ResultPath);
            cmd.Parameters.AddWithValue("@st", a.Status);
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.ExecuteNonQuery();
        }

        // ── 통합 검색 ────────────────────────────────────
        public List<SearchResult> GlobalSearch(string keyword)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(keyword)) return results;
            var kw = $"%{keyword}%";
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"
                SELECT c.id,'코드' AS type, c.name, COALESCE(p.name,''), c.tags, c.description, c.created_at
                FROM asset_code c LEFT JOIN project p ON p.id=c.project_id
                WHERE c.name LIKE @kw OR c.tags LIKE @kw OR c.description LIKE @kw OR c.language LIKE @kw OR c.repo_url LIKE @kw OR c.version LIKE @kw
                UNION ALL
                SELECT m.id,'모델', m.name, COALESCE(p.name,''), '', m.description, m.created_at
                FROM asset_model m LEFT JOIN project p ON p.id=m.project_id
                WHERE m.name LIKE @kw OR m.description LIKE @kw OR m.framework LIKE @kw OR m.base_model LIKE @kw
                UNION ALL
                SELECT d.id,'문서', d.title, COALESCE(p.name,''), '', d.summary, d.created_at
                FROM asset_document d LEFT JOIN project p ON p.id=d.project_id
                WHERE d.title LIKE @kw OR d.summary LIKE @kw OR d.doc_type LIKE @kw OR d.version LIKE @kw
                UNION ALL
                SELECT id,'특허', title, '', '', description, created_at
                FROM asset_patent WHERE title LIKE @kw OR description LIKE @kw OR application_no LIKE @kw OR inventors LIKE @kw OR status LIKE @kw
                UNION ALL
                SELECT id,'실험', name, '', '', metrics, created_at
                FROM asset_experiment WHERE name LIKE @kw OR metrics LIKE @kw OR asset_ref LIKE @kw OR status LIKE @kw OR params LIKE @kw
                ORDER BY created_at DESC LIMIT 100";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", kw);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                results.Add(new SearchResult
                {
                    Id = r.GetInt32(0), AssetType = r.GetString(1),
                    Name = r.GetString(2), ProjectName = r.GetString(3),
                    Tags = r.GetString(4), Description = r.GetString(5),
                    CreatedAt = DateTime.Parse(r.GetString(6))
                });
            return results;
        }

        // ── 대시보드 통계 ────────────────────────────────
        public Dictionary<string, int> GetStats()
        {
            var d = new Dictionary<string, int>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            foreach (var (key, table) in new[]
            {
                ("소스코드","asset_code"), ("모델","asset_model"),
                ("문서","asset_document"), ("특허","asset_patent"), ("실험","asset_experiment")
            })
            {
                using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {table}", conn);
                d[key] = (int)(long)(cmd.ExecuteScalar() ?? 0L);
            }
            return d;
        }

        // ── v3.0 F-002 Step 2.4: 폴더 임포트 중복 검사 ─────────
        // 임포트 시 같은 자산이 이미 등록되어 있는지 사전에 확인.
        // (repo_url / file_path / name / title 기준으로 빠른 EXISTS 검사)

        /// <summary>asset_code 에 같은 repo_url 또는 같은 name 이 있으면 true.</summary>
        public bool IsDuplicateCode(string name, string repoUrl)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT COUNT(*) FROM asset_code
                WHERE name=@n
                   OR (repo_url IS NOT NULL AND repo_url<>'' AND repo_url=@r)", conn);
            cmd.Parameters.AddWithValue("@n", name ?? "");
            cmd.Parameters.AddWithValue("@r", repoUrl ?? "");
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        /// <summary>asset_model 에 같은 file_path 또는 같은 name 이 있으면 true.</summary>
        public bool IsDuplicateModel(string name, string filePath)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT COUNT(*) FROM asset_model
                WHERE name=@n
                   OR (file_path IS NOT NULL AND file_path<>'' AND file_path=@p)", conn);
            cmd.Parameters.AddWithValue("@n", name ?? "");
            cmd.Parameters.AddWithValue("@p", filePath ?? "");
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        /// <summary>asset_document 에 같은 file_path 또는 같은 title 이 있으면 true.</summary>
        public bool IsDuplicateDocument(string title, string filePath)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT COUNT(*) FROM asset_document
                WHERE title=@t
                   OR (file_path IS NOT NULL AND file_path<>'' AND file_path=@p)", conn);
            cmd.Parameters.AddWithValue("@t", title ?? "");
            cmd.Parameters.AddWithValue("@p", filePath ?? "");
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        /// <summary>asset_experiment 에 같은 name 이 있으면 true.</summary>
        public bool IsDuplicateExperiment(string name)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM asset_experiment WHERE name=@n", conn);
            cmd.Parameters.AddWithValue("@n", name ?? "");
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        public string DbPath => _dbPath;

        // ════════════════════════════════════════════════
        // v4 마이그레이션 + 신규 테이블 (audit_log, tag, asset_tag, ttl_ontology, user_setting)
        // 모두 멱등 — IF NOT EXISTS / ALTER 가드 적용
        // ════════════════════════════════════════════════
        private void MigrateToV4(SqliteConnection conn)
        {
            // 1) 신규 테이블 생성
            const string ddl = @"
CREATE TABLE IF NOT EXISTS audit_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ts          TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    actor       TEXT NOT NULL DEFAULT 'local',
    action      TEXT NOT NULL,
    asset_type  TEXT NOT NULL,
    asset_id    INTEGER,
    diff_json   TEXT NOT NULL DEFAULT '{}'
);
CREATE INDEX IF NOT EXISTS idx_audit_asset ON audit_log(asset_type, asset_id);
CREATE INDEX IF NOT EXISTS idx_audit_ts    ON audit_log(ts);

CREATE TABLE IF NOT EXISTS tag (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT NOT NULL UNIQUE,
    color TEXT DEFAULT ''
);

CREATE TABLE IF NOT EXISTS asset_tag (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_type TEXT NOT NULL,
    asset_id   INTEGER NOT NULL,
    tag_id     INTEGER NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
    UNIQUE(asset_type, asset_id, tag_id)
);
CREATE INDEX IF NOT EXISTS idx_asset_tag_asset ON asset_tag(asset_type, asset_id);
CREATE INDEX IF NOT EXISTS idx_asset_tag_tag   ON asset_tag(tag_id);

CREATE TABLE IF NOT EXISTS ttl_ontology (
    id           INTEGER PRIMARY KEY CHECK (id = 1),
    base_uri     TEXT NOT NULL,
    base_prefix  TEXT NOT NULL,
    json_payload TEXT NOT NULL,
    updated_at   TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS user_setting (
    grp        TEXT NOT NULL,
    key        TEXT NOT NULL,
    value      TEXT NOT NULL DEFAULT '',
    updated_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    PRIMARY KEY (grp, key)
);";
            using (var cmd = new SqliteCommand(ddl, conn)) cmd.ExecuteNonQuery();

            // 2) 기존 asset_* 테이블에 updated_at 컬럼 ALTER (멱등)
            //    SQLite 제약: ALTER ADD COLUMN 의 DEFAULT 는 상수만 허용 (datetime('now') 같은 함수 불가)
            //    → 컬럼만 추가하고, 직후 UPDATE 로 기존 행에 created_at 값을 복사
            foreach (var t in new[] { "asset_code","asset_model","asset_document","asset_patent","asset_experiment","project" })
            {
                if (!ColumnExists(conn, t, "updated_at"))
                {
                    using (var alter = new SqliteCommand(
                        $"ALTER TABLE {t} ADD COLUMN updated_at TEXT", conn))
                    {
                        alter.ExecuteNonQuery();
                    }
                    // 기존 행의 updated_at 을 created_at 으로 초기화 (없으면 현재시각)
                    using (var fill = new SqliteCommand(
                        $@"UPDATE {t} SET updated_at = COALESCE(created_at, datetime('now','localtime'))
                           WHERE updated_at IS NULL", conn))
                    {
                        fill.ExecuteNonQuery();
                    }
                }
            }

            // 3) v4.1 — audit_log 에 Core 접근 이벤트용 컬럼 추가 (계획서 5.3)
            //    'core.verify' / 'core.unlock' / 'core.build' / 'core.denied' 등
            //    Core 이벤트는 actor_role(권한등급) 과 core_session(세션ID) 을
            //    전용 컬럼으로 기록한다. 자산 CRUD 행에서는 두 컬럼이 빈 문자열.
            //    ALTER ADD COLUMN DEFAULT '' — 기존 nowmoment.db 그대로 사용 가능.
            foreach (var col in new[] { "actor_role", "core_session" })
            {
                if (!ColumnExists(conn, "audit_log", col))
                {
                    using var alter = new SqliteCommand(
                        $"ALTER TABLE audit_log ADD COLUMN {col} TEXT NOT NULL DEFAULT ''",
                        conn);
                    alter.ExecuteNonQuery();
                }
            }
        }

        private static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = new SqliteCommand($"PRAGMA table_info({table})", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── v4: audit_log ────────────────────────────────
        /// <summary>
        /// 이력 적재. 모든 자산 CRUD 경로에서 호출.
        /// v4.1: Core 접근 이벤트는 actorRole / coreSession 을 함께 전달한다
        /// (계획서 5.3). 자산 CRUD 경로는 두 인자를 생략하면 된다(빈 문자열).
        /// </summary>
        public void WriteAudit(string action, string assetType, long? assetId, string diffJson,
            string actor = "local", string actorRole = "", string coreSession = "")
        {
            try
            {
                using var conn = new SqliteConnection(ConnStr);
                conn.Open();
                using var cmd = new SqliteCommand(@"
                    INSERT INTO audit_log
                        (actor, action, asset_type, asset_id, diff_json, actor_role, core_session)
                    VALUES (@a, @ac, @at, @ai, @dj, @ar, @cs)", conn);
                cmd.Parameters.AddWithValue("@a",  actor ?? "local");
                cmd.Parameters.AddWithValue("@ac", action ?? "");
                cmd.Parameters.AddWithValue("@at", assetType ?? "");
                cmd.Parameters.AddWithValue("@ai", (object?)assetId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dj", string.IsNullOrEmpty(diffJson) ? "{}" : diffJson);
                cmd.Parameters.AddWithValue("@ar", actorRole ?? "");
                cmd.Parameters.AddWithValue("@cs", coreSession ?? "");
                cmd.ExecuteNonQuery();
            }
            catch { /* audit 실패가 본 작업을 중단시키면 안 됨 */ }
        }

        public List<AuditLog> GetAuditLogs(string? assetType = null, string? action = null,
            DateTime? from = null, DateTime? to = null, int limit = 500)
        {
            var list = new List<AuditLog>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = @"SELECT id, ts, actor, action, asset_type, asset_id, diff_json,
                               actor_role, core_session
                        FROM audit_log
                        WHERE (@at IS NULL OR asset_type = @at)
                          AND (@ac IS NULL OR action     = @ac)
                          AND (@fr IS NULL OR ts >= @fr)
                          AND (@to IS NULL OR ts <  @to)
                        ORDER BY id DESC
                        LIMIT @lim";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@at",  (object?)assetType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ac",  (object?)action    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fr",  (object?)from?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@to",  (object?)to?.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AuditLog
                {
                    Id          = r.GetInt64(0),
                    Ts          = r.GetString(1),
                    Actor       = r.IsDBNull(2) ? "local" : r.GetString(2),
                    Action      = r.IsDBNull(3) ? "" : r.GetString(3),
                    AssetType   = r.IsDBNull(4) ? "" : r.GetString(4),
                    AssetId     = r.IsDBNull(5) ? null : r.GetInt64(5),
                    DiffJson    = r.IsDBNull(6) ? "{}" : r.GetString(6),
                    ActorRole   = r.IsDBNull(7) ? "" : r.GetString(7),
                    CoreSession = r.IsDBNull(8) ? "" : r.GetString(8),
                });
            return list;
        }

        public Dictionary<string,int> GetAuditCounts(DateTime? from = null, DateTime? to = null)
        {
            var d = new Dictionary<string,int>
            {
                ["total"]=0, ["create"]=0, ["update"]=0, ["delete"]=0, ["backup"]=0
            };
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                SELECT action, COUNT(*) FROM audit_log
                WHERE (@fr IS NULL OR ts >= @fr)
                  AND (@to IS NULL OR ts <  @to)
                GROUP BY action", conn);
            cmd.Parameters.AddWithValue("@fr",  (object?)from?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@to",  (object?)to?.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var act = r.IsDBNull(0) ? "" : r.GetString(0);
                var cnt = r.GetInt32(1);
                d["total"] += cnt;
                if (d.ContainsKey(act)) d[act] = cnt;
            }
            return d;
        }

        // ── v4: tag ──────────────────────────────────────
        public List<Tag> GetTags(bool withCount = false)
        {
            var list = new List<Tag>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            var sql = withCount
                ? @"SELECT t.id, t.name, t.color, COALESCE(c.cnt,0)
                    FROM tag t
                    LEFT JOIN (SELECT tag_id, COUNT(*) cnt FROM asset_tag GROUP BY tag_id) c
                      ON c.tag_id = t.id
                    ORDER BY t.name"
                : "SELECT id, name, color, 0 FROM tag ORDER BY name";
            using var cmd = new SqliteCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new Tag {
                    Id = r.GetInt32(0), Name = r.GetString(1),
                    Color = r.IsDBNull(2) ? "" : r.GetString(2),
                    UseCount = r.GetInt32(3)
                });
            return list;
        }

        /// <summary>태그 이름으로 조회/생성하여 id 반환.</summary>
        public int GetOrCreateTag(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return 0;
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using (var sel = new SqliteCommand("SELECT id FROM tag WHERE name=@n", conn))
            {
                sel.Parameters.AddWithValue("@n", name);
                var v = sel.ExecuteScalar();
                if (v != null && v != DBNull.Value) return Convert.ToInt32(v);
            }
            using var ins = new SqliteCommand(
                "INSERT INTO tag (name) VALUES (@n); SELECT last_insert_rowid();", conn);
            ins.Parameters.AddWithValue("@n", name);
            return Convert.ToInt32(ins.ExecuteScalar());
        }

        /// <summary>자산에 태그 목록 적용 (콤마 분리 문자열 → asset_tag 동기화).</summary>
        public void SyncAssetTags(string assetType, long assetId, string commaSeparated)
        {
            var tags = (commaSeparated ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().TrimStart('#'))
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var del = new SqliteCommand(
                    "DELETE FROM asset_tag WHERE asset_type=@t AND asset_id=@i", conn, tx))
                {
                    del.Parameters.AddWithValue("@t", assetType);
                    del.Parameters.AddWithValue("@i", assetId);
                    del.ExecuteNonQuery();
                }
                foreach (var t in tags)
                {
                    int tagId;
                    using (var sel = new SqliteCommand("SELECT id FROM tag WHERE name=@n", conn, tx))
                    {
                        sel.Parameters.AddWithValue("@n", t);
                        var v = sel.ExecuteScalar();
                        if (v != null && v != DBNull.Value) tagId = Convert.ToInt32(v);
                        else
                        {
                            using var ins = new SqliteCommand(
                                "INSERT INTO tag (name) VALUES (@n); SELECT last_insert_rowid();", conn, tx);
                            ins.Parameters.AddWithValue("@n", t);
                            tagId = Convert.ToInt32(ins.ExecuteScalar());
                        }
                    }
                    using var link = new SqliteCommand(@"
                        INSERT OR IGNORE INTO asset_tag (asset_type, asset_id, tag_id)
                        VALUES (@t, @i, @g)", conn, tx);
                    link.Parameters.AddWithValue("@t", assetType);
                    link.Parameters.AddWithValue("@i", assetId);
                    link.Parameters.AddWithValue("@g", tagId);
                    link.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        // ── v4: ttl_ontology ─────────────────────────────
        public TtlOntologyRecord? LoadTtlOntology()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT base_uri, base_prefix, json_payload, updated_at FROM ttl_ontology WHERE id=1", conn);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new TtlOntologyRecord
            {
                BaseUri     = r.GetString(0),
                BasePrefix  = r.GetString(1),
                JsonPayload = r.GetString(2),
                UpdatedAt   = DateTime.Parse(r.GetString(3))
            };
        }

        public void SaveTtlOntology(TtlOntologyRecord rec)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO ttl_ontology (id, base_uri, base_prefix, json_payload, updated_at)
                VALUES (1, @u, @p, @j, datetime('now','localtime'))
                ON CONFLICT(id) DO UPDATE SET
                    base_uri    = excluded.base_uri,
                    base_prefix = excluded.base_prefix,
                    json_payload= excluded.json_payload,
                    updated_at  = excluded.updated_at", conn);
            cmd.Parameters.AddWithValue("@u", rec.BaseUri ?? "");
            cmd.Parameters.AddWithValue("@p", rec.BasePrefix ?? "");
            cmd.Parameters.AddWithValue("@j", rec.JsonPayload ?? "{}");
            cmd.ExecuteNonQuery();
        }

        // ── v4: user_setting ─────────────────────────────
        public string GetSetting(string group, string key, string fallback = "")
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT value FROM user_setting WHERE grp=@g AND key=@k", conn);
            cmd.Parameters.AddWithValue("@g", group);
            cmd.Parameters.AddWithValue("@k", key);
            var v = cmd.ExecuteScalar();
            return (v == null || v == DBNull.Value) ? fallback : v.ToString() ?? fallback;
        }

        public void SetSetting(string group, string key, string value)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(@"
                INSERT INTO user_setting (grp, key, value, updated_at)
                VALUES (@g, @k, @v, datetime('now','localtime'))
                ON CONFLICT(grp, key) DO UPDATE SET
                    value      = excluded.value,
                    updated_at = excluded.updated_at", conn);
            cmd.Parameters.AddWithValue("@g", group ?? "general");
            cmd.Parameters.AddWithValue("@k", key   ?? "");
            cmd.Parameters.AddWithValue("@v", value ?? "");
            cmd.ExecuteNonQuery();
        }

        public Dictionary<string,string> GetSettingGroup(string group)
        {
            var d = new Dictionary<string,string>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT key, value FROM user_setting WHERE grp=@g", conn);
            cmd.Parameters.AddWithValue("@g", group);
            using var r = cmd.ExecuteReader();
            while (r.Read()) d[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            return d;
        }
    }
}
