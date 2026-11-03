using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class PasswordHelperTests
    {
        [TestMethod]
        public void HashPassword_ValidPassword_ReturnsBase64Hash()
        {
            // Arrange
            string password = "test123";

            // Act
            string hash = PasswordHelper.HashPassword(password);

            // Assert
            Assert.IsNotNull(hash);
            Assert.IsTrue(hash.Length == 44); // SHA-256 Base64 해시는 44자
            Assert.IsTrue(PasswordHelper.IsHashedPassword(hash));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void HashPassword_NullPassword_ThrowsArgumentException()
        {
            // Arrange
            string password = null;

            // Act
            PasswordHelper.HashPassword(password);

            // Assert - ExpectedException으로 처리
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void HashPassword_EmptyPassword_ThrowsArgumentException()
        {
            // Arrange
            string password = string.Empty;

            // Act
            PasswordHelper.HashPassword(password);

            // Assert - ExpectedException으로 처리
        }

        [TestMethod]
        public void HashPassword_SamePassword_ReturnsSameHash()
        {
            // Arrange
            string password = "test123";

            // Act
            string hash1 = PasswordHelper.HashPassword(password);
            string hash2 = PasswordHelper.HashPassword(password);

            // Assert
            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void HashPassword_DifferentPasswords_ReturnsDifferentHashes()
        {
            // Arrange
            string password1 = "test123";
            string password2 = "test456";

            // Act
            string hash1 = PasswordHelper.HashPassword(password1);
            string hash2 = PasswordHelper.HashPassword(password2);

            // Assert
            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void VerifyPassword_ValidPassword_ReturnsTrue()
        {
            // Arrange
            string plainPassword = "test123";
            string hashedPassword = PasswordHelper.HashPassword(plainPassword);

            // Act
            bool result = PasswordHelper.VerifyPassword(plainPassword, hashedPassword);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void VerifyPassword_InvalidPassword_ReturnsFalse()
        {
            // Arrange
            string plainPassword = "test123";
            string wrongPassword = "wrong123";
            string hashedPassword = PasswordHelper.HashPassword(plainPassword);

            // Act
            bool result = PasswordHelper.VerifyPassword(wrongPassword, hashedPassword);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void VerifyPassword_NullParameters_ReturnsFalse()
        {
            // Arrange
            string plainPassword = null;
            string hashedPassword = PasswordHelper.HashPassword("test123");

            // Act
            bool result = PasswordHelper.VerifyPassword(plainPassword, hashedPassword);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void VerifyPassword_EmptyParameters_ReturnsFalse()
        {
            // Arrange
            string plainPassword = string.Empty;
            string hashedPassword = string.Empty;

            // Act
            bool result = PasswordHelper.VerifyPassword(plainPassword, hashedPassword);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void VerifyPassword_PlainTextPassword_ReturnsTrue()
        {
            // Arrange
            string plainPassword = "test123";
            string storedPassword = "test123"; // 평문 비밀번호 (마이그레이션 호환성)

            // Act
            bool result = PasswordHelper.VerifyPassword(plainPassword, storedPassword);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsHashedPassword_ValidHash_ReturnsTrue()
        {
            // Arrange
            string password = "test123";
            string hash = PasswordHelper.HashPassword(password);

            // Act
            bool result = PasswordHelper.IsHashedPassword(hash);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsHashedPassword_PlainText_ReturnsFalse()
        {
            // Arrange
            string plainPassword = "test123";

            // Act
            bool result = PasswordHelper.IsHashedPassword(plainPassword);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsHashedPassword_Null_ReturnsFalse()
        {
            // Arrange
            string password = null;

            // Act
            bool result = PasswordHelper.IsHashedPassword(password);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void MigratePlainPassword_ValidPassword_ReturnsHash()
        {
            // Arrange
            string plainPassword = "test123";

            // Act
            string hash = PasswordHelper.MigratePlainPassword(plainPassword);

            // Assert
            Assert.IsNotNull(hash);
            Assert.IsTrue(hash.Length == 44);
            Assert.IsTrue(PasswordHelper.IsHashedPassword(hash));
            Assert.IsTrue(PasswordHelper.VerifyPassword(plainPassword, hash));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MigratePlainPassword_NullPassword_ThrowsArgumentException()
        {
            // Arrange
            string plainPassword = null;

            // Act
            PasswordHelper.MigratePlainPassword(plainPassword);

            // Assert - ExpectedException으로 처리
        }
    }
}

