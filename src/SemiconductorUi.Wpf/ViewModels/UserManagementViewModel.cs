using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Models;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 사용자 관리 화면 (원본 Forms.UserManagementForm, 관리자 전용).
    /// 원본과 다른 점: 수정할 때 비밀번호를 비워 두면 역할만 바꾸고 비밀번호는 그대로 둔다
    /// (원본 안내 문구 "바꿀 때만 입력"에 맞춤). 지금 로그인한 계정과 마지막 관리자는 지우거나 작업자로 내릴 수 없다.
    /// </summary>
    public sealed class UserManagementViewModel : ObservableObject
    {
        public const string AdminRole = "관리자";
        public const string OperatorRole = "작업자";

        private readonly IDialogService _dialogs;
        private readonly IUserRepository _repository;
        private readonly string _currentUser;
        private List<User> _users = new List<User>();
        private UserRow _selected;
        private string _username = string.Empty;
        private string _role = OperatorRole;

        /// <summary>닫기 버튼 (창이 듣는다)</summary>
        public event EventHandler CloseRequested;

        public UserManagementViewModel(IDialogService dialogs, IUserRepository repository, string currentUser)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _currentUser = currentUser;

            AddCommand = new RelayCommand(Add, () => !IsEditing);
            UpdateCommand = new RelayCommand(Update, () => IsEditing);
            DeleteCommand = new RelayCommand(Delete, () => IsEditing);
            NewCommand = new RelayCommand(() => Selected = null, () => IsEditing);
            CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            LoadUsers();
        }

        public ObservableCollection<UserRow> Users { get; } = new ObservableCollection<UserRow>();

        public IReadOnlyList<string> Roles { get; } = new[] { AdminRole, OperatorRole };

        public UserRow Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                if (value != null)
                {
                    Username = value.Username;
                    Role = value.Role;
                }
                else
                {
                    Username = string.Empty;
                    Role = OperatorRole;
                }
                // 비밀번호는 해시된 값이므로 표시하지 않음
                PasswordProvider?.Clear();
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(CanEditUsername));
                OnPropertyChanged(nameof(PasswordHint));
                RelayCommand.Refresh();
            }
        }

        /// <summary>목록에서 고른 사용자를 고치는 중인지 (아니면 새 사용자 추가)</summary>
        public bool IsEditing => _selected != null;

        /// <summary>수정할 때는 사용자명 변경 불가</summary>
        public bool CanEditUsername => !IsEditing;

        public string Username { get => _username; set => SetProperty(ref _username, value ?? string.Empty); }
        public string Role { get => _role; set => SetProperty(ref _role, value ?? OperatorRole); }

        public string PasswordHint => IsEditing
            ? "비밀번호를 바꿀 때만 입력하세요. 비워 두면 그대로 둡니다."
            : "새 사용자의 비밀번호를 입력하세요.";

        /// <summary>PasswordBox는 바인딩할 수 없어서 창이 넣어 준다.</summary>
        public IPasswordProvider PasswordProvider { get; set; }

        public ICommand AddCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand CloseCommand { get; }

        private string Password => PasswordProvider?.Password ?? string.Empty;

        private void LoadUsers(string select = null)
        {
            _users = _repository.LoadAll() ?? new List<User>();
            Users.Clear();
            foreach (var user in _users)
            {
                Users.Add(new UserRow(user, string.Equals(user.Username, _currentUser, StringComparison.OrdinalIgnoreCase)));
            }
            // 선택을 지우고 입력 칸을 비운 뒤, 이름이 주어졌으면 그 사용자를 다시 고른다
            _selected = null;
            OnPropertyChanged(nameof(Selected));
            Username = string.Empty;
            Role = OperatorRole;
            PasswordProvider?.Clear();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(CanEditUsername));
            OnPropertyChanged(nameof(PasswordHint));
            RelayCommand.Refresh();
            if (select != null) Selected = Users.FirstOrDefault(u => u.Username == select);
        }

        private int AdminCount => _users.Count(u => u.Role == AdminRole);

        private void Add()
        {
            var name = Username.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(Password))
            {
                _dialogs.Warn("사용자명과 비밀번호를 입력하세요.", "입력 오류");
                return;
            }

            if (_users.Any(u => u.Username.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.Warn("이미 존재하는 사용자명입니다.", "중복 오류");
                return;
            }

            _users.Add(new User
            {
                Username = name,
                Password = PasswordHelper.HashPassword(Password),
                Role = Role,
                CreatedAt = DateTime.Now
            });
            _repository.SaveAll(_users);
            LoadUsers();
            _dialogs.Info("사용자가 추가되었습니다.", "완료");
        }

        private void Update()
        {
            var user = _users.FirstOrDefault(u => u.Username == _selected?.Username);
            if (user == null)
            {
                _dialogs.Warn("수정할 사용자를 선택하세요.", "선택 오류");
                return;
            }

            if (user.Role == AdminRole && Role != AdminRole)
            {
                if (IsCurrentUser(user))
                {
                    _dialogs.Warn("지금 로그인한 계정은 작업자로 바꿀 수 없습니다.", "수정 불가");
                    return;
                }
                if (AdminCount <= 1)
                {
                    _dialogs.Warn("관리자가 한 명은 있어야 합니다.", "수정 불가");
                    return;
                }
            }

            bool changePassword = !string.IsNullOrWhiteSpace(Password);
            if (!changePassword && user.Role == Role)
            {
                _dialogs.Info("바뀐 내용이 없습니다.", "안내");
                return;
            }

            if (changePassword) user.Password = PasswordHelper.HashPassword(Password);
            user.Role = Role;
            _repository.SaveAll(_users);
            LoadUsers(user.Username);
            _dialogs.Info(changePassword ? "사용자 정보와 비밀번호가 수정되었습니다." : "사용자 역할이 수정되었습니다.", "완료");
        }

        private void Delete()
        {
            var user = _users.FirstOrDefault(u => u.Username == _selected?.Username);
            if (user == null)
            {
                _dialogs.Warn("삭제할 사용자를 선택하세요.", "선택 오류");
                return;
            }

            if (user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Warn("관리자 계정은 삭제할 수 없습니다.", "삭제 불가");
                return;
            }
            if (IsCurrentUser(user))
            {
                _dialogs.Warn("지금 로그인한 계정은 삭제할 수 없습니다.", "삭제 불가");
                return;
            }
            if (user.Role == AdminRole && AdminCount <= 1)
            {
                _dialogs.Warn("관리자가 한 명은 있어야 합니다.", "삭제 불가");
                return;
            }

            if (!_dialogs.Confirm($"사용자 '{user.Username}'를 삭제하시겠습니까?", "삭제 확인", true)) return;

            _users.RemoveAll(u => u.Username == user.Username);
            _repository.SaveAll(_users);
            LoadUsers();
            _dialogs.Info("사용자가 삭제되었습니다.", "완료");
        }

        private bool IsCurrentUser(User user) => string.Equals(user.Username, _currentUser, StringComparison.OrdinalIgnoreCase);

        public sealed class UserRow
        {
            public UserRow(User user, bool isCurrent)
            {
                Username = user.Username;
                Role = user.Role;
                CreatedText = user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                IsCurrent = isCurrent;
            }

            public string Username { get; }
            public string Role { get; }
            public string CreatedText { get; }
            public bool IsCurrent { get; }
            public bool IsAdmin => Role == AdminRole;
        }
    }

    /// <summary>PasswordBox 내용을 뷰모델에 넘기는 통로</summary>
    public interface IPasswordProvider
    {
        string Password { get; }
        void Clear();
    }
}
