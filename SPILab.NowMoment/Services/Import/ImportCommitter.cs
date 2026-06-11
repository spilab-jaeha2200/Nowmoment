// ════════════════════════════════════════════════════════════
// ImportCommitter.cs — v3.0 F-002 폴더 임포트 (Step 2.4)
//
// FolderImportViewModel 이 사용자에게 [확정] 받은 ImportCandidate 들을
// 실제 DB 에 INSERT 한다.
//
// 중복 검사:
//   - 코드: repo_url 일치 여부
//   - 모델/문서: file_path 일치 여부
//   - 실험: name 일치 여부 (간단)
//   중복으로 판정되면 INSERT 를 건너뛰고 로그에 표시.
//
// 트랜잭션: 각 INSERT 는 독립 — 일부 실패해도 나머지는 진행.
//           기획서 결정대로 batch 단위 롤백은 하지 않음 (단순성 우선).
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services.Import
{
    public static class ImportCommitter
    {
        /// <summary>선택된 후보들을 DB 에 INSERT.
        /// 반환: (성공 건수, 실패 건수, 결과 로그 문자열).</summary>
        public static (int ok, int fail, string log)
            CommitAll(DatabaseService db, List<ImportCandidate> selected, int projectId)
        {
            int ok = 0, fail = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"임포트 결과 — 시도 {selected.Count} 건");
            sb.AppendLine(new string('─', 40));

            // 사전에 한 번만 기존 자산 키들을 모아두고 in-memory 체크 — N×SELECT 회피
            var existingRepoUrls   = LoadDistinct(db, "SELECT repo_url   FROM asset_code WHERE repo_url   <> ''");
            var existingModelPaths = LoadDistinct(db, "SELECT file_path  FROM asset_model WHERE file_path  <> ''");
            var existingDocPaths   = LoadDistinct(db, "SELECT file_path  FROM asset_document WHERE file_path <> ''");
            var existingExpNames   = LoadDistinct(db, "SELECT name       FROM asset_experiment");

            foreach (var c in selected)
            {
                try
                {
                    string label = $"  · [{c.KindLabel}] {c.Name}";

                    switch (c.Kind)
                    {
                        case ImportAssetKind.Code:
                            if (!string.IsNullOrEmpty(c.RepoUrl) && existingRepoUrls.Contains(c.RepoUrl))
                            {
                                sb.AppendLine($"{label}  — 중복 (repo_url 일치) → 스킵");
                                continue;
                            }
                            db.InsertCode(new AssetCode
                            {
                                Name        = c.Name,
                                RepoUrl     = c.RepoUrl,
                                Language    = string.IsNullOrEmpty(c.Language) ? "Python" : c.Language,
                                Version     = string.IsNullOrEmpty(c.Version) ? "1.0.0" : c.Version,
                                ProjectId   = projectId,
                                Tags        = c.Tags,
                                Description = c.Description,
                            });
                            existingRepoUrls.Add(c.RepoUrl);
                            ok++;
                            sb.AppendLine($"{label}  — 추가됨");
                            break;

                        case ImportAssetKind.Model:
                            if (!string.IsNullOrEmpty(c.SourcePath) && existingModelPaths.Contains(c.SourcePath))
                            {
                                sb.AppendLine($"{label}  — 중복 (file_path 일치) → 스킵");
                                continue;
                            }
                            db.InsertModel(new AssetModel
                            {
                                Name        = c.Name,
                                Framework   = string.IsNullOrEmpty(c.Framework) ? "기타" : c.Framework,
                                FilePath    = c.SourcePath,
                                ProjectId   = projectId,
                                Description = $"파일 크기: {FormatSize(c.FileSizeBytes)}",
                            });
                            existingModelPaths.Add(c.SourcePath);
                            ok++;
                            sb.AppendLine($"{label}  — 추가됨");
                            break;

                        case ImportAssetKind.Document:
                            if (!string.IsNullOrEmpty(c.SourcePath) && existingDocPaths.Contains(c.SourcePath))
                            {
                                sb.AppendLine($"{label}  — 중복 (file_path 일치) → 스킵");
                                continue;
                            }
                            db.InsertDocument(new AssetDocument
                            {
                                Title     = c.Name,
                                DocType   = string.IsNullOrEmpty(c.DocType) ? "document" : c.DocType,
                                FilePath  = c.SourcePath,
                                ProjectId = projectId,
                                Version   = string.IsNullOrEmpty(c.Version) ? "1.0" : c.Version,
                                Summary   = c.Summary,
                            });
                            existingDocPaths.Add(c.SourcePath);
                            ok++;
                            sb.AppendLine($"{label}  — 추가됨");
                            break;

                        case ImportAssetKind.Experiment:
                            if (existingExpNames.Contains(c.Name))
                            {
                                sb.AppendLine($"{label}  — 중복 (name 일치) → 스킵");
                                continue;
                            }
                            db.InsertExperiment(new AssetExperiment
                            {
                                Name       = c.Name,
                                AssetRef   = c.SourcePath,
                                Params     = c.ParamsJson,
                                Metrics    = c.MetricsJson,
                                ResultPath = c.SourcePath,
                                Status     = "completed",
                            });
                            existingExpNames.Add(c.Name);
                            ok++;
                            sb.AppendLine($"{label}  — 추가됨");
                            break;

                        default:
                            sb.AppendLine($"{label}  — 미분류 → 스킵");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    sb.AppendLine($"  · [{c.KindLabel}] {c.Name}  — 실패: {ex.Message}");
                }
            }

            sb.AppendLine(new string('─', 40));
            sb.AppendLine($"성공 {ok} 건 · 실패 {fail} 건 · 스킵 {selected.Count - ok - fail} 건");
            return (ok, fail, sb.ToString());
        }

        // ── 헬퍼 ────────────────────────────────────────
        private static HashSet<string> LoadDistinct(DatabaseService db, string sql)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = new SqliteConnection(db.ConnectionString);
                conn.Open();
                using var cmd = new SqliteCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var v = r.GetString(0);
                    if (!string.IsNullOrEmpty(v)) set.Add(v);
                }
            }
            catch { /* 빈 셋 반환 */ }
            return set;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
