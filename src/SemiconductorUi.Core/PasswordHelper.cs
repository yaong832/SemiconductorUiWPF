using System;
using System.Security.Cryptography;
using System.Text;

namespace SemiconductorUi
{
    /// <summary>
    /// 비밀번호 해싱 및 검증 유틸리티 클래스
    /// SHA-256 해싱 알고리즘 사용
    /// </summary>
    public static class PasswordHelper
    {
        /// <summary>
        /// 비밀번호를 해시값으로 변환
        /// </summary>
        /// <param name="password">평문 비밀번호</param>
        /// <returns>해시된 비밀번호 (Base64 인코딩)</returns>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("비밀번호는 비어있을 수 없습니다.", nameof(password));
            }

            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// 평문 비밀번호와 해시된 비밀번호를 비교
        /// </summary>
        /// <param name="plainPassword">평문 비밀번호</param>
        /// <param name="hashedPassword">해시된 비밀번호</param>
        /// <returns>일치하면 true, 아니면 false</returns>
        public static bool VerifyPassword(string plainPassword, string hashedPassword)
        {
            if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            try
            {
                // 해시된 비밀번호인지 확인 (Base64 형식)
                if (IsHashedPassword(hashedPassword))
                {
                    // 해시된 비밀번호와 비교
                    var inputHash = HashPassword(plainPassword);
                    return inputHash == hashedPassword;
                }
                else
                {
                    // 기존 평문 비밀번호와의 호환성 (마이그레이션용)
                    // 평문 비교 후 자동으로 해시로 변환 권장
                    return plainPassword == hashedPassword;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 비밀번호가 이미 해시된 형식인지 확인
        /// Base64 인코딩된 해시는 특정 길이와 문자 패턴을 가짐
        /// </summary>
        /// <param name="password">확인할 비밀번호 문자열</param>
        /// <returns>해시된 형식이면 true, 평문이면 false</returns>
        public static bool IsHashedPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            // SHA-256 해시를 Base64로 인코딩하면 44자 (패딩 포함)
            // 평문 비밀번호는 보통 이보다 짧거나 다른 패턴을 가짐
            if (password.Length == 44 && IsBase64String(password))
            {
                return true;
            }

            // 더 안전한 방법: Base64 디코딩 시도
            try
            {
                Convert.FromBase64String(password);
                // Base64 디코딩 성공 시 해시로 간주 (길이 체크 추가)
                return password.Length >= 32; // 최소 해시 길이
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 문자열이 Base64 형식인지 확인
        /// </summary>
        private static bool IsBase64String(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }

            // Base64 문자셋: A-Z, a-z, 0-9, +, /, = (패딩)
            foreach (char c in s)
            {
                if (!((c >= 'A' && c <= 'Z') ||
                      (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') ||
                      c == '+' || c == '/' || c == '='))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 평문 비밀번호를 해시로 변환 (마이그레이션용)
        /// 기존 평문 비밀번호를 저장된 해시로 업그레이드할 때 사용
        /// </summary>
        /// <param name="plainPassword">평문 비밀번호</param>
        /// <returns>해시된 비밀번호</returns>
        public static string MigratePlainPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                throw new ArgumentException("비밀번호는 비어있을 수 없습니다.", nameof(plainPassword));
            }

            return HashPassword(plainPassword);
        }
    }
}

