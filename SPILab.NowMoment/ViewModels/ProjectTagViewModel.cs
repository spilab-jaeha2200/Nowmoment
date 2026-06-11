// ════════════════════════════════════════════════════════════════════
// ProjectTagViewModel.cs — v4 Phase 4 (SCR-A06)
//
// 좌측: 프로젝트 목록 + CRUD
// 우측: 선택 프로젝트의 자산 통계 + 상위 태그
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public class ProjectTagViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly AuditService _audit;

        public ObservableCollection<Project> Projects { get; } = new();
        public ObservableCollection<TagItem> TopTags  { get; } = new();

        private Project? _selected;
        public Project? SelectedProject
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    LoadStats();
                    OnPropertyChanged(nameof(SelectedHeader));
                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        public bool HasSelection => _selected != null;
        public string SelectedHeader => _selected == null ? "(프로젝트를 선택하세요)" : $"📂  {_selected.Name}";

        // 카드 통계
        private int _statCode, _statModel, _statDoc, _statPatent, _statExp;
        public int StatCode  { get => _statCode;   set => Set(ref _statCode, value); }
        public int StatModel { get => _statModel;  set => Set(ref _statModel, value); }
        public int StatDoc   { get => _statDoc;    set => Set(ref _statDoc, value); }
        public int StatPatent{ get => _statPatent; set => Set(ref _statPatent, value); }
        public int StatExp   { get => _statExp;    set => Set(ref _statExp, value); }
        public int StatTotal => StatCode + StatModel + StatDoc + StatPatent + StatExp;

        // 명령
        public ICommand AddCommand     { get; }
        public ICommand EditCommand    { get; }
        public ICommand DeleteCommand  { get; }
        public ICommand RefreshCommand { get; }

        public ProjectTagViewModel(DatabaseService db, AuditService audit)
        {
            _db = db; _audit = audit;
            AddCommand     = new RelayCommand(_ => DoAdd());
            EditCommand    = new RelayCommand(_ => DoEdit(),    _ => HasSelection);
            DeleteCommand  = new RelayCommand(_ => DoDelete(),  _ => HasSelection);
            RefreshCommand = new RelayCommand(_ => Reload());
            Reload();
        }

        public void Reload()
        {
            Projects.Clear();
            foreach (var p in _db.GetProjects()) Projects.Add(p);
            // 선택 유지
            if (_selected != null)
            {
                var still = Projects.FirstOrDefault(p => p.Id == _selected.Id);
                SelectedProject = still ?? Projects.FirstOrDefault();
            }
            else
            {
                SelectedProject = Projects.FirstOrDefault();
            }
        }

        private void LoadStats()
        {
            if (_selected == null)
            {
                StatCode = StatModel = StatDoc = StatPatent = StatExp = 0;
                TopTags.Clear();
                OnPropertyChanged(nameof(StatTotal));
                return;
            }
            var c = _db.GetAssetCountsByProject(_selected.Id);
            StatCode    = c.TryGetValue("code",       out var v1) ? v1 : 0;
            StatModel   = c.TryGetValue("model",      out var v2) ? v2 : 0;
            StatDoc     = c.TryGetValue("document",   out var v3) ? v3 : 0;
            StatPatent  = c.TryGetValue("patent",     out var v4) ? v4 : 0;
            StatExp     = c.TryGetValue("experiment", out var v5) ? v5 : 0;
            OnPropertyChanged(nameof(StatTotal));

            TopTags.Clear();
            foreach (var (name, cnt) in _db.GetTopTagsForProject(_selected.Id, 10))
                TopTags.Add(new TagItem { Name = name, Count = cnt });
        }

        private void DoAdd()
        {
            var p = new Project { Name = "(새 프로젝트)", Type = "internal", Status = "active" };
            _db.InsertProject(p);
            var newId = (int)_db.GetLastInsertId("project");
            p.Id = newId;
            _audit.LogCreate("project", newId, p);
            Reload();
            SelectedProject = Projects.FirstOrDefault(x => x.Id == newId);
        }

        private void DoEdit()
        {
            if (_selected == null) return;
            // 간단한 인라인 편집 — 프롬프트로 이름만 변경
            var newName = SimplePrompt("프로젝트 이름", _selected.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == _selected.Name) return;
            var before = _db.GetProjectById(_selected.Id);
            _selected.Name = newName.Trim();
            _db.UpdateProject(_selected);
            if (before != null) _audit.LogUpdate("project", _selected.Id, before, _selected);
            Reload();
        }

        private void DoDelete()
        {
            if (_selected == null) return;
            var msg = $"'{_selected.Name}' 프로젝트를 삭제합니까?\n\n" +
                      $"※ 소속 자산은 보존되며 project_id 만 NULL 로 설정됩니다.";
            if (MessageBox.Show(msg, "프로젝트 삭제", MessageBoxButton.OKCancel,
                                MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            var snap = _db.GetProjectById(_selected.Id);
            _db.DeleteProject(_selected.Id);
            _audit.LogDelete("project", _selected.Id, snap);
            Reload();
        }

        private static string? SimplePrompt(string title, string defaultValue)
        {
            var w = new Window
            {
                Title = title, Height = 170, Width = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Owner = Application.Current.MainWindow,
                FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic"),
            };
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var tb = new System.Windows.Controls.TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,6) };
            var txt = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Padding = new Thickness(8,0,8,0),
                Height = 34,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            txt.SelectAll();
            var panel = new System.Windows.Controls.StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal,
              HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
            var ok = new System.Windows.Controls.Button { Content = "확인", IsDefault = true, Padding = new Thickness(20,4,20,4), Margin = new Thickness(0,0,8,0) };
            var cancel = new System.Windows.Controls.Button { Content = "취소", IsCancel = true, Padding = new Thickness(20,4,20,4) };
            panel.Children.Add(ok); panel.Children.Add(cancel);

            System.Windows.Controls.Grid.SetRow(tb, 0);
            System.Windows.Controls.Grid.SetRow(txt, 1);
            System.Windows.Controls.Grid.SetRow(panel, 2);
            grid.Children.Add(tb); grid.Children.Add(txt); grid.Children.Add(panel);
            w.Content = grid;

            string? result = null;
            ok.Click += (_, _) => { result = txt.Text; w.DialogResult = true; w.Close(); };
            txt.Focus();
            w.ShowDialog();
            return result;
        }

        public class TagItem
        {
            public string Name { get; set; } = "";
            public int    Count { get; set; }
        }
    }
}
