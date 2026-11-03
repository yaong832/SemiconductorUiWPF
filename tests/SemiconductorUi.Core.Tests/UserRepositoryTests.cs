using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// UserRepository 단위 테스트
    /// Phase 3.3 추가: Repository 클래스 테스트
    /// </summary>
    [TestClass]
    public class UserRepositoryTests
    {
        private string _testFilePath;
        private UserRepositoryImpl _repository;

        [TestInitialize]
        public void Setup()
        {
            // 테스트용 임시 파일 경로 생성
            _testFilePath = Path.Combine(Path.GetTempPath(), $"TestUsers_{Guid.NewGuid()}.xml");
            _repository = new UserRepositoryImpl(_testFilePath);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // 테스트 파일 정리
            try
            {
                if (File.Exists(_testFilePath))
                {
                    File.Delete(_testFilePath);
                }
                if (File.Exists(_testFilePath + ".tmp"))
                {
                    File.Delete(_testFilePath + ".tmp");
                }
                if (File.Exists(_testFilePath + ".bak"))
                {
                    File.Delete(_testFilePath + ".bak");
                }
            }
            catch
            {
                // 정리 실패는 무시
            }
        }

        [TestMethod]
        public void LoadAll_FileNotExists_ReturnsDefaultUsers()
        {
            // Arrange & Act
            var users = _repository.LoadAll();

            // Assert
            Assert.IsNotNull(users);
            Assert.IsTrue(users.Count >= 2); // admin, operator 기본 사용자
            Assert.IsTrue(users.Any(u => u.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(users.Any(u => u.Username.Equals("operator", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void SaveAll_ValidUsers_SavesToFile()
        {
            // Arrange
            var users = new List<User>
            {
                new User { Username = "testuser", Password = "hashedpass", Role = "작업자", CreatedAt = DateTime.Now }
            };

            // Act
            _repository.SaveAll(users);

            // Assert
            Assert.IsTrue(File.Exists(_testFilePath));
            var loadedUsers = _repository.LoadAll();
            Assert.AreEqual(1, loadedUsers.Count);
            Assert.AreEqual("testuser", loadedUsers[0].Username);
        }

        [TestMethod]
        public void SaveAll_EmptyList_SavesEmptyList()
        {
            // Arrange
            var emptyList = new List<User>();

            // Act
            _repository.SaveAll(emptyList);

            // Assert
            var loadedUsers = _repository.LoadAll();
            Assert.IsNotNull(loadedUsers);
            Assert.AreEqual(0, loadedUsers.Count);
        }

        [TestMethod]
        public void SaveAll_NullList_SavesEmptyList()
        {
            // Arrange & Act
            _repository.SaveAll(null);

            // Assert
            var loadedUsers = _repository.LoadAll();
            Assert.IsNotNull(loadedUsers);
            Assert.AreEqual(0, loadedUsers.Count);
        }

        [TestMethod]
        public void FindByUsername_UserExists_ReturnsUser()
        {
            // Arrange
            var users = new List<User>
            {
                new User { Username = "testuser", Password = "pass", Role = "작업자", CreatedAt = DateTime.Now }
            };
            _repository.SaveAll(users);

            // Act
            var found = _repository.FindByUsername("testuser");

            // Assert
            Assert.IsNotNull(found);
            Assert.AreEqual("testuser", found.Username);
        }

        [TestMethod]
        public void FindByUsername_UserNotExists_ReturnsNull()
        {
            // Arrange
            var users = new List<User>
            {
                new User { Username = "testuser", Password = "pass", Role = "작업자", CreatedAt = DateTime.Now }
            };
            _repository.SaveAll(users);

            // Act
            var found = _repository.FindByUsername("nonexistent");

            // Assert
            Assert.IsNull(found);
        }

        [TestMethod]
        public void FindByUsername_CaseInsensitive_ReturnsUser()
        {
            // Arrange
            var users = new List<User>
            {
                new User { Username = "TestUser", Password = "pass", Role = "작업자", CreatedAt = DateTime.Now }
            };
            _repository.SaveAll(users);

            // Act
            var found1 = _repository.FindByUsername("testuser");
            var found2 = _repository.FindByUsername("TESTUSER");

            // Assert
            Assert.IsNotNull(found1);
            Assert.IsNotNull(found2);
            Assert.AreEqual("TestUser", found1.Username);
            Assert.AreEqual("TestUser", found2.Username);
        }

        [TestMethod]
        public void ValidateUser_ValidCredentials_ReturnsTrue()
        {
            // Arrange
            var password = "test123";
            var hashedPassword = PasswordHelper.HashPassword(password);
            var users = new List<User>
            {
                new User { Username = "testuser", Password = hashedPassword, Role = "작업자", CreatedAt = DateTime.Now }
            };
            _repository.SaveAll(users);

            // Act
            var isValid = _repository.ValidateUser("testuser", password);

            // Assert
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void ValidateUser_InvalidPassword_ReturnsFalse()
        {
            // Arrange
            var password = "test123";
            var hashedPassword = PasswordHelper.HashPassword(password);
            var users = new List<User>
            {
                new User { Username = "testuser", Password = hashedPassword, Role = "작업자", CreatedAt = DateTime.Now }
            };
            _repository.SaveAll(users);

            // Act
            var isValid = _repository.ValidateUser("testuser", "wrongpassword");

            // Assert
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void ValidateUser_UserNotExists_ReturnsFalse()
        {
            // Arrange & Act
            var isValid = _repository.ValidateUser("nonexistent", "password");

            // Assert
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void EnsureSeedDefaults_CreatesDefaultUsers()
        {
            // Act
            var defaults = _repository.EnsureSeedDefaults();

            // Assert
            Assert.IsNotNull(defaults);
            Assert.IsTrue(defaults.Count >= 2);
            Assert.IsTrue(defaults.Any(u => u.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(defaults.Any(u => u.Username.Equals("operator", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(File.Exists(_testFilePath));
        }

        [TestMethod]
        public void SaveAndLoad_RoundTrip_DataPreserved()
        {
            // Arrange
            var originalUsers = new List<User>
            {
                new User { Username = "user1", Password = "pass1", Role = "관리자", CreatedAt = DateTime.Now.AddDays(-1) },
                new User { Username = "user2", Password = "pass2", Role = "작업자", CreatedAt = DateTime.Now }
            };

            // Act
            _repository.SaveAll(originalUsers);
            var loadedUsers = _repository.LoadAll();

            // Assert
            Assert.AreEqual(originalUsers.Count, loadedUsers.Count);
            for (int i = 0; i < originalUsers.Count; i++)
            {
                Assert.AreEqual(originalUsers[i].Username, loadedUsers[i].Username);
                Assert.AreEqual(originalUsers[i].Password, loadedUsers[i].Password);
                Assert.AreEqual(originalUsers[i].Role, loadedUsers[i].Role);
            }
        }
    }
}

