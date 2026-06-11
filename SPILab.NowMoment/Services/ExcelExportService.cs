// ════════════════════════════════════════════════════════════════════
// ExcelExportService.cs — v4 Phase 3
//
// 자산 5종(또는 단일 종) 데이터를 .xlsx 로 내보내기.
// ClosedXML 사용. 시트별로 한 자산 타입씩 저장.
//
// 사용:
//   var svc = new ExcelExportService(db);
//   svc.ExportAll("C:\\out\\catalog.xlsx");      // 5종 모두 5개 시트
//   svc.ExportSingle("code", "C:\\out\\code.xlsx");
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.Services
{
    public class ExcelExportService
    {
        private readonly DatabaseService _db;
        public ExcelExportService(DatabaseService db) { _db = db; }

        // ── 공개 API ────────────────────────────────────
        public void ExportAll(string filePath)
        {
            using var wb = new XLWorkbook();
            WriteCodeSheet      (wb, _db.GetCodes());
            WriteModelSheet     (wb, _db.GetModels());
            WriteDocumentSheet  (wb, _db.GetDocuments());
            WritePatentSheet    (wb, _db.GetPatents());
            WriteExperimentSheet(wb, _db.GetExperiments());
            wb.SaveAs(filePath);
        }

        public void ExportSingle(string kind, string filePath)
        {
            using var wb = new XLWorkbook();
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "code":       WriteCodeSheet(wb, _db.GetCodes()); break;
                case "model":      WriteModelSheet(wb, _db.GetModels()); break;
                case "document":   WriteDocumentSheet(wb, _db.GetDocuments()); break;
                case "patent":     WritePatentSheet(wb, _db.GetPatents()); break;
                case "experiment": WriteExperimentSheet(wb, _db.GetExperiments()); break;
                default: throw new ArgumentException($"알 수 없는 자산 종류: {kind}");
            }
            wb.SaveAs(filePath);
        }

        /// <summary>임의 자산 리스트(예: 그리드 선택 항목)를 시트 1개로 저장.</summary>
        public void ExportSelected<T>(IEnumerable<T> items, string sheetName, string filePath) where T : class
        {
            using var wb = new XLWorkbook();
            if (items is IEnumerable<AssetCode>       codes)       WriteCodeSheet      (wb, ToList(codes), sheetName);
            else if (items is IEnumerable<AssetModel> models)      WriteModelSheet     (wb, ToList(models), sheetName);
            else if (items is IEnumerable<AssetDocument> docs)     WriteDocumentSheet  (wb, ToList(docs), sheetName);
            else if (items is IEnumerable<AssetPatent>   pats)     WritePatentSheet    (wb, ToList(pats), sheetName);
            else if (items is IEnumerable<AssetExperiment> exps)   WriteExperimentSheet(wb, ToList(exps), sheetName);
            else throw new ArgumentException("지원되지 않는 자산 타입");
            wb.SaveAs(filePath);
        }

        private static List<T> ToList<T>(IEnumerable<T> src)
        {
            var list = new List<T>();
            foreach (var it in src) list.Add(it);
            return list;
        }

        // ── 시트별 작성 ─────────────────────────────────
        private static void WriteCodeSheet(XLWorkbook wb, List<AssetCode> items, string? sheetName = null)
        {
            var ws = wb.Worksheets.Add(sheetName ?? "소스코드");
            ws.Cell(1, 1).InsertTable(new[]
            {
                new {
                    ID = (int)0, 자산명 = "", 언어 = "", 버전 = "",
                    프로젝트 = "", 태그 = "", 설명 = "", 등록일 = "",
                }
            }, "소스코드", false);  // 헤더만 먼저
            int r = 2;
            foreach (var a in items)
            {
                ws.Cell(r, 1).Value = a.Id;
                ws.Cell(r, 2).Value = a.Name;
                ws.Cell(r, 3).Value = a.Language;
                ws.Cell(r, 4).Value = a.Version;
                ws.Cell(r, 5).Value = a.ProjectName;
                ws.Cell(r, 6).Value = a.Tags;
                ws.Cell(r, 7).Value = a.Description;
                ws.Cell(r, 8).Value = a.CreatedAt.ToString("yyyy-MM-dd");
                r++;
            }
            FormatSheet(ws, 8);
        }

        private static void WriteModelSheet(XLWorkbook wb, List<AssetModel> items, string? sheetName = null)
        {
            var ws = wb.Worksheets.Add(sheetName ?? "AI 모델");
            string[] hdr = { "ID","모델명","프레임워크","정확도","파일경로","프로젝트","기반모델","설명","등록일" };
            for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i + 1).Value = hdr[i];
            int r = 2;
            foreach (var a in items)
            {
                ws.Cell(r, 1).Value = a.Id;
                ws.Cell(r, 2).Value = a.Name;
                ws.Cell(r, 3).Value = a.Framework;
                ws.Cell(r, 4).Value = a.Accuracy.HasValue ? a.Accuracy.Value : (double?)null;
                ws.Cell(r, 5).Value = a.FilePath;
                ws.Cell(r, 6).Value = a.ProjectName;
                ws.Cell(r, 7).Value = a.BaseModel;
                ws.Cell(r, 8).Value = a.Description;
                ws.Cell(r, 9).Value = a.CreatedAt.ToString("yyyy-MM-dd");
                r++;
            }
            FormatSheet(ws, hdr.Length);
        }

        private static void WriteDocumentSheet(XLWorkbook wb, List<AssetDocument> items, string? sheetName = null)
        {
            var ws = wb.Worksheets.Add(sheetName ?? "문서");
            string[] hdr = { "ID","제목","유형","버전","프로젝트","요약","파일경로","등록일" };
            for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i + 1).Value = hdr[i];
            int r = 2;
            foreach (var a in items)
            {
                ws.Cell(r, 1).Value = a.Id;
                ws.Cell(r, 2).Value = a.Title;
                ws.Cell(r, 3).Value = a.DocType;
                ws.Cell(r, 4).Value = a.Version;
                ws.Cell(r, 5).Value = a.ProjectName;
                ws.Cell(r, 6).Value = a.Summary;
                ws.Cell(r, 7).Value = a.FilePath;
                ws.Cell(r, 8).Value = a.CreatedAt.ToString("yyyy-MM-dd");
                r++;
            }
            FormatSheet(ws, hdr.Length);
        }

        private static void WritePatentSheet(XLWorkbook wb, List<AssetPatent> items, string? sheetName = null)
        {
            var ws = wb.Worksheets.Add(sheetName ?? "특허");
            string[] hdr = { "ID","특허명","출원번호","상태","출원일","발명자","설명","등록일" };
            for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i + 1).Value = hdr[i];
            int r = 2;
            foreach (var a in items)
            {
                ws.Cell(r, 1).Value = a.Id;
                ws.Cell(r, 2).Value = a.Title;
                ws.Cell(r, 3).Value = a.ApplicationNo;
                ws.Cell(r, 4).Value = a.Status;
                ws.Cell(r, 5).Value = a.FilingDate?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(r, 6).Value = a.Inventors;
                ws.Cell(r, 7).Value = a.Description;
                ws.Cell(r, 8).Value = a.CreatedAt.ToString("yyyy-MM-dd");
                r++;
            }
            FormatSheet(ws, hdr.Length);
        }

        private static void WriteExperimentSheet(XLWorkbook wb, List<AssetExperiment> items, string? sheetName = null)
        {
            var ws = wb.Worksheets.Add(sheetName ?? "실험");
            string[] hdr = { "ID","실험명","자산참조","상태","파라미터","메트릭","결과경로","등록일" };
            for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i + 1).Value = hdr[i];
            int r = 2;
            foreach (var a in items)
            {
                ws.Cell(r, 1).Value = a.Id;
                ws.Cell(r, 2).Value = a.Name;
                ws.Cell(r, 3).Value = a.AssetRef;
                ws.Cell(r, 4).Value = a.Status;
                ws.Cell(r, 5).Value = a.Params;
                ws.Cell(r, 6).Value = a.Metrics;
                ws.Cell(r, 7).Value = a.ResultPath;
                ws.Cell(r, 8).Value = a.CreatedAt.ToString("yyyy-MM-dd");
                r++;
            }
            FormatSheet(ws, hdr.Length);
        }

        // ── 공통 서식 ────────────────────────────────────
        private static void FormatSheet(IXLWorksheet ws, int colCount)
        {
            // 헤더 행
            var hdr = ws.Range(1, 1, 1, colCount);
            hdr.Style.Font.Bold = true;
            hdr.Style.Font.FontColor = XLColor.White;
            hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
            // 너무 좁거나 너무 넓은 컬럼 폭 보정
            foreach (var col in ws.ColumnsUsed())
            {
                if (col.Width < 10) col.Width = 10;
                if (col.Width > 50) col.Width = 50;
            }
        }
    }
}
