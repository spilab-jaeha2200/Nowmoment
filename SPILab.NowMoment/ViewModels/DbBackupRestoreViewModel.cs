// ════════════════════════════════════════════════════════════════════
// ViewModels/DbBackupRestoreViewModel.cs — DB 백업/복원 화면 VM
//
// 백업: v3 MainViewModel.BackupDbCommand 를 그대로 호출 (SaveFileDialog +
//       BackupService + 결과 MessageBox 까지 v3 가 처리).
// 복원: 미구현 — UI 는 표시하되 버튼은 IsEnabled=False (XAML 에서 처리).
//       ViewModel 에는 복원 관련 명령을 두지 않는다.
// ════════════════════════════════════════════════════════════════════
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SPILab.NowMoment.ViewModels
{
    public class DbBackupRestoreViewModel : INotifyPropertyChanged
    {
        public DbBackupRestoreViewModel(Action onBackup)
        {
            BackupCommand = new SimpleCommand(_ =>
            {
                try
                {
                    onBackup?.Invoke();
                    BackupResult = $"✅ 백업 실행됨  ({DateTime.Now:HH:mm:ss})";
                    IsBackupError = false;
                }
                catch (Exception ex)
                {
                    BackupResult = "❌ 백업 실패: " + ex.Message;
                    IsBackupError = true;
                }
            });
        }

        // ── 백업 ──
        public ICommand BackupCommand { get; }

        private string _backupResult = "";
        public string BackupResult
        {
            get => _backupResult;
            private set
            {
                if (_backupResult != value)
                {
                    _backupResult = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasBackupResult));
                }
            }
        }

        public bool HasBackupResult => !string.IsNullOrEmpty(_backupResult);

        private bool _isBackupError;
        public bool IsBackupError
        {
            get => _isBackupError;
            private set { if (_isBackupError != value) { _isBackupError = value; OnPropertyChanged(); } }
        }

        // ── 복원 ──
        // 복원 기능은 아직 미구현. 화면의 복원 버튼은 XAML 에서 IsEnabled=False 로
        // 비활성 처리되어 있으며, 명령 바인딩이 없다.

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
