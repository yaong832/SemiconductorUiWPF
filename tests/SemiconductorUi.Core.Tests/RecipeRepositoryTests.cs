using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;
using SemiconductorUi.Models;
using SemiconductorUi.Repositories;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// RecipeRepository 단위 테스트
    /// Phase 3.3 추가: Repository 클래스 테스트
    /// </summary>
    [TestClass]
    public class RecipeRepositoryTests
    {
        private string _testFilePath;
        private RecipeRepositoryImpl _repository;

        [TestInitialize]
        public void Setup()
        {
            // 테스트용 임시 파일 경로 생성
            _testFilePath = Path.Combine(Path.GetTempPath(), $"TestRecipes_{Guid.NewGuid()}.xml");
            _repository = new RecipeRepositoryImpl(_testFilePath);
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
        public void LoadAll_FileNotExists_ReturnsDefaultRecipes()
        {
            // Arrange & Act
            var recipes = _repository.LoadAll();

            // Assert
            Assert.IsNotNull(recipes);
            Assert.IsTrue(recipes.Count > 0); // 기본 레시피가 있어야 함
            Assert.IsTrue(File.Exists(_testFilePath)); // 파일이 생성되어야 함
        }

        [TestMethod]
        public void SaveAll_ValidRecipes_SavesToFile()
        {
            // Arrange
            var recipes = new List<RecipeSnapshot>
            {
                new RecipeSnapshot
                {
                    Name = "Test Recipe",
                    WaferCount = 5,
                    DurA = 10,
                    DurB = 20,
                    DurC = 30,
                    SecondExposure = false
                }
            };

            // Act
            _repository.SaveAll(recipes);

            // Assert
            Assert.IsTrue(File.Exists(_testFilePath));
            var loadedRecipes = _repository.LoadAll();
            Assert.AreEqual(1, loadedRecipes.Count);
            Assert.AreEqual("Test Recipe", loadedRecipes[0].Name);
        }

        [TestMethod]
        public void SaveAll_EmptyList_SavesEmptyList()
        {
            // Arrange
            var emptyList = new List<RecipeSnapshot>();

            // Act
            _repository.SaveAll(emptyList);

            // Assert
            var loadedRecipes = _repository.LoadAll();
            Assert.IsNotNull(loadedRecipes);
            Assert.AreEqual(0, loadedRecipes.Count);
        }

        [TestMethod]
        public void SaveAll_NullList_SavesEmptyList()
        {
            // Arrange & Act
            _repository.SaveAll(null);

            // Assert
            var loadedRecipes = _repository.LoadAll();
            Assert.IsNotNull(loadedRecipes);
            Assert.AreEqual(0, loadedRecipes.Count);
        }

        [TestMethod]
        public void EnsureSeedDefaults_CreatesDefaultRecipes()
        {
            // Act
            var defaults = _repository.EnsureSeedDefaults();

            // Assert
            Assert.IsNotNull(defaults);
            Assert.IsTrue(defaults.Count > 0);
            Assert.IsTrue(File.Exists(_testFilePath));
        }

        [TestMethod]
        public void SaveAndLoad_RoundTrip_DataPreserved()
        {
            // Arrange
            var originalRecipes = new List<RecipeSnapshot>
            {
                new RecipeSnapshot
                {
                    Name = "Recipe 1",
                    WaferCount = 3,
                    DurA = 15,
                    DurB = 25,
                    DurC = 35,
                    SecondExposure = false
                },
                new RecipeSnapshot
                {
                    Name = "Recipe 2",
                    WaferCount = 5,
                    DurA = 20,
                    DurB = 30,
                    DurC = 40,
                    SecondExposure = true
                }
            };

            // Act
            _repository.SaveAll(originalRecipes);
            var loadedRecipes = _repository.LoadAll();

            // Assert
            Assert.AreEqual(originalRecipes.Count, loadedRecipes.Count);
            for (int i = 0; i < originalRecipes.Count; i++)
            {
                Assert.AreEqual(originalRecipes[i].Name, loadedRecipes[i].Name);
                Assert.AreEqual(originalRecipes[i].WaferCount, loadedRecipes[i].WaferCount);
                Assert.AreEqual(originalRecipes[i].DurA, loadedRecipes[i].DurA);
                Assert.AreEqual(originalRecipes[i].DurB, loadedRecipes[i].DurB);
                Assert.AreEqual(originalRecipes[i].DurC, loadedRecipes[i].DurC);
                Assert.AreEqual(originalRecipes[i].SecondExposure, loadedRecipes[i].SecondExposure);
            }
        }
    }
}

