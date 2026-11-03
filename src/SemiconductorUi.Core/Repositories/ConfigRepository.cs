using System;
using System.IO;
using System.Xml.Serialization;
using SemiconductorUi.Models;
using AppSettings = SemiconductorUi.AppSettings;

namespace SemiconductorUi.Repositories
{
	using IConfigRepository = SemiconductorUi.IConfigRepository;

	/// <summary>
	/// 설정 데이터 저장소 구현 클래스
	/// Phase 4.1: Repository 패턴 개선 - 인터페이스 구현, 트랜잭션 처리, 동시성 제어
	/// </summary>
	public class ConfigRepositoryImpl : IConfigRepository
	{
		private readonly string _filePath;
		private static readonly object _lockObject = new object();

		public ConfigRepositoryImpl(string filePath = null)
		{
			_filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.xml");
		}

		public EnvThresholdSnapshot Load()
		{
			lock (_lockObject)
			{
				return ExceptionHandler.SafeFileOperation(() =>
				{
					if (!File.Exists(_filePath))
					{
						return CreateDefault();
					}
					using (var fs = File.OpenRead(_filePath))
					{
						var ser = new XmlSerializer(typeof(EnvThresholdSnapshot));
						var config = (EnvThresholdSnapshot)ser.Deserialize(fs);
						return config ?? CreateDefault();
					}
				}, CreateDefault(), "Config.xml");
			}
		}

		public void Save(EnvThresholdSnapshot config)
		{
			lock (_lockObject)
			{
				ExceptionHandler.SafeFileOperation(() =>
				{
					var dir = Path.GetDirectoryName(_filePath);
					if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

					// 트랜잭션 처리: 임시 파일에 저장 후 원자적으로 교체
					var tempFilePath = _filePath + ".tmp";
					
					using (var fs = File.Create(tempFilePath))
					{
						var ser = new XmlSerializer(typeof(EnvThresholdSnapshot));
						ser.Serialize(fs, config ?? CreateDefault());
					}

					// 원자적 교체 (파일이 존재하는 경우 백업)
					if (File.Exists(_filePath))
					{
						var backupPath = _filePath + ".bak";
						File.Copy(_filePath, backupPath, true);
					}

					// 임시 파일을 실제 파일로 교체 (.NET Framework 호환)
					if (File.Exists(_filePath))
					{
						File.Delete(_filePath);
					}
					File.Move(tempFilePath, _filePath);

					// 백업 파일 삭제 (성공 후)
					var backupPathToDelete = _filePath + ".bak";
					if (File.Exists(backupPathToDelete))
					{
						try
						{
							File.Delete(backupPathToDelete);
						}
						catch (Exception ex)
						{
							// 백업 파일 삭제 실패는 무시 (다음 저장 시 재시도)
							// 로깅만 수행 (예외는 다시 던지지 않음)
							ExceptionHandler.HandleException(ex, "ConfigRepository.Save (백업 파일 삭제)");
						}
					}
				}, "Config.xml");
			}
		}

		public EnvThresholdSnapshot CreateDefault()
		{
			// AppSettings에서 기본값 읽어옴
			return new EnvThresholdSnapshot
			{
				TempWarn = AppSettings.AlarmTempWarn,
				TempAlarm = AppSettings.AlarmTempAlarm,
				PressWarnRatio = AppSettings.AlarmPressWarnRatio,
				PressAlarmRatio = AppSettings.AlarmPressAlarmRatio,
				PressWarnAbs = AppSettings.AlarmPressWarnAbs,
				PressAlarmAbs = AppSettings.AlarmPressAlarmAbs,
				RfWarnRatio = AppSettings.AlarmRfWarnRatio,
				RfAlarmRatio = AppSettings.AlarmRfAlarmRatio,
				GasWarn = AppSettings.AlarmGasWarn,
				GasAlarm = AppSettings.AlarmGasAlarm,
				GasLeakWarn = AppSettings.AlarmGasLeakWarn,
				GasLeakAlarm = AppSettings.AlarmGasLeakAlarm
			};
		}
	}

	/// <summary>
	/// 설정 데이터 저장소 (하위 호환성을 위한 static 래퍼)
	/// Phase 4.1: 내부적으로 ConfigRepositoryImpl 인스턴스 사용
	/// </summary>
	public static class ConfigRepository
	{
		private static readonly IConfigRepository _instance = new ConfigRepositoryImpl();
		private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.xml");

		/// <summary>
		/// 설정 로드
		/// </summary>
		public static EnvThresholdSnapshot Load()
		{
			return _instance.Load();
		}

		/// <summary>
		/// 설정 저장
		/// </summary>
		public static void Save(EnvThresholdSnapshot config)
		{
			_instance.Save(config);
		}

		/// <summary>
		/// 기본 설정 생성
		/// </summary>
		private static EnvThresholdSnapshot CreateDefault()
		{
			return _instance.CreateDefault();
		}
	}
}

