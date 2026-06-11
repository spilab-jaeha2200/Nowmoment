// ════════════════════════════════════════════════════════════════════
// TtlStudioViewModel.V4Persist.cs — v4 Phase 3
//
// TTL Studio 작업 내용을 SQLite ttl_ontology (단일 행) 에 자동 저장/복원.
// 앱 재시작 시 마지막 작업 상태가 그대로 복원됨.
//
// 동작:
//   - AttachDatabase(db) 호출 시: 기존 행이 있으면 즉시 로드 → 컬렉션 채움
//   - 컬렉션 변경 / 헤더 변경 시: 디바운스 (1초) 후 자동 저장
//   - 명시적 SaveSnapshot() / LoadSnapshot() 도 제공
//
// 직렬화: System.Text.Json. TtlOntology 의 4개 컬렉션을 평탄한 POCO 로 매핑.
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Models.Ttl;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public partial class TtlStudioViewModel
    {
        private DatabaseService? _db;
        private Timer? _autosaveTimer;
        private bool _suppressAutosave;  // 로드 중 변경 이벤트로 인한 즉시 저장 방지
        private const int AutosaveDebounceMs = 1000;

        /// <summary>
        /// DB 연결을 부착하고 마지막 스냅샷을 로드.
        /// MainWindow 초기화 시 main.TtlStudio.AttachDatabase(db) 호출.
        /// </summary>
        public void AttachDatabase(DatabaseService db)
        {
            _db = db;

            // 컬렉션 변경 시 자동 저장 트리거 (디바운스)
            Classes.CollectionChanged    += OnTtlChanged;
            Properties.CollectionChanged += OnTtlChanged;
            Instances.CollectionChanged  += OnTtlChanged;
            Triples.CollectionChanged    += OnTtlChanged;

            // 헤더(BaseUri/BasePrefix) 변경도 트리거
            this.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BaseUri) || e.PropertyName == nameof(BasePrefix))
                    ScheduleAutosave();
            };

            // 마지막 스냅샷 복원
            try { LoadSnapshot(); }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ TTL 자동 복원 실패: {ex.Message}";
            }
        }

        private void OnTtlChanged(object? sender, EventArgs e) => ScheduleAutosave();

        private void ScheduleAutosave()
        {
            if (_db == null || _suppressAutosave) return;
            _autosaveTimer?.Dispose();
            _autosaveTimer = new Timer(_ => SafeSave(), null, AutosaveDebounceMs, Timeout.Infinite);
        }

        private void SafeSave()
        {
            try { SaveSnapshot(); }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    StatusMessage = $"⚠ TTL 자동 저장 실패: {ex.Message}");
            }
        }

        /// <summary>현재 메모리 상태를 ttl_ontology 단일 행에 저장.</summary>
        public void SaveSnapshot()
        {
            if (_db == null) return;
            var payload = new TtlSnapshotDto
            {
                Classes    = new List<TtlClassDto>(),
                Properties = new List<TtlPropertyDto>(),
                Instances  = new List<TtlInstanceDto>(),
                Triples    = new List<TtlTripleDto>(),
            };
            foreach (var c in Classes)
                payload.Classes.Add(new TtlClassDto {
                    LocalName = c.LocalName, Label = c.Label, Comment = c.Comment,
                    ParentClass = c.ParentClass
                });
            foreach (var p in Properties)
                payload.Properties.Add(new TtlPropertyDto {
                    LocalName = p.LocalName, Label = p.Label, Comment = p.Comment,
                    Kind = p.Kind.ToString(), Domain = p.Domain, Range = p.Range
                });
            foreach (var i in Instances)
                payload.Instances.Add(new TtlInstanceDto {
                    LocalName = i.LocalName, TypeClass = i.ClassOf, Label = i.Label
                });
            foreach (var t in Triples)
                payload.Triples.Add(new TtlTripleDto {
                    Subject = t.Subject, Predicate = t.Predicate, Object = t.ObjectValue,
                    IsLiteral = t.ObjectIsLiteral
                });

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            _db.SaveTtlOntology(new TtlOntologyRecord
            {
                BaseUri = BaseUri,
                BasePrefix = BasePrefix,
                JsonPayload = json,
            });
        }

        /// <summary>ttl_ontology 단일 행을 읽어 컬렉션 채움. 없으면 no-op.</summary>
        public void LoadSnapshot()
        {
            if (_db == null) return;
            var rec = _db.LoadTtlOntology();
            if (rec == null) return;

            _suppressAutosave = true;
            try
            {
                BaseUri    = rec.BaseUri;
                BasePrefix = rec.BasePrefix;

                var payload = JsonSerializer.Deserialize<TtlSnapshotDto>(rec.JsonPayload);
                if (payload == null) return;

                Classes.Clear();
                foreach (var c in payload.Classes)
                    Classes.Add(new TtlClass { LocalName = c.LocalName, Label = c.Label,
                                               Comment = c.Comment, ParentClass = c.ParentClass });

                Properties.Clear();
                foreach (var p in payload.Properties)
                    Properties.Add(new TtlProperty {
                        LocalName = p.LocalName, Label = p.Label, Comment = p.Comment,
                        Kind = Enum.TryParse<TtlPropertyKind>(p.Kind, out var k) ? k : TtlPropertyKind.ObjectProperty,
                        Domain = p.Domain, Range = p.Range
                    });

                Instances.Clear();
                foreach (var i in payload.Instances)
                    Instances.Add(new TtlInstance { LocalName = i.LocalName, ClassOf = i.TypeClass, Label = i.Label });

                Triples.Clear();
                foreach (var t in payload.Triples)
                    Triples.Add(new TtlTriple { Subject = t.Subject, Predicate = t.Predicate,
                                                ObjectValue = t.Object, ObjectIsLiteral = t.IsLiteral });

                StatusMessage = $"✓ 마지막 저장본 복원 ({rec.UpdatedAt:yyyy-MM-dd HH:mm})";
            }
            finally { _suppressAutosave = false; }
        }

        // ── DTO (직렬화 전용) ────────────────────────────
        private class TtlSnapshotDto
        {
            public List<TtlClassDto>    Classes    { get; set; } = new();
            public List<TtlPropertyDto> Properties { get; set; } = new();
            public List<TtlInstanceDto> Instances  { get; set; } = new();
            public List<TtlTripleDto>   Triples    { get; set; } = new();
        }
        private class TtlClassDto
        {
            public string LocalName { get; set; } = "";
            public string Label { get; set; } = "";
            public string Comment { get; set; } = "";
            public string ParentClass { get; set; } = "";
        }
        private class TtlPropertyDto
        {
            public string LocalName { get; set; } = "";
            public string Label { get; set; } = "";
            public string Comment { get; set; } = "";
            public string Kind { get; set; } = "ObjectProperty";
            public string Domain { get; set; } = "";
            public string Range { get; set; } = "";
        }
        private class TtlInstanceDto
        {
            public string LocalName { get; set; } = "";
            public string TypeClass { get; set; } = "";
            public string Label { get; set; } = "";
        }
        private class TtlTripleDto
        {
            public string Subject { get; set; } = "";
            public string Predicate { get; set; } = "";
            public string Object { get; set; } = "";
            public bool IsLiteral { get; set; }
        }
    }
}
