// ════════════════════════════════════════════════════════════════════
// MainViewModel.V4Settings.cs — v4 Phase 3
//   - DoOpenSettings  : 사용자 설정 다이얼로그 열기
//   - DoExportExcel   : 자산을 xlsx 로 내보내기 (kind=null이면 전체)
// ════════════════════════════════════════════════════════════════════
using System;
using System.Windows;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public partial class MainViewModel
    {
        private void DoOpenSettings()
        {
            try
            {
                var vm = new SettingsViewModel(_db, Audit);
                var dlg = new Views.SettingsDialog(vm)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 화면 표시 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DoExportExcel(string? kind)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "Excel 내보내기 — 저장 위치 선택",
                Filter   = "Excel 통합 문서 (*.xlsx)|*.xlsx|모든 파일 (*.*)|*.*",
                FileName = string.IsNullOrEmpty(kind)
                    ? $"NowMoment_All_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    : $"NowMoment_{kind}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                AddExtension = true,
                DefaultExt   = "xlsx",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                Application.Current.MainWindow.Cursor = System.Windows.Input.Cursors.Wait;
                var svc = new ExcelExportService(_db);
                if (string.IsNullOrEmpty(kind))
                    svc.ExportAll(dlg.FileName);
                else
                    svc.ExportSingle(kind, dlg.FileName);

                Audit.LogAction("export", "system", null, new {
                    format = "xlsx",
                    kind = kind ?? "all",
                    file = System.IO.Path.GetFileName(dlg.FileName),
                });

                MessageBox.Show(
                    $"✅ Excel 내보내기 완료\n\n파일: {System.IO.Path.GetFileName(dlg.FileName)}",
                    "Excel 내보내기",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Excel 내보내기 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Application.Current.MainWindow.Cursor = null;
            }
        }
    }
}
