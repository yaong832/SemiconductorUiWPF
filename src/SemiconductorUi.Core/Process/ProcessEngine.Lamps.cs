using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.ViewModels;
using ProcessState = SemiconductorUi.ViewModels.MainFormViewModel.ProcessState;

namespace SemiconductorUi.Process
{
    // 원본 Form1.cs에서 그대로 옮긴 공정 코드. UI 호출만 IProcessHost로 바꿨다.
    public sealed partial class ProcessEngine
    {


        /// <summary>원본 Form1UiUpdater.UpdateChamberProcessLamps + UpdateChamberLampUI + UpdateHardwareChamberLamp</summary>
        private void UpdateChamberProcessLamps()
        {
            UpdateChamberLamp(ChamberAState, EquipmentRegion.ChamberA);
            UpdateChamberLamp(ChamberBState, EquipmentRegion.ChamberB);
            UpdateChamberLamp(ChamberCState, EquipmentRegion.ChamberC);
        }

        private void UpdateChamberLamp(ChamberController.ChamberState chamber, EquipmentRegion region)
        {
            bool isProcessing = IsChamberProcessing(chamber);
            bool isCompleted = IsChamberCompleted(chamber);
            bool on;
            if (isProcessing)
            {
                // 공정 중: 램프 켜짐
                on = true;
                _chamberLampBlinkStates[region] = false;
                _chamberLampBlinkCounters[region] = 0;
            }
            else if (isCompleted)
            {
                // 공정 완료 (웨이퍼 있음): 깜빡임
                if (!_chamberLampBlinkCounters.ContainsKey(region))
                {
                    _chamberLampBlinkCounters[region] = 0;
                    _chamberLampBlinkStates[region] = false;
                }
                _chamberLampBlinkCounters[region]++;
                if (_chamberLampBlinkCounters[region] >= CHAMBER_LAMP_BLINK_INTERVAL)
                {
                    _chamberLampBlinkCounters[region] = 0;
                    _chamberLampBlinkStates[region] = !_chamberLampBlinkStates[region];
                }
                on = _chamberLampBlinkStates[region];
            }
            else
            {
                // 웨이퍼 없음: 램프 꺼짐
                on = false;
                _chamberLampBlinkStates[region] = false;
                _chamberLampBlinkCounters[region] = 0;
            }
            _chamberLampOn[region] = on;
            if (EthercatConnected && !isSyncingEquipmentState)
            {
                SetChamberLamp(region, on);
            }
        }

        internal bool IsChamberProcessing(ChamberController.ChamberState chamber)
        {
            if (chamber == null) return false;
            
            // 웨이퍼가 있고, 공정 시간이 남아있고, 도어가 닫혀있고, 실제 공정 진행 중이면 램프 ON
            bool hasWafer = chamber.CurrentWafer != null;
            bool hasProcessTime = chamber.TotalSeconds > 0;
            bool timeRemaining = chamber.RemainingSeconds > 0;  // 프로그레스바가 움직이는 조건
            bool doorClosed = !IsRegionDoorOpen(chamber.Region);
            
            bool isActuallyProcessing = ChamberStatusTexts.IsProcessing(chamber.StatusText);
            
            return hasWafer && hasProcessTime && timeRemaining && doorClosed && isActuallyProcessing;
        }

        internal bool IsChamberCompleted(ChamberController.ChamberState chamber)
        {
            if (chamber == null) return false;
            
            bool hasWafer = chamber.CurrentWafer != null;
            bool hasProcessTime = chamber.TotalSeconds > 0;
            bool timeRemaining = chamber.RemainingSeconds <= 0;  // 공정 시간 없음
            bool doorClosed = !IsRegionDoorOpen(chamber.Region);
            
            return hasWafer && hasProcessTime && timeRemaining && doorClosed;
        }



        internal void SetChamberLamp(EquipmentRegion region, bool on)
        {
            if (!EthercatConnected)
            {
                return;
            }

            try
            {
                if (!ChamberHardwareHelper.ControlChamberLamp(EtherCAT_M, region, on))
                {
                    AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(region)} 램프 제어 실패", "ERROR");
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(region)} 램프 제어 오류: {ex.Message}", "ERROR");
            }
        }



        internal bool IsRegionDoorOpen(EquipmentRegion region)
        {
            return ViewModel?.IsDoorOpen(region) ?? false;
        }

        internal void ResetDoorStates()
        {
            // 원본 Form1Configurator.ResetDoorStates
            ViewModel?.ResetDoorStates();
            RefreshAllDoorVisuals();
        }

        internal void RefreshAllDoorVisuals()
        {
            Host.RefreshUi();
        }

        internal void ApplyDoorVisualsForRegion(EquipmentRegion region, bool animate)
        {
            // 화면은 ViewModel.DoorOpenStates를 읽어 밸브를 그린다
            Host.RefreshUi();
        }

        internal void SetDoorState(EquipmentRegion region, bool open)
        {
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return;
            }

            // ViewModel의 현재 상태 확인
            if (ViewModel != null && ViewModel.IsDoorOpen(region) == open)
            {
                return;
            }

            // 실제 장비 제어 (EtherCAT 연결 시)
            if (EthercatConnected)
            {
                try
                {
                    if (!ChamberHardwareHelper.ControlChamberDoor(EtherCAT_M, region, open))
                    {
                        AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(region)} 도어 제어 실패", "ERROR");
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage($"{EquipmentRegionHelper.FormatRegionLabel(region)} 도어 제어 오류: {ex.Message}", "ERROR");
                }
            }

            // ViewModel 상태 업데이트
            ViewModel?.SetDoorState(region, open);
            ApplyDoorVisualsForRegion(region, animate: true);
        }

        internal void EnsureDoorOpenForRegion(EquipmentRegion region)
        {
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return;
            }

            SetDoorState(region, true);
        }

        internal void EnsureDoorClosedForRegion(EquipmentRegion region)
        {
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return;
            }

            SetDoorState(region, false);
        }

        internal bool IsWaferEnRouteToChamber(ChamberController.ChamberState chamber)
        {
            return waferTrackingService?.IsWaferEnRouteToChamber(chamber) ?? false;
        }

        internal bool IsWaferAwaitingPickup(ChamberController.ChamberState chamber)
        {
            return waferTrackingService?.IsWaferAwaitingPickup(chamber) ?? false;
        }

        private bool NeedsSecondExposure(Wafer wafer)
        {
            return waferTrackingService?.NeedsSecondExposure(wafer) ?? false;
        }

        private void MarkSecondExposureComplete(Wafer wafer)
        {
            waferTrackingService?.MarkSecondExposureComplete(wafer);
        }

        internal ArgbColor GetWaferColorForState(ChamberController.ChamberState chamber)
        {
            return waferTrackingService?.GetWaferColorForState(chamber) ?? ArgbColor.Transparent;
        }

        internal void UpdateTmVisualization()
        {
            // 원본 Form1UiUpdater.UpdateTmVisualization: 화면이 TmVisualTarget/IsWaferOnBlade를 읽어 그린다
            Host.RefreshUi();
        }

        public bool IsWaferOnBlade()
        {
            return waferTrackingService?.IsWaferOnBlade() ?? false;
        }

        public ArgbColor GetWaferColorForRegion(EquipmentRegion region)
        {
            return waferTrackingService?.GetWaferColorForRegion(region) ?? ArgbColor.FromRgb(210, 230, 255);
        }

        internal void GetFoupDisplayCounts(out int displayFoupACount, out int displayFoupBCount)
        {
            // FoupManager로 위임
            if (foupManager != null)
            {
                foupManager.GetFoupDisplayCounts(out displayFoupACount, out displayFoupBCount);
            }
            else
            {
                displayFoupACount = 0;
                displayFoupBCount = 0;
            }
        }

        internal void UpdateMainLampColors(ArgbColor? greenActive, bool yellowActive, bool redActive)
        {
            // 원본 Form1UiUpdater.UpdateMainLampColors
            MainLampGreen = greenActive;
            MainLampYellow = yellowActive;
            MainLampRed = redActive;
            if (EthercatConnected)
            {
                try
                {
                    EtherCAT_M.Digital_Output(0, redActive);
                    EtherCAT_M.Digital_Output(1, yellowActive);
                    EtherCAT_M.Digital_Output(2, greenActive.HasValue);
                }
                catch (Exception ex)
                {
                    AddLogMessage($"3색 램프 제어 오류: {ex.Message}", "ERROR");
                }
            }
            Host.RefreshUi();
        }


    }
}
