using Microsoft.VisualStudio.TestTools.UnitTesting;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using Phase = SemiconductorUi.Controllers.TransferController.TmPhase;

namespace SemiconductorUi.Tests
{
    /// <summary>
    /// 시뮬레이션 모드의 TM 높이 추정이 하드웨어 집기/놓기 순서(하강·안착·상승)와 티칭 값을 따르는지 확인합니다.
    /// </summary>
    [TestClass]
    public class TmHeightEstimatorTests
    {
        private TmHardwareController.TmPositionSet _positions;

        [TestInitialize]
        public void Setup()
        {
            _positions = new TmHardwareController.TmPositionSet();
        }

        [TestMethod]
        public void Pickup_FromFoupLevel_GoesDescendThenLandThenRaise()
        {
            // FOUP A 3층: 안착 1,432,388 / 상승 1,627,604 / 하강 = 안착 - 30,000
            Assert.AreEqual(1432388L - 30000L, TmHeightEstimator.EstimateForPhase(Phase.MoveToPickup, EquipmentRegion.FoupA, _positions, 3));
            Assert.AreEqual(1432388L, TmHeightEstimator.EstimateForPhase(Phase.PickupExtend, EquipmentRegion.FoupA, _positions, 3));
            Assert.AreEqual(1627604L, TmHeightEstimator.EstimateForPhase(Phase.PickupRetract, EquipmentRegion.FoupA, _positions, 3));
        }

        [TestMethod]
        public void Dropoff_ToChamber_GoesRaiseThenLandThenDescend()
        {
            Assert.AreEqual(1156931L, TmHeightEstimator.EstimateForPhase(Phase.MoveToDropoff, EquipmentRegion.ChamberB, _positions, 1));
            Assert.AreEqual(806931L, TmHeightEstimator.EstimateForPhase(Phase.DropoffExtend, EquipmentRegion.ChamberB, _positions, 1));
            Assert.AreEqual(806931L - 30000L, TmHeightEstimator.EstimateForPhase(Phase.DropoffRetract, EquipmentRegion.ChamberB, _positions, 1));
        }

        [TestMethod]
        public void Idle_IsHomeHeight()
        {
            _positions.Home_Y = 1234;
            Assert.AreEqual(1234L, TmHeightEstimator.EstimateForPhase(Phase.Idle, EquipmentRegion.FoupA, _positions, 1));
        }

        [TestMethod]
        public void FoupLevel_OutOfRange_IsClampedToFirstAndFifth()
        {
            Assert.AreEqual(_positions.FoupB_LandY[0], TmHeightEstimator.GetLandHeight(EquipmentRegion.FoupB, _positions, 0));
            Assert.AreEqual(_positions.FoupB_LandY[4], TmHeightEstimator.GetLandHeight(EquipmentRegion.FoupB, _positions, 9));
        }

        [TestMethod]
        public void NotAModule_UsesHomeHeight()
        {
            _positions.Home_Y = 50;
            Assert.AreEqual(50L, TmHeightEstimator.GetDescendHeight(EquipmentRegion.TM, _positions, 1));
            Assert.AreEqual(50L, TmHeightEstimator.EstimateForPhase(Phase.MoveToPickup, EquipmentRegion.None, _positions, 1));
        }

        [TestMethod]
        public void DropoffPhases_AreRecognized()
        {
            Assert.IsTrue(TmHeightEstimator.IsDropoffPhase(Phase.MoveToDropoff));
            Assert.IsTrue(TmHeightEstimator.IsDropoffPhase(Phase.WaitDoorDropoffClose));
            Assert.IsFalse(TmHeightEstimator.IsDropoffPhase(Phase.PickupRetract));
            Assert.IsFalse(TmHeightEstimator.IsDropoffPhase(Phase.Idle));
        }
    }
}
