// ════════════════════════════════════════════════════════════
// TtlModels.cs — v3.0 F-005 TTL Studio (Step 5.1)
//
// 메모리 상의 OWL/RDFS 온톨로지 모델.
// .ttl 파일로 입출력하며, 사용자 편집은 ViewModel 을 통해 이뤄짐.
//
// 구성:
//   - TtlOntology : 4가지 컬렉션의 컨테이너 + 네임스페이스 prefix 관리
//   - TtlClass    : 클래스 (rdfs:Class / owl:Class)
//   - TtlProperty : 속성 (owl:ObjectProperty / owl:DatatypeProperty)
//   - TtlInstance : 인스턴스 (특정 클래스의 개체)
//   - TtlTriple   : 자유 트리플 (s/p/o)
//
// 모든 클래스는 INotifyPropertyChanged 를 구현하여 DataGrid 양방향 바인딩.
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SPILab.NowMoment.Models.Ttl
{
    public enum TtlPropertyKind
    {
        ObjectProperty,    // 다른 인스턴스/클래스 참조 (owl:ObjectProperty)
        DatatypeProperty,  // 리터럴 값 (owl:DatatypeProperty) — string, int, double, date
    }

    /// <summary>OWL/RDFS 클래스 정의.</summary>
    public class TtlClass : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _localName = "";
        private string _label = "";
        private string _comment = "";
        private string _parentClass = "";

        /// <summary>로컬 이름 (예: "Project") — IRI는 prefix 와 결합되어 :Project 가 됨.</summary>
        public string LocalName
        {
            get => _localName;
            set { if (_localName != value) { _localName = value; OnChanged(nameof(LocalName)); } }
        }

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; OnChanged(nameof(Label)); } }
        }

        public string Comment
        {
            get => _comment;
            set { if (_comment != value) { _comment = value; OnChanged(nameof(Comment)); } }
        }

        /// <summary>상위 클래스 LocalName ("" 이면 owl:Thing 의 직속 자식)</summary>
        public string ParentClass
        {
            get => _parentClass;
            set { if (_parentClass != value) { _parentClass = value; OnChanged(nameof(ParentClass)); } }
        }

        public override string ToString() => string.IsNullOrEmpty(_label) ? _localName : $"{_localName} ({_label})";
    }

    /// <summary>OWL Property — Object 또는 Datatype.</summary>
    public class TtlProperty : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _localName = "";
        private string _label = "";
        private string _comment = "";
        private TtlPropertyKind _kind = TtlPropertyKind.ObjectProperty;
        private string _domain = "";   // 클래스 LocalName
        private string _range = "";    // ObjectProperty 면 클래스 LocalName, Datatype 이면 xsd 타입

        public string LocalName
        {
            get => _localName;
            set { if (_localName != value) { _localName = value; OnChanged(nameof(LocalName)); } }
        }

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; OnChanged(nameof(Label)); } }
        }

        public string Comment
        {
            get => _comment;
            set { if (_comment != value) { _comment = value; OnChanged(nameof(Comment)); } }
        }

        public TtlPropertyKind Kind
        {
            get => _kind;
            set { if (_kind != value) { _kind = value; OnChanged(nameof(Kind)); OnChanged(nameof(KindLabel)); } }
        }

        /// <summary>UI 표시용 — 콤보박스 값 ("ObjectProperty"/"DatatypeProperty").</summary>
        public string KindLabel
        {
            get => _kind == TtlPropertyKind.ObjectProperty ? "ObjectProperty" : "DatatypeProperty";
            set
            {
                var newKind = value == "DatatypeProperty"
                    ? TtlPropertyKind.DatatypeProperty
                    : TtlPropertyKind.ObjectProperty;
                if (_kind != newKind) { _kind = newKind; OnChanged(nameof(Kind)); OnChanged(nameof(KindLabel)); }
            }
        }

        public string Domain
        {
            get => _domain;
            set { if (_domain != value) { _domain = value; OnChanged(nameof(Domain)); } }
        }

        public string Range
        {
            get => _range;
            set { if (_range != value) { _range = value; OnChanged(nameof(Range)); } }
        }

        public override string ToString() => $"{_localName} ({KindLabel})";
    }

    /// <summary>특정 클래스의 인스턴스 — DB의 자산이 아닌 사용자 정의 개체.</summary>
    public class TtlInstance : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _localName = "";
        private string _label = "";
        private string _classOf = "";

        public string LocalName
        {
            get => _localName;
            set { if (_localName != value) { _localName = value; OnChanged(nameof(LocalName)); } }
        }

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; OnChanged(nameof(Label)); } }
        }

        /// <summary>이 인스턴스가 속한 클래스 LocalName.</summary>
        public string ClassOf
        {
            get => _classOf;
            set { if (_classOf != value) { _classOf = value; OnChanged(nameof(ClassOf)); } }
        }

        public override string ToString() => string.IsNullOrEmpty(_label) ? _localName : $"{_localName} ({_label})";
    }

    /// <summary>자유 트리플 (s/p/o) — 시스템 어휘 외 추가 진술.</summary>
    public class TtlTriple : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _subject = "";
        private string _predicate = "";
        private string _objectValue = "";
        private bool   _objectIsLiteral = false;

        public string Subject
        {
            get => _subject;
            set { if (_subject != value) { _subject = value; OnChanged(nameof(Subject)); } }
        }

        public string Predicate
        {
            get => _predicate;
            set { if (_predicate != value) { _predicate = value; OnChanged(nameof(Predicate)); } }
        }

        public string ObjectValue
        {
            get => _objectValue;
            set { if (_objectValue != value) { _objectValue = value; OnChanged(nameof(ObjectValue)); } }
        }

        public bool ObjectIsLiteral
        {
            get => _objectIsLiteral;
            set { if (_objectIsLiteral != value) { _objectIsLiteral = value; OnChanged(nameof(ObjectIsLiteral)); } }
        }

        public override string ToString() => $"{_subject} {_predicate} {_objectValue}";
    }

    /// <summary>4가지 컬렉션 + 네임스페이스 prefix 의 컨테이너.</summary>
    public class TtlOntology
    {
        /// <summary>기본 네임스페이스 (예: "http://spilab.ai/ontology#").</summary>
        public string BaseUri { get; set; } = "http://spilab.ai/ontology#";

        /// <summary>기본 prefix (예: "spilab"). prefix : LocalName 으로 IRI 표시.</summary>
        public string BasePrefix { get; set; } = "spilab";

        public ObservableCollection<TtlClass>    Classes    { get; } = new();
        public ObservableCollection<TtlProperty> Properties { get; } = new();
        public ObservableCollection<TtlInstance> Instances  { get; } = new();
        public ObservableCollection<TtlTriple>   Triples    { get; } = new();

        /// <summary>현재 메타 정보 표시용.</summary>
        public string Summary =>
            $"클래스 {Classes.Count} · 속성 {Properties.Count} · 인스턴스 {Instances.Count} · 트리플 {Triples.Count}";

        /// <summary>모든 컬렉션 비우기.</summary>
        public void Clear()
        {
            Classes.Clear();
            Properties.Clear();
            Instances.Clear();
            Triples.Clear();
        }
    }
}
