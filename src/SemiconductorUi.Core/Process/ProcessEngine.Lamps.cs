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
                _simLampBlinkStartMs.Remove(region);
                _chamberLampBlinkStates[region] = false;
                _chamberLampBlinkCounters[region] = 0;
            }
            else if (isCompleted && !EthercatConnected)
            {
                // 시뮬레이션 모드 공정 완료 (웨이퍼 있음): 시간 기준 깜빡임 (원본 7a28c12)
                // 틱(1초) 수를 세면 10초마다 바뀌어 점멸로 보이지 않으므로 시간으로 계산한다.
                if (!_simLampBlinkStartMs.ContainsKey(region))
                {
                    _simLampBlinkStartMs[region] = LampClockMs();
                }
                on = GetSimLampBlinkOn(region);
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
                _simLampBlinkStartMs.Remove(region);
                _chamberLampBlinkStates[region] = false;
                _chamberLampBlinkCounters[region] = 0;
            }
            _chamberLampOn[region] = on;
            if (EthercatConnected && !isSyncingEquipmentState)
            {
                SetChamberLamp(region, on);
            }
        }

        // 시뮬레이션 점멸: 하드웨어 램프처럼 꺼짐으로 시작해 한 주기(10틱 x 하드웨어 틱 간격)마다 켜짐/꺼짐을 바꾼다.
        private readonly Dictionary<EquipmentRegion, long> _simLampBlinkStartMs = new Dictionary<EquipmentRegion, long>();
        private readonly System.Diagnostics.Stopwatch _simLampClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>점멸 시계 (밀리초). 시험에서 바꿔 끼운다.</summary>
        internal Func<long> LampClockMs;

        /// <summary>화면이 점멸을 다시 계산할 간격 (원본 SIM_LAMP_BLINK_REFRESH_MS)</summary>
        public const int SimLampBlinkRefreshMs = 100;

        /// <summary>시뮬레이션 모드에서 시간 기준으로 깜빡이는 챔버 램프가 있는지</summary>
        public bool HasSimLampBlinking => _simLampBlinkStartMs.Count > 0;

        private bool GetSimLampBlinkOn(EquipmentRegion region)
        {
            long phaseMs = (long)CHAMBER_LAMP_BLINK_INTERVAL * Math.Max(1, AppSettings.HardwareModeTickMilliseconds);
            long elapsed = LampClockMs() - _simLampBlinkStartMs[region];
            return (elapsed / phaseMs) % 2 == 1;
        }

        /// <summary>
        /// 시뮬레이션 틱(1초) 사이에도 점멸이 바뀌도록 화면이 짧은 간격으로 부른다 (원본 SimLampBlinkTimer_Tick).
        /// 장비 출력은 건드리지 않는다. 램프 표시가 바뀌었으면 true.
        /// </summary>
        public bool TickSimLampBlink()
        {
            if (_simLampBlinkStartMs.Count == 0)
            {
                return false;
            }
            if (EthercatConnected)
            {
                // 장비에 연결되면 화면 램프는 실제 램프와 같은 틱 카운터로 돌아간다.
                _simLampBlinkStartMs.Clear();
                return false;
            }
            bool changed = false;
            foreach (var region in new[] { EquipmentRegion.ChamberA, EquipmentRegion.ChamberB, EquipmentRegion.ChamberC })
            {
                bool before = IsChamberLampOn(region);
                UpdateChamberLamp(GetChamberState(region), region);
                changed |= before != IsChamberLampOn(region);
            }
            return changed;
        }

        private ChamberController.ChamberState GetChamberState(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA: return ChamberAState;
                case EquipmentRegion.ChamberB: return ChamberBState;
                case EquipmentRegion.ChamberC: return ChamberCState;
                default: return null;
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
