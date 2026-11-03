using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;
using SemiconductorUi.Services;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class ServiceContainerTests
    {
        [TestInitialize]
        public void Setup()
        {
            // 각 테스트 전에 ServiceContainer 초기화
            ServiceContainer.Clear();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // 각 테스트 후에 ServiceContainer 초기화
            ServiceContainer.Clear();
        }

        [TestMethod]
        public void Register_ValidService_RegistersSuccessfully()
        {
            // Arrange
            var testService = new TestService();

            // Act
            ServiceContainer.Register<ITestService>(testService);

            // Assert
            var retrieved = ServiceContainer.Get<ITestService>();
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(testService, retrieved);
        }

        [TestMethod]
        public void Get_RegisteredService_ReturnsService()
        {
            // Arrange
            var testService = new TestService();
            ServiceContainer.Register<ITestService>(testService);

            // Act
            var retrieved = ServiceContainer.Get<ITestService>();

            // Assert
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(testService, retrieved);
        }

        [TestMethod]
        public void Get_UnregisteredService_ReturnsNull()
        {
            // Act
            var retrieved = ServiceContainer.Get<ITestService>();

            // Assert
            Assert.IsNull(retrieved);
        }

        [TestMethod]
        public void Register_OverrideExistingService_ReplacesService()
        {
            // Arrange
            var service1 = new TestService { Id = 1 };
            var service2 = new TestService { Id = 2 };
            ServiceContainer.Register<ITestService>(service1);

            // Act
            ServiceContainer.Register<ITestService>(service2);

            // Assert
            var retrieved = ServiceContainer.Get<ITestService>();
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(service2, retrieved);
            Assert.AreEqual(2, ((TestService)retrieved).Id);
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentNullException))]
        public void Register_NullService_ThrowsArgumentNullException()
        {
            // Act
            ServiceContainer.Register<ITestService>(null);

            // Assert - ExpectedException으로 처리
        }

        [TestMethod]
        public void Unregister_RegisteredService_RemovesService()
        {
            // Arrange
            var testService = new TestService();
            ServiceContainer.Register<ITestService>(testService);

            // Act
            ServiceContainer.Unregister<ITestService>();

            // Assert
            var retrieved = ServiceContainer.Get<ITestService>();
            Assert.IsNull(retrieved);
        }

        [TestMethod]
        public void Unregister_UnregisteredService_DoesNotThrow()
        {
            // Act & Assert - 예외가 발생하지 않아야 함
            ServiceContainer.Unregister<ITestService>();
        }

        [TestMethod]
        public void IsRegistered_RegisteredService_ReturnsTrue()
        {
            // Arrange
            var testService = new TestService();
            ServiceContainer.Register<ITestService>(testService);

            // Act
            bool result = ServiceContainer.IsRegistered<ITestService>();

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsRegistered_UnregisteredService_ReturnsFalse()
        {
            // Act
            bool result = ServiceContainer.IsRegistered<ITestService>();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Clear_RemovesAllServices()
        {
            // Arrange
            var service1 = new TestService();
            var service2 = new TestService2();
            ServiceContainer.Register<ITestService>(service1);
            ServiceContainer.Register<ITestService2>(service2);

            // Act
            ServiceContainer.Clear();

            // Assert
            Assert.IsNull(ServiceContainer.Get<ITestService>());
            Assert.IsNull(ServiceContainer.Get<ITestService2>());
        }

        [TestMethod]
        public void Register_MultipleServices_AllRegistered()
        {
            // Arrange
            var service1 = new TestService();
            var service2 = new TestService2();

            // Act
            ServiceContainer.Register<ITestService>(service1);
            ServiceContainer.Register<ITestService2>(service2);

            // Assert
            Assert.IsNotNull(ServiceContainer.Get<ITestService>());
            Assert.IsNotNull(ServiceContainer.Get<ITestService2>());
        }
    }

    // 테스트용 인터페이스 및 클래스
    public interface ITestService
    {
    }

    public interface ITestService2
    {
    }

    public class TestService : ITestService
    {
        public int Id { get; set; }
    }

    public class TestService2 : ITestService2
    {
    }
}

