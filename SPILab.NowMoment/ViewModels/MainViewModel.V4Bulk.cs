// ════════════════════════════════════════════════════════════════════
// MainViewModel.V4Bulk.cs — v4 Phase 2: 일괄 삭제 + 복제 핸들러
//
// DataGrid 의 SelectedItems (IList) 또는 단일 객체를 받아 처리.
// 모든 삭제·복제는 audit_log 에 기록되며, 복제 시 신규 자산은 별도 create 로 기록.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.ViewModels
{
    public partial class MainViewModel
    {
        // ── 공통 헬퍼 ─────────────────────────────────────
        private static IEnumerable<T> Extract<T>(object? p) where T : class
        {
            if (p is T single) return new[] { single };
            if (p is IList list)
                return list.OfType<T>().ToList();
            return Array.Empty<T>();
        }

        private bool ConfirmBulk(int count, string what)
        {
            if (count == 0) return false;
            var msg = count == 1
                ? $"1개 {what} 를 삭제합니까?"
                : $"{count}개 {what} 를 일괄 삭제합니까?";
            return Confirm(msg);
        }

        // ── 일괄 삭제 ─────────────────────────────────────
        private void BulkDeleteCode(object? p)
        {
            var items = Extract<AssetCode>(p).ToList();
            if (!ConfirmBulk(items.Count, "소스코드 자산")) return;
            foreach (var a in items)
            {
                var snap = _db.GetCodeById(a.Id);
                _db.DeleteAsset("asset_code", a.Id);
                Audit.LogDelete("asset_code", a.Id, snap);
            }
            LoadCodes(); LoadStats();
        }

        private void BulkDeleteModel(object? p)
        {
            var items = Extract<AssetModel>(p).ToList();
            if (!ConfirmBulk(items.Count, "AI 모델")) return;
            foreach (var a in items)
            {
                var snap = _db.GetModelById(a.Id);
                _db.DeleteAsset("asset_model", a.Id);
                Audit.LogDelete("asset_model", a.Id, snap);
            }
            LoadModels(); LoadStats();
        }

        private void BulkDeleteDocument(object? p)
        {
            var items = Extract<AssetDocument>(p).ToList();
            if (!ConfirmBulk(items.Count, "문서")) return;
            foreach (var a in items)
            {
                var snap = _db.GetDocumentById(a.Id);
                _db.DeleteAsset("asset_document", a.Id);
                Audit.LogDelete("asset_document", a.Id, snap);
            }
            LoadDocuments(); LoadStats();
        }

        private void BulkDeletePatent(object? p)
        {
            var items = Extract<AssetPatent>(p).ToList();
            if (!ConfirmBulk(items.Count, "특허")) return;
            foreach (var a in items)
            {
                var snap = _db.GetPatentById(a.Id);
                _db.DeleteAsset("asset_patent", a.Id);
                Audit.LogDelete("asset_patent", a.Id, snap);
            }
            LoadPatents(); LoadStats();
        }

        private void BulkDeleteExperiment(object? p)
        {
            var items = Extract<AssetExperiment>(p).ToList();
            if (!ConfirmBulk(items.Count, "실험")) return;
            foreach (var a in items)
            {
                var snap = _db.GetExperimentById(a.Id);
                _db.DeleteAsset("asset_experiment", a.Id);
                Audit.LogDelete("asset_experiment", a.Id, snap);
            }
            LoadExperiments(); LoadStats();
        }

        // ── 복제 ─────────────────────────────────────────
        private static string MakeCopyName(string name) =>
            string.IsNullOrEmpty(name) ? "(복사본)" : $"{name} (복사본)";

        private void DuplicateCode(object? p)
        {
            if (p is not AssetCode src) return;
            var copy = new AssetCode {
                Name = MakeCopyName(src.Name),
                RepoUrl = src.RepoUrl, Language = src.Language, Version = src.Version,
                ProjectId = src.ProjectId, Tags = src.Tags, Description = src.Description,
            };
            _db.InsertCode(copy);
            var newId = _db.GetLastInsertId("asset_code");
            copy.Id = (int)newId;
            Audit.LogCreate("asset_code", newId, copy);
            _db.SyncAssetTags("asset_code", newId, copy.Tags);
            LoadCodes(); LoadStats();
        }

        private void DuplicateModel(object? p)
        {
            if (p is not AssetModel src) return;
            var copy = new AssetModel {
                Name = MakeCopyName(src.Name),
                Framework = src.Framework, Accuracy = src.Accuracy, FilePath = src.FilePath,
                ProjectId = src.ProjectId, BaseModel = src.BaseModel, Description = src.Description,
            };
            _db.InsertModel(copy);
            var newId = _db.GetLastInsertId("asset_model");
            copy.Id = (int)newId;
            Audit.LogCreate("asset_model", newId, copy);
            LoadModels(); LoadStats();
        }

        private void DuplicateDocument(object? p)
        {
            if (p is not AssetDocument src) return;
            var copy = new AssetDocument {
                Title = MakeCopyName(src.Title),
                DocType = src.DocType, FilePath = src.FilePath, ProjectId = src.ProjectId,
                Version = src.Version, Summary = src.Summary,
            };
            _db.InsertDocument(copy);
            var newId = _db.GetLastInsertId("asset_document");
            copy.Id = (int)newId;
            Audit.LogCreate("asset_document", newId, copy);
            LoadDocuments(); LoadStats();
        }

        private void DuplicatePatent(object? p)
        {
            if (p is not AssetPatent src) return;
            var copy = new AssetPatent {
                Title = MakeCopyName(src.Title),
                ApplicationNo = src.ApplicationNo, Status = src.Status,
                FilingDate = src.FilingDate, Inventors = src.Inventors, Description = src.Description,
            };
            _db.InsertPatent(copy);
            var newId = _db.GetLastInsertId("asset_patent");
            copy.Id = (int)newId;
            Audit.LogCreate("asset_patent", newId, copy);
            LoadPatents(); LoadStats();
        }

        private void DuplicateExperiment(object? p)
        {
            if (p is not AssetExperiment src) return;
            var copy = new AssetExperiment {
                Name = MakeCopyName(src.Name),
                AssetRef = src.AssetRef, Params = src.Params, Metrics = src.Metrics,
                ResultPath = src.ResultPath, Status = src.Status,
            };
            _db.InsertExperiment(copy);
            var newId = _db.GetLastInsertId("asset_experiment");
            copy.Id = (int)newId;
            Audit.LogCreate("asset_experiment", newId, copy);
            LoadExperiments(); LoadStats();
        }
    }
}
