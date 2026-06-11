// ════════════════════════════════════════════════════════════
// MainViewModel.V4SaveContinue.cs   (v4 Phase 6)
//
// 5종 자산 편집 다이얼로그의 [저장+계속] 처리:
//   • Dialog.Tag == "continue" 이면 INSERT/UPDATE 직후 같은 종류의 신규
//     다이얼로그를 다시 열어 연속 입력을 가능케 한다.
//   • 기존 OpenAdd*/Edit* 메서드는 그대로 두고, V4 라우터(아래 5개)가
//     RelayCommand 의 신규 시점에 호출되도록 MainViewModel.cs 의
//     커맨드 바인딩만 한 줄씩 교체한다. (변경 가이드 README 참고)
// ════════════════════════════════════════════════════════════
using System.Windows;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public partial class MainViewModel
    {
        // ── 공통 유틸: 다이얼로그가 "저장+계속" 으로 닫혔는가? ─────
        private static bool IsContinue(Window dlg) =>
            dlg.Tag is string s && s == "continue";

        // ──────────────────────────────────────────────────────
        // 소스코드 / 모듈
        // ──────────────────────────────────────────────────────
        internal void OpenAddCodeV4()
        {
            while (true)
            {
                var vm = new CodeEditViewModel(_db.GetProjects());
                vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_code", 0);
                var dlg = new Views.CodeEditDialog(vm) { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true) break;

                var model = vm.ToModel();
                _db.InsertCode(model);
                var newId = _db.GetLastInsertId("asset_code");
                model.Id = (int)newId;
                Audit.LogCreate("asset_code", newId, model);
                _db.SyncAssetTags("asset_code", newId, model.Tags);
                LoadCodes(); LoadStats();

                if (!IsContinue(dlg)) break;
            }
        }

        internal void EditCodeV4(object? p)
        {
            if (p is not AssetCode a) return;
            var before = _db.GetCodeById(a.Id);
            var vm = new CodeEditViewModel(_db.GetProjects(), a);
            vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_code", a.Id);
            var dlg = new Views.CodeEditDialog(vm) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var after = vm.ToModel();
            _db.UpdateCode(after);
            _db.TouchUpdatedAt("asset_code", a.Id);
            if (before != null) Audit.LogUpdate("asset_code", a.Id, before, after);
            _db.SyncAssetTags("asset_code", a.Id, after.Tags);
            LoadCodes();

            // 수정 모드에서 [저장+계속] 누르면 같은 자산에 대해 다시 편집 다이얼로그를 연다
            if (IsContinue(dlg)) EditCodeV4(_db.GetCodeById(a.Id));
        }

        // ──────────────────────────────────────────────────────
        // AI 모델 / 학습데이터
        // ──────────────────────────────────────────────────────
        internal void OpenAddModelV4()
        {
            while (true)
            {
                var vm = new ModelEditViewModel(_db.GetProjects());
                vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_model", 0);
                var dlg = new Views.ModelEditDialog(vm) { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true) break;

                var model = vm.ToModel();
                _db.InsertModel(model);
                var newId = _db.GetLastInsertId("asset_model");
                model.Id = (int)newId;
                Audit.LogCreate("asset_model", newId, model);
                LoadModels(); LoadStats();

                if (!IsContinue(dlg)) break;
            }
        }

        internal void EditModelV4(object? p)
        {
            if (p is not AssetModel a) return;
            var before = _db.GetModelById(a.Id);
            var vm = new ModelEditViewModel(_db.GetProjects(), a);
            vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_model", a.Id);
            var dlg = new Views.ModelEditDialog(vm) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var after = vm.ToModel();
            _db.UpdateModel(after);
            _db.TouchUpdatedAt("asset_model", a.Id);
            if (before != null) Audit.LogUpdate("asset_model", a.Id, before, after);
            LoadModels();

            if (IsContinue(dlg)) EditModelV4(_db.GetModelById(a.Id));
        }

        // ──────────────────────────────────────────────────────
        // 문서 / 논문
        // ──────────────────────────────────────────────────────
        internal void OpenAddDocumentV4()
        {
            while (true)
            {
                var vm = new DocumentEditViewModel(_db.GetProjects());
                vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_document", 0);
                var dlg = new Views.DocumentEditDialog(vm) { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true) break;

                var model = vm.ToModel();
                _db.InsertDocument(model);
                var newId = _db.GetLastInsertId("asset_document");
                model.Id = (int)newId;
                Audit.LogCreate("asset_document", newId, model);
                LoadDocuments(); LoadStats();

                if (!IsContinue(dlg)) break;
            }
        }

        internal void EditDocumentV4(object? p)
        {
            if (p is not AssetDocument a) return;
            var before = _db.GetDocumentById(a.Id);
            var vm = new DocumentEditViewModel(_db.GetProjects(), a);
            vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_document", a.Id);
            var dlg = new Views.DocumentEditDialog(vm) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var after = vm.ToModel();
            _db.UpdateDocument(after);
            _db.TouchUpdatedAt("asset_document", a.Id);
            if (before != null) Audit.LogUpdate("asset_document", a.Id, before, after);
            LoadDocuments();

            if (IsContinue(dlg)) EditDocumentV4(_db.GetDocumentById(a.Id));
        }

        // ──────────────────────────────────────────────────────
        // 특허 / IP
        // ──────────────────────────────────────────────────────
        internal void OpenAddPatentV4()
        {
            while (true)
            {
                var vm = new PatentEditViewModel();
                vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_patent", 0);
                var dlg = new Views.PatentEditDialog(vm) { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true) break;

                var model = vm.ToModel();
                _db.InsertPatent(model);
                var newId = _db.GetLastInsertId("asset_patent");
                model.Id = (int)newId;
                Audit.LogCreate("asset_patent", newId, model);
                LoadPatents(); LoadStats();

                if (!IsContinue(dlg)) break;
            }
        }

        internal void EditPatentV4(object? p)
        {
            if (p is not AssetPatent a) return;
            var before = _db.GetPatentById(a.Id);
            var vm = new PatentEditViewModel(a);
            vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_patent", a.Id);
            var dlg = new Views.PatentEditDialog(vm) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var after = vm.ToModel();
            _db.UpdatePatent(after);
            _db.TouchUpdatedAt("asset_patent", a.Id);
            if (before != null) Audit.LogUpdate("asset_patent", a.Id, before, after);
            LoadPatents();

            if (IsContinue(dlg)) EditPatentV4(_db.GetPatentById(a.Id));
        }

        // ──────────────────────────────────────────────────────
        // 실험 / 측정 데이터
        // ──────────────────────────────────────────────────────
        internal void OpenAddExperimentV4()
        {
            while (true)
            {
                var vm = new ExperimentEditViewModel();
                vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_experiment", 0);
                var dlg = new Views.ExperimentEditDialog(vm) { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true) break;

                var model = vm.ToModel();
                _db.InsertExperiment(model);
                var newId = _db.GetLastInsertId("asset_experiment");
                model.Id = (int)newId;
                Audit.LogCreate("asset_experiment", newId, model);
                LoadExperiments(); LoadStats();

                if (!IsContinue(dlg)) break;
            }
        }

        internal void EditExperimentV4(object? p)
        {
            if (p is not AssetExperiment a) return;
            var before = _db.GetExperimentById(a.Id);
            var vm = new ExperimentEditViewModel(a);
            vm.KgPanel = new AssetKgLinkPanelViewModel(KgService, "asset_experiment", a.Id);
            var dlg = new Views.ExperimentEditDialog(vm) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var after = vm.ToModel();
            _db.UpdateExperiment(after);
            _db.TouchUpdatedAt("asset_experiment", a.Id);
            if (before != null) Audit.LogUpdate("asset_experiment", a.Id, before, after);
            LoadExperiments();

            if (IsContinue(dlg)) EditExperimentV4(_db.GetExperimentById(a.Id));
        }
    }
}
