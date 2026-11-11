using System;
using System.Collections.Generic;
using SemiconductorUi.Models;
using SemiconductorUi.Services;

namespace SemiconductorUi.Wpf.Services
{
    /// <summary>
    /// 사용자 저장 때 변경 이력을 남기는 저장소 (원본 UserManagementForm의 ChangeHistory.RecordUsers 호출).
    /// 저장 동작은 그대로이고, 기록이 실패해도 저장은 된다.
    /// </summary>
    public sealed class ChangeRecordingUserRepository : IUserRepository
    {
        private readonly IUserRepository _inner;

        public ChangeRecordingUserRepository(IUserRepository inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void SaveAll(List<User> users)
        {
            var before = ChangeHistory.TryLoad(() => _inner.LoadAll());
            _inner.SaveAll(users);
            ChangeHistory.RecordUsers(before, users);
        }

        public List<User> LoadAll() => _inner.LoadAll();
        public User FindByUsername(string username) => _inner.FindByUsername(username);
        public bool ValidateUser(string username, string password) => _inner.ValidateUser(username, password);
        public List<User> EnsureSeedDefaults() => _inner.EnsureSeedDefaults();
    }
}
