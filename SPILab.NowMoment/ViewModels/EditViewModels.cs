using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.ViewModels
{
    // ── CodeEditViewModel ────────────────────────────────
    public class CodeEditViewModel : BaseViewModel
    {
        public int Id { get; set; }
        private string _name = "", _repoUrl = "", _language = "Python",
                        _version = "1.0.0", _tags = "", _description = "";
        public string Name        { get => _name;        set => Set(ref _name, value); }
        public string RepoUrl     { get => _repoUrl;     set => Set(ref _repoUrl, value); }
        public string Language    { get => _language;    set => Set(ref _language, value); }
        public string Version     { get => _version;     set => Set(ref _version, value); }
        public string Tags        { get => _tags;        set => Set(ref _tags, value); }
        public string Description { get => _description; set => Set(ref _description, value); }

        public ObservableCollection<Project> Projects { get; }
        private Project? _selectedProject;
        public Project? SelectedProject { get => _selectedProject; set => Set(ref _selectedProject, value); }

        public List<string> Languages { get; } = new() { "Python", "C#", "C++", "JavaScript", "MATLAB", "Julia", "R", "기타" };

        public CodeEditViewModel(List<Project> projects, AssetCode? existing = null)
        {
            Projects = new ObservableCollection<Project>(projects);
            if (existing != null)
            {
                Id = existing.Id; Name = existing.Name; RepoUrl = existing.RepoUrl;
                Language = existing.Language; Version = existing.Version;
                Tags = existing.Tags; Description = existing.Description;
                SelectedProject = Projects.FirstOrDefault(p => p.Id == existing.ProjectId);
            }
        }

        public AssetCode ToModel() => new()
        {
            Id = Id, Name = Name, RepoUrl = RepoUrl, Language = Language,
            Version = Version, ProjectId = SelectedProject?.Id ?? 0,
            Tags = Tags, Description = Description
        };

        // ── v3.0 F-001 Step 1.5: KG 링크 패널 ─────────────────────
        public AssetKgLinkPanelViewModel? KgPanel { get; set; }
    }

    // ── ModelEditViewModel ───────────────────────────────
    public class ModelEditViewModel : BaseViewModel
    {
        public int Id { get; set; }
        private string _name = "", _framework = "PyTorch", _filePath = "",
                        _baseModel = "", _description = "";
        private double? _accuracy;
        public string Name        { get => _name;        set => Set(ref _name, value); }
        public string Framework   { get => _framework;   set => Set(ref _framework, value); }
        public double? Accuracy   { get => _accuracy;    set => Set(ref _accuracy, value); }
        public string FilePath    { get => _filePath;    set => Set(ref _filePath, value); }
        public string BaseModel   { get => _baseModel;   set => Set(ref _baseModel, value); }
        public string Description { get => _description; set => Set(ref _description, value); }

        public ObservableCollection<Project> Projects { get; }
        private Project? _selectedProject;
        public Project? SelectedProject { get => _selectedProject; set => Set(ref _selectedProject, value); }

        public List<string> Frameworks { get; } = new() { "PyTorch", "TensorFlow", "scikit-learn", "ONNX", "TFLite", "기타" };

        public ICommand BrowseFileCommand { get; }

        public ModelEditViewModel(List<Project> projects, AssetModel? existing = null)
        {
            Projects = new ObservableCollection<Project>(projects);
            BrowseFileCommand = new RelayCommand(_ =>
            {
                var dlg = new OpenFileDialog { Filter = "모델 파일|*.pt;*.pth;*.pkl;*.onnx;*.h5;*.bin|전체|*.*" };
                if (dlg.ShowDialog() == true) FilePath = dlg.FileName;
            });
            if (existing != null)
            {
                Id = existing.Id; Name = existing.Name; Framework = existing.Framework;
                Accuracy = existing.Accuracy; FilePath = existing.FilePath;
                BaseModel = existing.BaseModel; Description = existing.Description;
                SelectedProject = Projects.FirstOrDefault(p => p.Id == existing.ProjectId);
            }
        }

        public AssetModel ToModel() => new()
        {
            Id = Id, Name = Name, Framework = Framework, Accuracy = Accuracy,
            FilePath = FilePath, ProjectId = SelectedProject?.Id ?? 0,
            BaseModel = BaseModel, Description = Description
        };

        public AssetKgLinkPanelViewModel? KgPanel { get; set; }
    }

    // ── DocumentEditViewModel ────────────────────────────
    public class DocumentEditViewModel : BaseViewModel
    {
        public int Id { get; set; }
        private string _title = "", _docType = "paper", _filePath = "",
                        _version = "1.0", _summary = "";
        public string Title    { get => _title;    set => Set(ref _title, value); }
        public string DocType  { get => _docType;  set => Set(ref _docType, value); }
        public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
        public string Version  { get => _version;  set => Set(ref _version, value); }
        public string Summary  { get => _summary;  set => Set(ref _summary, value); }

        public ObservableCollection<Project> Projects { get; }
        private Project? _selectedProject;
        public Project? SelectedProject { get => _selectedProject; set => Set(ref _selectedProject, value); }

        public List<string> DocTypes { get; } = new() { "paper", "proposal", "report", "manual", "patent_doc", "기타" };
        public ICommand BrowseFileCommand { get; }

        public DocumentEditViewModel(List<Project> projects, AssetDocument? existing = null)
        {
            Projects = new ObservableCollection<Project>(projects);
            BrowseFileCommand = new RelayCommand(_ =>
            {
                var dlg = new OpenFileDialog { Filter = "문서 파일|*.pdf;*.docx;*.pptx;*.xlsx;*.md|전체|*.*" };
                if (dlg.ShowDialog() == true) FilePath = dlg.FileName;
            });
            if (existing != null)
            {
                Id = existing.Id; Title = existing.Title; DocType = existing.DocType;
                FilePath = existing.FilePath; Version = existing.Version; Summary = existing.Summary;
                SelectedProject = Projects.FirstOrDefault(p => p.Id == existing.ProjectId);
            }
        }

        public AssetDocument ToModel() => new()
        {
            Id = Id, Title = Title, DocType = DocType, FilePath = FilePath,
            ProjectId = SelectedProject?.Id ?? 0, Version = Version, Summary = Summary
        };

        public AssetKgLinkPanelViewModel? KgPanel { get; set; }
    }

    // ── PatentEditViewModel ──────────────────────────────
    public class PatentEditViewModel : BaseViewModel
    {
        public int Id { get; set; }
        private string _title = "", _applicationNo = "", _status = "applied",
                        _inventors = "", _description = "";
        private DateTime? _filingDate;
        public string Title          { get => _title;          set => Set(ref _title, value); }
        public string ApplicationNo  { get => _applicationNo;  set => Set(ref _applicationNo, value); }
        public string Status         { get => _status;         set => Set(ref _status, value); }
        public DateTime? FilingDate  { get => _filingDate;     set => Set(ref _filingDate, value); }
        public string Inventors      { get => _inventors;      set => Set(ref _inventors, value); }
        public string Description    { get => _description;    set => Set(ref _description, value); }

        public List<string> Statuses { get; } = new() { "applied", "registered", "pending", "rejected" };

        public PatentEditViewModel(AssetPatent? existing = null)
        {
            if (existing != null)
            {
                Id = existing.Id; Title = existing.Title;
                ApplicationNo = existing.ApplicationNo; Status = existing.Status;
                FilingDate = existing.FilingDate; Inventors = existing.Inventors;
                Description = existing.Description;
            }
        }

        public AssetPatent ToModel() => new()
        {
            Id = Id, Title = Title, ApplicationNo = ApplicationNo,
            Status = Status, FilingDate = FilingDate,
            Inventors = Inventors, Description = Description
        };

        public AssetKgLinkPanelViewModel? KgPanel { get; set; }
    }

    // ── ExperimentEditViewModel ──────────────────────────
    public class ExperimentEditViewModel : BaseViewModel
    {
        public int Id { get; set; }
        private string _name = "", _assetRef = "", _params = "{}",
                        _metrics = "{}", _resultPath = "", _status = "completed";
        public string Name        { get => _name;        set => Set(ref _name, value); }
        public string AssetRef    { get => _assetRef;    set => Set(ref _assetRef, value); }
        public string Params      { get => _params;      set => Set(ref _params, value); }
        public string Metrics     { get => _metrics;     set => Set(ref _metrics, value); }
        public string ResultPath  { get => _resultPath;  set => Set(ref _resultPath, value); }
        public string Status      { get => _status;      set => Set(ref _status, value); }

        public List<string> Statuses { get; } = new() { "completed", "running", "failed", "pending" };
        public ICommand BrowseResultCommand { get; }

        public ExperimentEditViewModel(AssetExperiment? existing = null)
        {
            BrowseResultCommand = new RelayCommand(_ =>
            {
                var dlg = new OpenFileDialog { Filter = "결과 파일|*.zip;*.json;*.csv;*.npz;*.pkl|전체|*.*" };
                if (dlg.ShowDialog() == true) ResultPath = dlg.FileName;
            });
            if (existing != null)
            {
                Id = existing.Id; Name = existing.Name; AssetRef = existing.AssetRef;
                Params = existing.Params; Metrics = existing.Metrics;
                ResultPath = existing.ResultPath; Status = existing.Status;
            }
        }

        public AssetExperiment ToModel() => new()
        {
            Id = Id, Name = Name, AssetRef = AssetRef,
            Params = Params, Metrics = Metrics,
            ResultPath = ResultPath, Status = Status
        };

        public AssetKgLinkPanelViewModel? KgPanel { get; set; }
    }

    // ── LINQ FirstOrDefault 확장 (간단 구현) ─────────────
    public static class CollectionExtensions
    {
        public static T? FirstOrDefault<T>(this System.Collections.ObjectModel.ObservableCollection<T> col,
            Func<T, bool> pred) where T : class
        {
            foreach (var item in col) if (pred(item)) return item;
            return null;
        }
    }
}
