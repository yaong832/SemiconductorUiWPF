using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using SemiconductorUi.Models;
using SemiconductorUi;

namespace SemiconductorUi.Repositories
{
	using IUserRepository = SemiconductorUi.IUserRepository;

	/// <summary>
	/// 사용자 데이터 저장소 구현 클래스
	/// Phase 4.1: Repository 패턴 개선 - 인터페이스 구현, 트랜잭션 처리, 동시성 제어
	/// </summary>
	public class UserRepositoryImpl : IUserRepository
	{
		private readonly string _filePath;
		private static readonly object _lockObject = new object();

		public UserRepositoryImpl(string filePath = null)
		{
			_filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Users.xml");
		}

		public List<User> LoadAll()
		{
			lock (_lockObject)
			{
				try
				{
					if (!File.Exists(_filePath))
					{
						return EnsureSeedDefaults();
					}
				using (var fs = File.OpenRead(_filePath))
				{
					var ser = new XmlSerializer(typeof(UserList));
					var userList = (UserList)ser.Deserialize(fs);
						return userList?.Users ?? EnsureSeedDefaults();
				}
				}
			catch (Exception ex)
			{
				// 파일 로드 오류 시 기본 사용자 데이터 반환
				// 실제 장비 구동에 영향을 주지 않도록 기존 동작 유지
				ExceptionHandler.HandleException(ex, "UserRepository.LoadAll");
				return EnsureSeedDefaults();
			}
			}
		}

		public void SaveAll(List<User> users)
		{
			lock (_lockObject)
			{
				try
				{
					var dir = Path.GetDirectoryName(_filePath);
					if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

					var userList = new UserList { Users = users ?? new List<User>() };
					var tempFilePath = _filePath + ".tmp";
					using (var fs = File.Create(tempFilePath))
					{
						var ser = new XmlSerializer(typeof(UserList));
						ser.Serialize(fs, userList);
					}

					if (File.Exists(_filePath))
					{
						var backupPath = _filePath + ".bak";
						File.Copy(_filePath, backupPath, true);
						File.Delete(_filePath);
					}

					File.Move(tempFilePath, _filePath);

					var backupPathToDelete = _filePath + ".bak";
					if (File.Exists(backupPathToDelete))
					{
						try { File.Delete(backupPathToDelete); } catch { }
					}
				}
				catch (Exception ex)
				{
					ExceptionHandler.HandleException(ex, "UserRepository.SaveAll");
				}
			}
		}

		public List<User> EnsureSeedDefaults()
		{
			var defaults = new List<User>
			{
				new User
				{
					Username = "admin",
					Password = PasswordHelper.HashPassword("admin123"),
					Role = "관리자",
					CreatedAt = DateTime.Now
				},
				new User
				{
					Username = "operator",
					Password = PasswordHelper.HashPassword("operator123"),
					Role = "작업자",
					CreatedAt = DateTime.Now
				}
			};
			SaveAll(defaults);
			return defaults;
		}

		public User FindByUsername(string username)
		{
			var users = LoadAll();
			return users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// 사용자 인증 (비밀번호 해시 검증)
		/// 기존 평문 비밀번호와의 호환성 유지 (자동 마이그레이션)
		/// 실제 장비 구동에 영향을 주지 않도록 기존 동작과 100% 호환
		/// </summary>
		/// <param name="username">사용자명</param>
		/// <param name="password">평문 비밀번호</param>
		/// <returns>인증 성공 시 true</returns>
		public bool ValidateUser(string username, string password)
		{
			var user = FindByUsername(username);
			if (user == null)
			{
				return false;
			}

			// 1단계: 해시된 비밀번호인지 확인하고 검증
			if (PasswordHelper.IsHashedPassword(user.Password))
			{
				// 해시된 비밀번호와 비교
				try
				{
					if (PasswordHelper.VerifyPassword(password, user.Password))
					{
						return true;
					}
				}
				catch
				{
					// PasswordHelper 오류 시 기존 평문 비교로 폴백
				}
			}

			// 2단계: 평문 비밀번호 비교 (기존 동작 호환성 유지)
			if (user.Password == password)
			{
				// 평문 비밀번호를 자동으로 해시로 마이그레이션 (다음 로그인부터 해시 사용)
				try
				{
					var hashedPassword = PasswordHelper.HashPassword(password);
					user.Password = hashedPassword;
					
					// 사용자 목록 업데이트
					var users = LoadAll();
					var index = users.FindIndex(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
					if (index >= 0)
					{
						users[index] = user;
						SaveAll(users);
					}
				}
				catch
				{
					// 마이그레이션 실패해도 로그인은 성공 (기존 동작 유지)
				}
				
				return true;
			}

			// 3단계: PasswordHelper를 통한 검증 시도 (추가 호환성)
			try
			{
				if (PasswordHelper.VerifyPassword(password, user.Password))
				{
					// 해시 검증 성공 시 해시로 저장 (마이그레이션)
					if (!PasswordHelper.IsHashedPassword(user.Password))
					{
						var hashedPassword = PasswordHelper.HashPassword(password);
						user.Password = hashedPassword;
						
						var users = LoadAll();
						var index = users.FindIndex(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
						if (index >= 0)
						{
							users[index] = user;
							SaveAll(users);
						}
					}
					return true;
				}
			}
			catch
			{
				// PasswordHelper 오류 시 무시 (기존 동작 유지)
			}

			return false;
		}
	}

	/// <summary>
	/// 사용자 데이터 저장소 (하위 호환성을 위한 static 래퍼)
	/// Phase 4.1: 내부적으로 UserRepositoryImpl 인스턴스 사용
	/// </summary>
	public static class UserRepository
	{
		private static readonly IUserRepository _instance = new UserRepositoryImpl();
		private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Users.xml");

		/// <summary>
		/// 모든 사용자 로드
		/// </summary>
		public static List<User> LoadAll()
		{
			return _instance.LoadAll();
		}

		/// <summary>
		/// 모든 사용자 저장
		/// </summary>
		public static void SaveAll(List<User> users)
		{
			_instance.SaveAll(users);
		}

		/// <summary>
		/// 기본 사용자 데이터 생성
		/// </summary>
		public static List<User> EnsureSeedDefaults()
		{
			return _instance.EnsureSeedDefaults();
		}

		/// <summary>
		/// 사용자명으로 사용자 찾기
		/// </summary>
		public static User FindByUsername(string username)
		{
			return _instance.FindByUsername(username);
		}

		/// <summary>
		/// 사용자 인증 (비밀번호 해시 검증)
		/// 기존 평문 비밀번호와의 호환성 유지 (자동 마이그레이션)
		/// </summary>
		/// <param name="username">사용자명</param>
		/// <param name="password">평문 비밀번호</param>
		/// <returns>인증 성공 시 true</returns>
		public static bool ValidateUser(string username, string password)
		{
			return _instance.ValidateUser(username, password);
		}
	}
}

