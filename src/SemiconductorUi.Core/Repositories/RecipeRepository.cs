using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using SemiconductorUi.Models;

namespace SemiconductorUi.Repositories
{
	using IRecipeRepository = SemiconductorUi.IRecipeRepository;

	/// <summary>
	/// 레시피 데이터 저장소 구현 클래스
	/// Phase 4.1: Repository 패턴 개선 - 인터페이스 구현, 트랜잭션 처리, 동시성 제어
	/// </summary>
	public class RecipeRepositoryImpl : IRecipeRepository
	{
		private readonly string _filePath;
		private static readonly object _lockObject = new object();

		public RecipeRepositoryImpl(string filePath = null)
		{
			_filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes.xml");
		}

		public List<RecipeSnapshot> LoadAll()
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
						var ser = new XmlSerializer(typeof(List<RecipeSnapshot>));
						var list = (List<RecipeSnapshot>)ser.Deserialize(fs);
						return list ?? EnsureSeedDefaults();
					}
				}
				catch (Exception ex)
				{
					// 파일 로드 오류 시 기본 레시피 데이터 반환
					// 실제 장비 구동에 영향을 주지 않도록 기존 동작 유지
					ExceptionHandler.HandleException(ex, "RecipeRepository.LoadAll");
					return EnsureSeedDefaults();
				}
			}
		}

		public void SaveAll(List<RecipeSnapshot> recipes)
		{
			lock (_lockObject)
			{
				try
				{
					var dir = Path.GetDirectoryName(_filePath);
					if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

					var tempFilePath = _filePath + ".tmp";
					using (var fs = File.Create(tempFilePath))
					{
						var ser = new XmlSerializer(typeof(List<RecipeSnapshot>));
						ser.Serialize(fs, recipes ?? new List<RecipeSnapshot>());
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
					ExceptionHandler.HandleException(ex, "RecipeRepository.SaveAll");
				}
			}
		}

		public List<RecipeSnapshot> EnsureSeedDefaults()
		{
			var defaults = new List<RecipeSnapshot>();
			// 공통 베이스 환경
			RecipeSnapshot Make(string name, int wafers, int a, int b, int c, bool second, double pmaT, double pmbT, double pmcT)
			{
				return new RecipeSnapshot
				{
					Name = name,
					WaferCount = wafers,
					DurA = a, DurB = b, DurC = c,
					SecondExposure = second,
					PMA = new RecipeSnapshot.Triple { T = pmaT, P = 760.0, H = 45.0 },
					PMB = new RecipeSnapshot.Triple { T = pmbT, P = 0.005, H = 5.0 },
					PMC = new RecipeSnapshot.Triple { T = pmcT, P = 50.0, H = 3.0 },
					GasRfPMA = new RecipeSnapshot.GasRf { NF3 = 200, O2 = 200, CF4 = 200, RF = 1000 },
					GasRfPMB = new RecipeSnapshot.GasRf { NF3 = 0, O2 = 0, CF4 = 0, RF = 0 },
					GasRfPMC = new RecipeSnapshot.GasRf { NF3 = 0, O2 = 0, CF4 = 0, RF = 0 }
				};
			}
			defaults.Add(Make("PR_STD_PIPE / B(1차)+C(병렬) / 3장", 3, 18, 40, 45, false, 23.0, 22.5, 110.0));
			defaults.Add(Make("PR_HIGH_PIPE / B(1차)+C(병렬) / 5장", 5, 22, 55, 55, false, 23.0, 22.5, 110.0));
			defaults.Add(Make("PR_SINGLE_PIPE / B(1차)+C(병렬) / 1장", 1, 15, 35, 35, false, 23.0, 22.5, 105.0));
			defaults.Add(Make("PR_DOUBLE_EXPO / B(1차)+C(2차) / 5장", 5, 18, 40, 30, true, 23.0, 22.5, 110.0));
			defaults.Add(Make("PR_DOUBLE_EXPO / B(1차)+C(2차) / 3장", 3, 18, 40, 30, true, 23.0, 22.5, 110.0));
			defaults.Add(Make("PR_DOUBLE_EXPO / B(1차)+C(2차) / 1장", 1, 18, 40, 30, true, 23.0, 22.5, 110.0));
			SaveAll(defaults);
			return defaults;
		}
	}

	/// <summary>
	/// 레시피 데이터 저장소 (하위 호환성을 위한 static 래퍼)
	/// Phase 4.1: 내부적으로 RecipeRepositoryImpl 인스턴스 사용
	/// </summary>
	public static class RecipeRepository
	{
		private static readonly IRecipeRepository _instance = new RecipeRepositoryImpl();
		private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes.xml");

		/// <summary>
		/// 모든 레시피 로드
		/// </summary>
		public static List<RecipeSnapshot> LoadAll()
		{
			return _instance.LoadAll();
		}

		/// <summary>
		/// 모든 레시피 저장
		/// </summary>
		public static void SaveAll(List<RecipeSnapshot> recipes)
		{
			_instance.SaveAll(recipes);
		}

		/// <summary>
		/// 기본 레시피 데이터 생성
		/// </summary>
		public static List<RecipeSnapshot> EnsureSeedDefaults()
		{
			return _instance.EnsureSeedDefaults();
		}
	}
}

