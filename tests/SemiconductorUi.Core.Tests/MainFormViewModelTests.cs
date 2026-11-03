using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi;
using SemiconductorUi.Controls;
using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Tests
{
    [TestClass]
    public class MainFormViewModelTests
    {
        [TestMethod]
        public void ViewModel_ImplementsINotifyPropertyChanged()
        {
            // Arrange & Act
            var viewModel = new MainFormViewModel();

            // Assert
            Assert.IsTrue(viewModel is INotifyPropertyChanged);
        }

        [TestMethod]
        public void IsLoggedIn_PropertyChanged_NotifiesSubscribers()
        {
            // Arrange
            var viewModel = new MainFormViewModel();
            bool propertyChanged = false;
            string changedPropertyName = null;

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChanged = true;
                changedPropertyName = e.PropertyName;
            };

            // Act
            viewModel.IsLoggedIn = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(nameof(MainFormViewModel.IsLoggedIn), changedPropertyName);
            Assert.IsTrue(viewModel.IsLoggedIn);
        }

        [TestMethod]
        public void CurrentUser_PropertyChanged_NotifiesSubscribers()
        {
            // Arrange
            var viewModel = new MainFormViewModel();
            bool propertyChanged = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainFormViewModel.CurrentUser))
                {
                    propertyChanged = true;
                }
            };

            // Act
            viewModel.CurrentUser = "testuser";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("testuser", viewModel.CurrentUser);
        }

        [TestMethod]
        public void CurrentProcessState_PropertyChanged_NotifiesSubscribers()
        {
            // Arrange
            var viewModel = new MainFormViewModel();
            bool propertyChanged = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainFormViewModel.CurrentProcessState))
                {
                    propertyChanged = true;
                }
            };

            // Act
            viewModel.CurrentProcessState = MainFormViewModel.ProcessState.Running;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(MainFormViewModel.ProcessState.Running, viewModel.CurrentProcessState);
        }

        [TestMethod]
        public void HasAlarm_PropertyChanged_NotifiesSubscribers()
        {
            // Arrange
            var viewModel = new MainFormViewModel();
            bool propertyChanged = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainFormViewModel.HasAlarm))
                {
                    propertyChanged = true;
                }
            };

            // Act
            viewModel.HasAlarm = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.HasAlarm);
        }

        [TestMethod]
        public void PropertyChanged_SameValue_DoesNotNotify()
        {
            // Arrange
            var viewModel = new MainFormViewModel();
            viewModel.IsLoggedIn = true;
            bool propertyChanged = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChanged = true;
            };

            // Act
            viewModel.IsLoggedIn = true; // 같은 값 설정

            // Assert
            Assert.IsFalse(propertyChanged);
        }

        [TestMethod]
        public void FoupProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var viewModel = new MainFormViewModel();

            // Act
            viewModel.CurrentFoupACount = 5;
            viewModel.CurrentFoupBCount = 3;
            viewModel.IsFoupAMounted = true;
            viewModel.IsFoupBMounted = false;

            // Assert
            Assert.AreEqual(5, viewModel.CurrentFoupACount);
            Assert.AreEqual(3, viewModel.CurrentFoupBCount);
            Assert.IsTrue(viewModel.IsFoupAMounted);
            Assert.IsFalse(viewModel.IsFoupBMounted);
        }

        [TestMethod]
        public void StatusTextProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var viewModel = new MainFormViewModel();

            // Act
            viewModel.StatusProcessText = "Running";
            viewModel.StatusProcessDetail = "Processing wafer 1";
            viewModel.StatusPressureText = "760 Torr";
            viewModel.StatusTemperatureText = "23.0°C";
            viewModel.StatusDoorText = "Closed";

            // Assert
            Assert.AreEqual("Running", viewModel.StatusProcessText);
            Assert.AreEqual("Processing wafer 1", viewModel.StatusProcessDetail);
            Assert.AreEqual("760 Torr", viewModel.StatusPressureText);
            Assert.AreEqual("23.0°C", viewModel.StatusTemperatureText);
            Assert.AreEqual("Closed", viewModel.StatusDoorText);
        }

        [TestMethod]
        public void ChamberCompletedCount_CanBeSetAndRetrieved()
        {
            // Arrange
            var viewModel = new MainFormViewModel();

            // Act
            viewModel.ChamberCompletedCountA = 10;
            viewModel.ChamberCompletedCountB = 20;
            viewModel.ChamberCompletedCountC = 30;

            // Assert
            Assert.AreEqual(10, viewModel.ChamberCompletedCountA);
            Assert.AreEqual(20, viewModel.ChamberCompletedCountB);
            Assert.AreEqual(30, viewModel.ChamberCompletedCountC);
        }

        [TestMethod]
        public void WaferLoadState_CanBeSetAndRetrieved()
        {
            // Arrange
            var viewModel = new MainFormViewModel();

            // Act
            viewModel.WaferLoadState = MainFormViewModel.WaferLoadStateType.Loading;

            // Assert
            Assert.AreEqual(MainFormViewModel.WaferLoadStateType.Loading, viewModel.WaferLoadState);
        }

        [TestMethod]
        public void TmProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var viewModel = new MainFormViewModel();

            // Act
            viewModel.TmVisualTarget = EquipmentRegion.ChamberA;
            viewModel.TmCarryingVisual = true;
            viewModel.TmCurrentPosition = EquipmentRegion.TM;
            viewModel.TmBladeExtensionFactor = 1.5f;

            // Assert
            Assert.AreEqual(EquipmentRegion.ChamberA, viewModel.TmVisualTarget);
            Assert.IsTrue(viewModel.TmCarryingVisual);
            Assert.AreEqual(EquipmentRegion.TM, viewModel.TmCurrentPosition);
            Assert.AreEqual(1.5f, viewModel.TmBladeExtensionFactor);
        }
    }
}

