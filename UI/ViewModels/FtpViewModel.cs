using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PinayPalBackupManager.Services;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PinayPalBackupManager.UI.ViewModels
{
    public partial class FtpViewModel : ObservableObject
    {
        private readonly BackupManager _backupManager;

        public FtpViewModel(BackupManager backupManager)
        {
            _backupManager = backupManager;
            // Initialize commands
            StartSyncCommand = new AsyncRelayCommand(StartSyncAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        }

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private int progress;

        [ObservableProperty]
        private string status = "READY";

        public IAsyncRelayCommand StartSyncCommand { get; }

        public IRelayCommand CancelCommand { get; }

        private async Task StartSyncAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            // Trigger the existing control logic via service or direct call
            _backupManager.ResetFtpTimer();
            await _backupManager.RunHealthCheckAsync(true);
            IsBusy = false;
        }

        private void Cancel()
        {
            // cancellation logic should be provided by manager/service
        }

        partial void OnIsBusyChanged(bool value)
        {
            // Notify commands that CanExecute may have changed
            (StartSyncCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (CancelCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
    }
}
