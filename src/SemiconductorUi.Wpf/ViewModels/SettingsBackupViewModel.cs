using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Services;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    public enum SettingsBackupTone { Info, Success, Danger }

    /// <summary>설정 백업 파일 작업 (기본은 SettingsBackup 정적 메서드. 스냅샷·시험에서 바꿔 끼운다)</summary>
    public interface ISettingsBackupStore
    {
        string BackupRoot { get; }
        IReadOnlyList<SettingsBackupComponent> Components { get; }
        int AutoBackupKeepCount { get; }
        List<SettingsBackupInfo> ListBackups();
        SettingsBackupInfo CreateBackup(string kind, string user, string note, DateTime now);
        PendingRestoreInfo GetPendingRestore();
        void ScheduleRestore(SettingsBackupInfo backup, IEnumerable<string> componentKeys, string user, DateTime now);
        bool CancelPendingRestore();
    }

    /// <summary>실제 파일 (원본 SettingsBackup)</summary>
    public sealed class FileSettingsBackupStore : ISettingsBackupStore
    {
        public string BackupRoot => SettingsBackup.BackupRoot;
        public IReadOnlyList<SettingsBackupComponent> Components => SettingsBackup.Components;
        public int AutoBackupKeepCount => AppSettings.AutoBackupKeepCount;
        public List<SettingsBackupInfo> ListBackups() => SettingsBackup.ListBackups();
        public SettingsBackupInfo CreateBackup(string kind, string user, string note, DateTime now) => SettingsBackup.CreateBackup(kind, user, note, now);
        public PendingRestoreInfo GetPendingRestore() => SettingsBackup.GetPendingRestore();
        public void ScheduleRestore(SettingsBackupInfo backup, IEnumerable<string> componentKeys, string user, DateTime now) =>
            SettingsBackup.ScheduleRestore(backup, componentKeys, user, now);
        public bool CancelPendingRestore() => SettingsBackup.CancelPendingRestore();
    }

    /// <summary>백업 목록 한 줄</summary>
    public sealed class SettingsBackupRow
    {
        public SettingsBackupRow(SettingsBackupInfo info, Func<string, string> componentName)
        {
            Info = info;
            TimeText = info.Time.ToString("yyyy-MM-dd HH:mm:ss");
            ComponentsText = string.Join(", ", info.Components.Select(componentName));
        }

        public SettingsBackupInfo Info { get; }
        public string TimeText { get; }
        public string Kind => Info.Kind;
        public string User => Info.User;
        public string Note => Info.Note;
        public string ComponentsText { get; }
        public bool IsManual => Info.Kind == SettingsBackup.KindManual;
        public bool IsBeforeRestore => Info.Kind == SettingsBackup.KindBeforeRestore;
    }

    /// <summary>되돌릴 항목 체크 하나</summary>
    public sealed class BackupComponentChoice : ObservableObject
    {
        private bool _isEnabled;
        private bool _isChecked;

        public BackupComponentChoice(SettingsBackupComponent component)
        {
            Key = component.Key;
            Name = component.Name;
        }

        public string Key { get; }
        public string Name { get; }

        /// <summary>고른 백업에 이 파일이 들어 있을 때만 고를 수 있다</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (!SetProperty(ref _isEnabled, value)) return;
                if (!value) IsChecked = false;
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value)) Changed?.Invoke();
            }
        }

        internal Action Changed;
    }

    /// <summary>
    /// 설정 백업·복원 (원본 Forms.SettingsBackupForm): 티칭 위치, 레시피, 알람 임계값, 사용자, 프로그램 설정 파일을 보관하고 되돌린다.
    /// 복원은 바로 하지 않고 다음에 프로그램을 켤 때 한다 (지금 쓰는 값과 파일이 어긋나지 않게).
    /// 백업은 로그인한 사용자, 복원 예약·취소는 관리자만. 확인 질문은 메시지 상자 대신 창 안에서 묻는다.
    /// </summary>
    public sealed class SettingsBackupViewModel : ObservableObject
    {
        private readonly ISettingsBackupStore _store;
        private readonly Func<bool> _isLoggedIn;
        private readonly Func<string> _currentUser;
        private readonly Func<bool> _isAdmin;
        private readonly Action<string, string> _log;
        private readonly Action<string> _openFolder;
        private readonly Func<DateTime> _clock;

        private SettingsBackupRow _selected;
        private string _memo = string.Empty;
        private string _status = string.Empty;
        private SettingsBackupTone _statusTone = SettingsBackupTone.Info;
        private PendingRestoreInfo _pending;
        private SettingsBackupInfo _confirmBackup;
        private List<string> _confirmKeys;
        private string _confirmText = string.Empty;

        public SettingsBackupViewModel(Func<bool> isLoggedIn, Func<string> currentUser, Func<bool> isAdmin,
            Action<string, string> log, Action<string> openFolder, ISettingsBackupStore store = null, Func<DateTime> clock = null)
        {
            _store = store ?? new FileSettingsBackupStore();
            _isLoggedIn = isLoggedIn ?? (() => false);
            _currentUser = currentUser ?? (() => "");
            _isAdmin = isAdmin ?? (() => false);
            _log = log;
            _openFolder = openFolder;
            _clock = clock ?? (() => DateTime.Now);

            foreach (var c in _store.Components)
            {
                var choice = new BackupComponentChoice(c) { Changed = RefreshCommands };
                Choices.Add(choice);
            }

            BackupNowCommand = new RelayCommand(BackupNow);
            RefreshCommand = new RelayCommand(RefreshAll);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => _openFolder != null);
            CancelPendingCommand = new RelayCommand(CancelPending, () => HasPending);
            RestoreCommand = new RelayCommand(AskRestore, () => CanRestore);
            ConfirmRestoreCommand = new RelayCommand(ConfirmRestore, () => IsConfirming);
            CancelConfirmCommand = new RelayCommand(() => CloseConfirm("취소했습니다."), () => IsConfirming);
        }

        public ObservableCollection<SettingsBackupRow> Backups { get; } = new ObservableCollection<SettingsBackupRow>();

        public ObservableCollection<BackupComponentChoice> Choices { get; } = new ObservableCollection<BackupComponentChoice>();

        public SettingsBackupRow SelectedBackup
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                foreach (var c in Choices) c.IsEnabled = value != null && value.Info.Components.Contains(c.Key);
                RefreshCommands();
            }
        }

        public string Memo { get => _memo; set => SetProperty(ref _memo, value ?? string.Empty); }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        public SettingsBackupTone StatusTone { get => _statusTone; private set => SetProperty(ref _statusTone, value); }

        public bool HasPending => _pending != null;

        public string PendingText => _pending == null ? string.Empty :
            $"복원 예약됨: 백업 {Path.GetFileName(_pending.BackupFolder)}의 {string.Join(", ", _pending.Components.Select(ComponentName))} " +
            $"(요청 {_pending.User}, {_pending.RequestedAt:MM-dd HH:mm}). 프로그램을 끄고 다시 켜면 적용됩니다.";

        public bool CanRestore => _selected != null && Choices.Any(c => c.IsEnabled && c.IsChecked);

        public bool IsConfirming => _confirmBackup != null;

        public string ConfirmText { get => _confirmText; private set => SetProperty(ref _confirmText, value); }

        public bool IsEmpty => Backups.Count == 0;

        public string Footnote =>
            $"자동 백업은 프로그램을 켤 때 하루 한 번 하고 최근 {_store.AutoBackupKeepCount}개만 남깁니다. 수동 백업과 복원 전 백업은 지우지 않습니다.\n" +
            $"복원하면 그 전에 지금 설정을 \"복원전\" 백업으로 먼저 남깁니다. 백업 폴더: {_store.BackupRoot}";

        public ICommand BackupNowCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CancelPendingCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand ConfirmRestoreCommand { get; }
        public ICommand CancelConfirmCommand { get; }

        /// <summary>창이 보일 때 (원본 VisibleChanged): 묻던 것을 거두고 새로 읽는다</summary>
        public void OnShown()
        {
            CloseConfirm(null);
            RefreshAll();
        }

        /// <summary>Esc: 묻는 중이면 취소하고 true</summary>
        public bool CancelIfConfirming()
        {
            if (!IsConfirming) return false;
            CloseConfirm("취소했습니다.");
            return true;
        }

        /// <summary>창을 숨길 때: 묻던 것을 거둔다</summary>
        public void OnHidden() => CloseConfirm(null);

        public void RefreshAll()
        {
            string selected = _selected?.Info.Folder;
            List<SettingsBackupInfo> list;
            try
            {
                list = _store.ListBackups();
            }
            catch (Exception ex)
            {
                list = new List<SettingsBackupInfo>();
                ShowStatus($"백업 목록을 읽지 못했습니다: {ex.Message}", SettingsBackupTone.Danger);
            }

            Backups.Clear();
            foreach (var b in list) Backups.Add(new SettingsBackupRow(b, ComponentName));
            OnPropertyChanged(nameof(IsEmpty));
            _selected = null;
            SelectedBackup = Backups.FirstOrDefault(r => r.Info.Folder == selected);
            OnPropertyChanged(nameof(SelectedBackup));
            RefreshPending();
        }

        /// <summary>항목 키를 화면 이름으로</summary>
        public string ComponentName(string key) => _store.Components.FirstOrDefault(c => c.Key == key)?.Name ?? key;

        private void RefreshPending()
        {
            try
            {
                _pending = _store.GetPendingRestore();
            }
            catch
            {
                // 읽지 못하면 예약 없음으로 보여 준다. 켤 때 다시 확인한다.
                _pending = null;
            }
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingText));
            RefreshCommands();
        }

        private void BackupNow()
        {
            CloseConfirm(null);
            if (!_isLoggedIn())
            {
                ShowStatus("백업은 로그인한 뒤에 할 수 있습니다.", SettingsBackupTone.Danger);
                return;
            }

            try
            {
                var info = _store.CreateBackup(SettingsBackup.KindManual, _currentUser(), Memo.Trim(), _clock());
                _log?.Invoke($"설정 수동 백업: {info.Folder} ({info.Components.Count}개 파일, 사용자 {info.User})", "INFO");
                Memo = string.Empty;
                RefreshAll();
                SelectedBackup = Backups.FirstOrDefault(r => r.Info.Folder == info.Folder);
                ShowStatus($"백업했습니다: {info.Name}", SettingsBackupTone.Success);
            }
            catch (Exception ex)
            {
                ShowStatus($"백업하지 못했습니다: {ex.Message}", SettingsBackupTone.Danger);
                _log?.Invoke($"설정 수동 백업 실패: {ex.Message}", "WARN");
            }
        }

        private void AskRestore()
        {
            var b = _selected?.Info;
            if (b == null) return;

            if (!_isAdmin())
            {
                ShowStatus("복원은 관리자로 로그인한 뒤에 할 수 있습니다.", SettingsBackupTone.Danger);
                return;
            }

            var keys = Choices.Where(c => c.IsEnabled && c.IsChecked).Select(c => c.Key).ToList();
            if (keys.Count == 0) return;

            _confirmBackup = b;
            _confirmKeys = keys;
            string text = $"백업 {b.Time:yyyy-MM-dd HH:mm:ss} ({b.Kind})의 {string.Join(", ", keys.Select(ComponentName))}을(를) " +
                          "다음에 프로그램을 켤 때 되돌릴까요? 지금 설정은 그때 \"복원전\" 백업으로 먼저 남깁니다.";
            if (keys.Contains(SettingsBackup.KeyTeaching))
            {
                text += "\n티칭 위치를 되돌린 뒤에는 장비를 움직이기 전에 티칭 값을 확인해 주세요.";
            }
            if (keys.Contains(SettingsBackup.KeyUsers))
            {
                text += "\n사용자를 되돌리면 비밀번호도 그 백업 때로 돌아갑니다.";
            }
            ConfirmText = text;
            OnPropertyChanged(nameof(IsConfirming));
            RefreshCommands();
        }

        private void ConfirmRestore()
        {
            var b = _confirmBackup;
            var keys = _confirmKeys;
            CloseConfirm(null);
            if (b == null || keys == null) return;

            if (!_isAdmin())
            {
                ShowStatus("복원은 관리자로 로그인한 뒤에 할 수 있습니다.", SettingsBackupTone.Danger);
                return;
            }

            try
            {
                string user = _currentUser();
                bool replaced = _store.GetPendingRestore() != null;
                _store.ScheduleRestore(b, keys, user, _clock());
                _log?.Invoke($"설정 복원 예약: {string.Join(", ", keys.Select(ComponentName))} ← 백업 {b.Name} (사용자 {user}). 다음 시작 때 적용", "WARN");
                foreach (var c in Choices) c.IsChecked = false;
                RefreshPending();
                ShowStatus(replaced
                    ? "전에 있던 예약 대신 이 복원을 예약했습니다. 프로그램을 끄고 다시 켜면 적용됩니다."
                    : "복원을 예약했습니다. 프로그램을 끄고 다시 켜면 적용됩니다.", SettingsBackupTone.Success);
            }
            catch (Exception ex)
            {
                ShowStatus($"복원을 예약하지 못했습니다: {ex.Message}", SettingsBackupTone.Danger);
            }
        }

        private void CancelPending()
        {
            CloseConfirm(null);
            if (!_isAdmin())
            {
                ShowStatus("예약 취소는 관리자로 로그인한 뒤에 할 수 있습니다.", SettingsBackupTone.Danger);
                return;
            }

            try
            {
                if (_store.CancelPendingRestore())
                {
                    _log?.Invoke($"설정 복원 예약 취소 (사용자 {_currentUser()})", "INFO");
                    ShowStatus("복원 예약을 취소했습니다.", SettingsBackupTone.Success);
                }
                RefreshPending();
            }
            catch (Exception ex)
            {
                ShowStatus($"예약을 취소하지 못했습니다: {ex.Message}", SettingsBackupTone.Danger);
            }
        }

        private void CloseConfirm(string message)
        {
            if (_confirmBackup != null || _confirmKeys != null)
            {
                _confirmBackup = null;
                _confirmKeys = null;
                OnPropertyChanged(nameof(IsConfirming));
                RefreshCommands();
            }
            if (message != null) ShowStatus(message, SettingsBackupTone.Info);
        }

        private void ShowStatus(string text, SettingsBackupTone tone)
        {
            Status = text;
            StatusTone = tone;
        }

        private void OpenFolder()
        {
            try
            {
                string folder = _selected?.Info.Folder ?? _store.BackupRoot;
                Directory.CreateDirectory(folder);
                _openFolder?.Invoke(folder);
            }
            catch (Exception ex)
            {
                ShowStatus($"폴더를 열 수 없습니다: {ex.Message}", SettingsBackupTone.Danger);
            }
        }

        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(CanRestore));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
