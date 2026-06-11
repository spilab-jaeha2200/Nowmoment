// ════════════════════════════════════════════════════════════
// CatalogPdfBuilder.cs — v3.0 F-004 PDF 카탈로그
//
// QuestPDF 를 사용해서 NowMoment 자산을 정부과제 산출물용 PDF 로 출력.
// 외부 도구 없이 1클릭 생성, 한글 정상 표시 (시스템 맑은 고딕 사용).
//
// 페이지 구성:
//   1)   표지       — 회사명, 보고서 제목, 생성 일시, 자산 카운트 요약
//   2)   목차
//   3+) 자산 카탈로그 (5종 자산을 종류별 표 형식)
//       - 소스코드 / AI 모델 / 문서·논문 / 특허·IP / 실험결과
//   N)   KG 통계 (도메인별 노드/엣지 카운트)
//   N+1) 끝맺음 (생성 정보 + SPILab 저작권)
//
// 사용:
//   var opts = new CatalogPdfOptions { Title = "...", Author = "..." };
//   var builder = new CatalogPdfBuilder(db, kg);
//   builder.Build(opts, @"C:\out\catalog.pdf");
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.Services.Pdf
{
    /// <summary>카탈로그 PDF 생성 시 사용자가 지정 가능한 옵션.</summary>
    public class CatalogPdfOptions
    {
        public string Title       { get; set; } = "기술자산 카탈로그";
        public string Subtitle    { get; set; } = "정부과제 산출물용 보고서";
        public string Author      { get; set; } = "SPILab Co., Ltd.";
        public string ProjectName { get; set; } = ""; // 비우면 모든 프로젝트
        public bool   IncludeCode       { get; set; } = true;
        public bool   IncludeModel      { get; set; } = true;
        public bool   IncludeDocument   { get; set; } = true;
        public bool   IncludePatent     { get; set; } = true;
        public bool   IncludeExperiment { get; set; } = true;
        public bool   IncludeKgSummary  { get; set; } = true;
    }

    public class CatalogPdfBuilder
    {
        private readonly DatabaseService _db;
        private readonly KnowledgeGraphService? _kg;

        // 색상 팔레트 (SPILab 다크 톤을 PDF용 밝은 톤으로 변환)
        private const string ColorAccent       = "#2E75B6"; // 파랑 — 헤더
        private const string ColorAccent2      = "#7DD8E8"; // 청록 — 강조
        private const string ColorTextDark     = "#1A2B3C"; // 본문
        private const string ColorTextMuted    = "#6E7E92"; // 부가정보
        private const string ColorTableHeader  = "#E6EEF6"; // 표 헤더 배경
        private const string ColorTableAltRow  = "#F5F8FB"; // 표 짝수 행
        private const string ColorBorder       = "#C5D2E0";

        // 시스템 폰트 (Windows 기본 설치)
        private const string FontKorean = "Malgun Gothic"; // 맑은 고딕

        public CatalogPdfBuilder(DatabaseService db, KnowledgeGraphService? kg = null)
        {
            _db = db;
            _kg = kg;

            // QuestPDF Community 라이선스 (MIT, 매출 100만 USD 미만 무료)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>옵션에 따라 PDF 를 생성하고 outputPath 에 저장.</summary>
        public void Build(CatalogPdfOptions opts, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("PDF 출력 경로가 비어있습니다.", nameof(outputPath));

            // 1) 데이터 로드 (필요 항목만)
            var codes        = opts.IncludeCode       ? _db.GetCodes()        : new();
            var models       = opts.IncludeModel      ? _db.GetModels()       : new();
            var docs         = opts.IncludeDocument   ? _db.GetDocuments()    : new();
            var patents      = opts.IncludePatent     ? _db.GetPatents()      : new();
            var experiments  = opts.IncludeExperiment ? _db.GetExperiments()  : new();

            // 프로젝트 명 필터 (선택)
            if (!string.IsNullOrEmpty(opts.ProjectName))
            {
                codes        = codes.Where(c => c.ProjectName == opts.ProjectName).ToList();
                models       = models.Where(m => m.ProjectName == opts.ProjectName).ToList();
                docs         = docs.Where(d => d.ProjectName == opts.ProjectName).ToList();
                // patent / experiment 은 ProjectName 컬럼이 없거나 다를 수 있어 그대로 둠
            }

            int totalCount = codes.Count + models.Count + docs.Count + patents.Count + experiments.Count;

            // 2) PDF 문서 생성
            Document.Create(container =>
            {
                container.Page(page => ComposeCoverPage(page, opts, totalCount,
                    codes.Count, models.Count, docs.Count, patents.Count, experiments.Count));

                container.Page(page => ComposeTocPage(page, opts,
                    codes.Count, models.Count, docs.Count, patents.Count, experiments.Count));

                if (opts.IncludeCode && codes.Count > 0)
                    container.Page(page => ComposeCodePage(page, codes));

                if (opts.IncludeModel && models.Count > 0)
                    container.Page(page => ComposeModelPage(page, models));

                if (opts.IncludeDocument && docs.Count > 0)
                    container.Page(page => ComposeDocumentPage(page, docs));

                if (opts.IncludePatent && patents.Count > 0)
                    container.Page(page => ComposePatentPage(page, patents));

                if (opts.IncludeExperiment && experiments.Count > 0)
                    container.Page(page => ComposeExperimentPage(page, experiments));

                if (opts.IncludeKgSummary && _kg != null)
                    container.Page(page => ComposeKgSummaryPage(page));

                container.Page(page => ComposeFinalPage(page));
            })
            .GeneratePdf(outputPath);
        }

        // ── 페이지 1: 표지 ────────────────────────────
        private void ComposeCoverPage(PageDescriptor page, CatalogPdfOptions opts,
            int total, int code, int model, int doc, int patent, int experiment)
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontFamily(FontKorean).FontSize(11).FontColor(ColorTextDark));

            page.Content().Column(col =>
            {
                col.Spacing(0);

                // 상단 여백
                col.Item().Height(150);

                // 제목 영역
                col.Item().PaddingBottom(8).Text(opts.Title)
                    .FontSize(36).Bold().FontColor(ColorAccent);

                col.Item().PaddingBottom(40).Text(opts.Subtitle)
                    .FontSize(16).FontColor(ColorTextMuted);

                // 구분선
                col.Item().LineHorizontal(2).LineColor(ColorAccent);

                // 자산 카운트 요약 카드
                col.Item().PaddingTop(40).Background(ColorTableHeader)
                    .Padding(24).Column(c =>
                    {
                        c.Item().PaddingBottom(12).Text("📊 자산 통계")
                            .FontSize(16).Bold().FontColor(ColorAccent);

                        c.Item().PaddingBottom(20).Text($"총 {total}건")
                            .FontSize(28).Bold().FontColor(ColorTextDark);

                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Column(cc =>
                            {
                                StatLine(cc, "소스코드", code);
                                StatLine(cc, "AI 모델", model);
                                StatLine(cc, "문서·논문", doc);
                            });
                            r.RelativeItem().Column(cc =>
                            {
                                StatLine(cc, "특허·IP", patent);
                                StatLine(cc, "실험결과", experiment);
                            });
                        });
                    });

                col.Item().Height(60);

                // 메타 정보
                col.Item().AlignRight().Column(c =>
                {
                    c.Item().Text(opts.Author).FontSize(12).Bold();
                    c.Item().Text($"생성일: {DateTime.Now:yyyy년 M월 d일}").FontSize(10).FontColor(ColorTextMuted);
                    if (!string.IsNullOrEmpty(opts.ProjectName))
                        c.Item().Text($"프로젝트: {opts.ProjectName}").FontSize(10).FontColor(ColorTextMuted);
                });
            });

            page.Footer().AlignCenter().Text("NowMoment v3.0 — 기술자산관리 시스템")
                .FontSize(8).FontColor(ColorTextMuted);
        }

        private void StatLine(ColumnDescriptor c, string label, int n)
        {
            c.Item().PaddingVertical(2).Row(r =>
            {
                r.RelativeItem().Text(label).FontSize(11);
                r.ConstantItem(60).AlignRight().Text(n.ToString())
                    .FontSize(13).Bold().FontColor(ColorAccent);
            });
        }

        // ── 페이지 2: 목차 ────────────────────────────
        private void ComposeTocPage(PageDescriptor page, CatalogPdfOptions opts,
            int code, int model, int doc, int patent, int experiment)
        {
            ApplyDefaultPage(page);

            page.Header().PaddingBottom(16).Column(col =>
            {
                col.Item().Text("📑 목차").FontSize(24).Bold().FontColor(ColorAccent);
                col.Item().LineHorizontal(1).LineColor(ColorBorder);
            });

            page.Content().Column(col =>
            {
                col.Spacing(8);
                int n = 1;
                if (opts.IncludeCode       && code       > 0) TocItem(col, $"{n++}. 소스코드",   $"{code}건");
                if (opts.IncludeModel      && model      > 0) TocItem(col, $"{n++}. AI 모델",   $"{model}건");
                if (opts.IncludeDocument   && doc        > 0) TocItem(col, $"{n++}. 문서·논문",  $"{doc}건");
                if (opts.IncludePatent     && patent     > 0) TocItem(col, $"{n++}. 특허·IP",   $"{patent}건");
                if (opts.IncludeExperiment && experiment > 0) TocItem(col, $"{n++}. 실험결과",   $"{experiment}건");
                if (opts.IncludeKgSummary  && _kg != null)    TocItem(col, $"{n++}. 지식그래프 통계", "");
            });

            ApplyDefaultFooter(page, "목차");
        }

        private void TocItem(ColumnDescriptor col, string label, string count)
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(label).FontSize(13);
                r.ConstantItem(80).AlignRight().Text(count).FontSize(11).FontColor(ColorTextMuted);
            });
        }

        // ── 페이지 3+: 자산 카탈로그 ──────────────────
        private void ComposeCodePage(PageDescriptor page, List<AssetCode> items)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "1. 소스코드", $"{items.Count}건");

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);   // ID
                    c.RelativeColumn(3);    // 자산명
                    c.RelativeColumn(2);    // 프로젝트
                    c.ConstantColumn(60);   // 언어
                    c.ConstantColumn(50);   // 버전
                    c.RelativeColumn(2);    // 태그
                });
                TableHeader(table, "ID", "자산명", "프로젝트", "언어", "버전", "태그");
                int row = 0;
                foreach (var a in items.OrderBy(x => x.Id))
                {
                    TableRow(table, ++row,
                        a.Id.ToString(), a.Name, a.ProjectName ?? "",
                        a.Language ?? "", a.Version ?? "", a.Tags ?? "");
                }
            });

            ApplyDefaultFooter(page, "1. 소스코드");
        }

        private void ComposeModelPage(PageDescriptor page, List<AssetModel> items)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "2. AI 모델", $"{items.Count}건");

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.ConstantColumn(70);
                    c.ConstantColumn(60);
                    c.RelativeColumn(2);
                });
                TableHeader(table, "ID", "모델명", "프로젝트", "프레임워크", "정확도", "기반 모델");
                int row = 0;
                foreach (var a in items.OrderBy(x => x.Id))
                {
                    string acc = (a.Accuracy.HasValue && a.Accuracy.Value > 0)
                        ? $"{a.Accuracy.Value:0.00}" : "-";
                    TableRow(table, ++row,
                        a.Id.ToString(), a.Name, a.ProjectName ?? "",
                        a.Framework ?? "", acc, a.BaseModel ?? "");
                }
            });

            ApplyDefaultFooter(page, "2. AI 모델");
        }

        private void ComposeDocumentPage(PageDescriptor page, List<AssetDocument> items)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "3. 문서·논문", $"{items.Count}건");

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(4);
                    c.RelativeColumn(2);
                    c.ConstantColumn(70);
                    c.ConstantColumn(50);
                });
                TableHeader(table, "ID", "제목", "프로젝트", "종류", "버전");
                int row = 0;
                foreach (var a in items.OrderBy(x => x.Id))
                {
                    TableRow(table, ++row,
                        a.Id.ToString(), a.Title, a.ProjectName ?? "",
                        a.DocType ?? "", a.Version ?? "");
                }
            });

            ApplyDefaultFooter(page, "3. 문서·논문");
        }

        private void ComposePatentPage(PageDescriptor page, List<AssetPatent> items)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "4. 특허·IP", $"{items.Count}건");

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(4);
                    c.ConstantColumn(110);
                    c.ConstantColumn(80);
                    c.ConstantColumn(80);
                });
                TableHeader(table, "ID", "제목", "출원번호", "상태", "출원일");
                int row = 0;
                foreach (var a in items.OrderBy(x => x.Id))
                {
                    string filingDate = a.FilingDate.HasValue
                        ? a.FilingDate.Value.ToString("yyyy-MM-dd")
                        : "";
                    TableRow(table, ++row,
                        a.Id.ToString(), a.Title, a.ApplicationNo ?? "",
                        a.Status ?? "", filingDate);
                }
            });

            ApplyDefaultFooter(page, "4. 특허·IP");
        }

        private void ComposeExperimentPage(PageDescriptor page, List<AssetExperiment> items)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "5. 실험결과", $"{items.Count}건");

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.ConstantColumn(70);
                });
                TableHeader(table, "ID", "실험명", "관련 자산", "상태");
                int row = 0;
                foreach (var a in items.OrderBy(x => x.Id))
                {
                    TableRow(table, ++row,
                        a.Id.ToString(), a.Name, a.AssetRef ?? "", a.Status ?? "");
                }
            });

            ApplyDefaultFooter(page, "5. 실험결과");
        }

        // ── KG 통계 페이지 ──────────────────────────
        private void ComposeKgSummaryPage(PageDescriptor page)
        {
            ApplyDefaultPage(page);
            ApplyAssetHeader(page, "6. 지식그래프 통계", "");

            page.Content().Column(col =>
            {
                col.Spacing(12);

                if (_kg == null)
                {
                    col.Item().Text("KG 서비스가 초기화되지 않았습니다.").FontColor(ColorTextMuted);
                    return;
                }

                try
                {
                    var stats = _kg.GetStats();
                    col.Item().Background(ColorTableHeader).Padding(16).Column(c =>
                    {
                        c.Item().PaddingBottom(8).Text("전체 그래프").FontSize(14).Bold().FontColor(ColorAccent);
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Column(cc => StatLine(cc, "전체 노드", stats.Nodes));
                            r.RelativeItem().Column(cc => StatLine(cc, "전체 엣지", stats.Edges));
                        });
                    });

                    if (stats.NodesByType != null && stats.NodesByType.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("노드 타입별 분포").FontSize(13).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.ConstantColumn(80);
                            });
                            TableHeader(table, "타입", "개수");
                            int row = 0;
                            foreach (var kv in stats.NodesByType.OrderByDescending(x => x.Value))
                                TableRow(table, ++row, kv.Key, kv.Value.ToString());
                        });
                    }

                    if (stats.EdgesByRel != null && stats.EdgesByRel.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("관계 타입별 분포").FontSize(13).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.ConstantColumn(80);
                            });
                            TableHeader(table, "관계", "개수");
                            int row = 0;
                            foreach (var kv in stats.EdgesByRel.OrderByDescending(x => x.Value))
                                TableRow(table, ++row, kv.Key, kv.Value.ToString());
                        });
                    }
                }
                catch (Exception ex)
                {
                    col.Item().Text($"KG 통계 로드 실패: {ex.Message}").FontColor("#A04040");
                }
            });

            ApplyDefaultFooter(page, "6. 지식그래프 통계");
        }

        // ── 마지막 페이지 ──────────────────────────
        private void ComposeFinalPage(PageDescriptor page)
        {
            ApplyDefaultPage(page);

            page.Content().AlignCenter().AlignMiddle().Column(col =>
            {
                col.Spacing(16);
                col.Item().AlignCenter().Text("— 끝 —").FontSize(20).Bold().FontColor(ColorAccent);
                col.Item().AlignCenter().Text($"본 카탈로그는 NowMoment v3.0 으로 자동 생성되었습니다.")
                    .FontSize(11).FontColor(ColorTextMuted);
                col.Item().AlignCenter().Text($"생성 일시: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                    .FontSize(10).FontColor(ColorTextMuted);
                col.Item().PaddingTop(24).AlignCenter().Text("SPILab Co., Ltd.")
                    .FontSize(13).Bold().FontColor(ColorAccent);
                col.Item().AlignCenter().Text("Physics-aware Hybrid AI Platform")
                    .FontSize(10).FontColor(ColorTextMuted);
            });

            ApplyDefaultFooter(page, "");
        }

        // ── 공통 헬퍼 ──────────────────────────────────
        private void ApplyDefaultPage(PageDescriptor page)
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontFamily(FontKorean).FontSize(10).FontColor(ColorTextDark));
        }

        private void ApplyAssetHeader(PageDescriptor page, string title, string subtitle)
        {
            page.Header().PaddingBottom(12).Column(col =>
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(title).FontSize(20).Bold().FontColor(ColorAccent);
                    if (!string.IsNullOrEmpty(subtitle))
                        r.ConstantItem(80).AlignRight().Text(subtitle)
                            .FontSize(12).FontColor(ColorTextMuted);
                });
                col.Item().LineHorizontal(1).LineColor(ColorBorder);
            });
        }

        private void ApplyDefaultFooter(PageDescriptor page, string sectionLabel)
        {
            page.Footer().Row(r =>
            {
                r.RelativeItem().AlignLeft().Text(sectionLabel)
                    .FontSize(8).FontColor(ColorTextMuted);
                r.RelativeItem().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(ColorTextMuted));
                    t.Span("- ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                    t.Span(" -");
                });
                r.RelativeItem().AlignRight().Text("NowMoment v3.0")
                    .FontSize(8).FontColor(ColorTextMuted);
            });
        }

        // ── 표 헬퍼 ────────────────────────────────────
        private void TableHeader(TableDescriptor table, params string[] columns)
        {
            table.Header(header =>
            {
                foreach (var c in columns)
                {
                    header.Cell().Background(ColorAccent)
                        .Padding(6)
                        .Text(c).FontColor(Colors.White).Bold().FontSize(10);
                }
            });
        }

        private void TableRow(TableDescriptor table, int rowIndex, params string[] cells)
        {
            string bg = (rowIndex % 2 == 0) ? ColorTableAltRow : Colors.White;
            foreach (var cell in cells)
            {
                table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(ColorBorder)
                    .Padding(6).Text(cell ?? "").FontSize(9);
            }
        }
    }
}
