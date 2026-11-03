using System;
using System.Collections.Generic;
using SemiconductorUi.Models;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;

namespace SemiconductorUi.Controllers
{
    /// <summary>
    /// 챔버 상태 및 공정 제어를 담당하는 컨트롤러
    /// </summary>
    public class ChamberController : IChamberService
    {
        #region Nested Classes

        /// <summary>
        /// 공정 단계 정의
        /// </summary>
        public class SimulationStepDefinition
        {
            public string DisplayName { get; }
            public int DurationSeconds { get; set; }

            public SimulationStepDefinition(string name, int durationSeconds)
            {
                DisplayName = name;
                DurationSeconds = durationSeconds;
            }
        }

        /// <summary>
        /// 챔버 상태 정보
        /// </summary>
        public class ChamberState
        {
            public string UnitKey { get; }
            public EquipmentRegion Region { get; }
            public SimulationStepDefinition Step { get; }
            public Wafer CurrentWafer { get; set; }
            public int RemainingSeconds { get; set; }
            public int TotalSeconds { get; set; }
            public string StatusText { get; set; } = ChamberStatusTexts.Idle;
            public bool ReservedForIncoming { get; set; }
            public bool PickupScheduled { get; set; }
            public double ProcessingAccumulator { get; set; }
            public DateTime LastProcessedTime { get; set; } = DateTime.MinValue;

            // 디버깅 로그 중복 방지를 위한 상태 추적 필드
            internal string LastDebugState { get; set; } = string.Empty;
            internal string LastDebugStateLoad { get; set; } = string.Empty;

            public ChamberState(string unitKey, EquipmentRegion region, SimulationStepDefinition step)
            {
                UnitKey = unitKey;
                Region = region;
                Step = step;
            }
        }

        /// <summary>
        /// 챔버 환경 사양
        /// </summary>
        public class ChamberEnvironmentSpec
        {
            public string Temperature { get; }
            public string Pressure { get; }
            public string Humidity { get; }
            public string Notes { get; }
            public double TargetTemperatureC { get; }
            public double TargetPressureTorr { get; }
            public double TargetHumidityPercent { get; }

            public ChamberEnvironmentSpec(
                string temperature,
                string pressure,
                string humidity,
                string notes,
                double targetTemperatureC,
                double targetPressureTorr,
                double targetHumidityPercent)
            {
                Temperature = temperature;
                Pressure = pressure;
                Humidity = humidity;
                Notes = notes;
                TargetTemperatureC = targetTemperatureC;
                TargetPressureTorr = targetPressureTorr;
                TargetHumidityPercent = targetHumidityPercent;
            }
        }

        /// <summary>
        /// 챔버 환경 실시간 데이터
        /// </summary>
        public class ChamberEnvironmentLive
        {
            public double TemperatureC { get; set; }
            public double PressureTorr { get; set; }
            public double HumidityPercent { get; set; }
        }

        #endregion

        #region Fields

        private readonly Dictionary<string, ChamberState> _chambers = new Dictionary<string, ChamberState>();
        private readonly Dictionary<string, ChamberEnvironmentSpec> _chamberEnvSpecs = new Dictionary<string, ChamberEnvironmentSpec>();
        private readonly Dictionary<string, ChamberEnvironmentLive> _chamberEnvLive = new Dictionary<string, ChamberEnvironmentLive>();
        private readonly SimulationStepDefinition[] _processFlow;

        #endregion

        #region Properties

        /// <summary>
        /// Chamber A 상태
        /// </summary>
        public ChamberState ChamberA => _chambers.TryGetValue("PMA", out var state) ? state : null;

        /// <summary>
        /// Chamber B 상태
        /// </summary>
        public ChamberState ChamberB => _chambers.TryGetValue("PMB", out var state) ? state : null;

        /// <summary>
        /// Chamber C 상태
        /// </summary>
        public ChamberState ChamberC => _chambers.TryGetValue("PMC", out var state) ? state : null;

        /// <summary>
        /// 모든 챔버 상태
        /// </summary>
        public IEnumerable<ChamberState> AllChambers => _chambers.Values;

        #endregion

        #region Constructor

        /// <summary>
        /// ChamberController 생성
        /// </summary>
        /// <param name="processFlow">공정 플로우 정의</param>
        public ChamberController(SimulationStepDefinition[] processFlow)
        {
            _processFlow = processFlow ?? throw new ArgumentNullException(nameof(processFlow));
            InitializeChambers();
            InitializeEnvironmentSpecs();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 챔버 초기화
        /// </summary>
        private void InitializeChambers()
        {
            if (_processFlow.Length < 3)
            {
                throw new InvalidOperationException("공정 플로우는 최소 3단계가 필요합니다.");
            }

            _chambers["PMA"] = new ChamberState("PMA", EquipmentRegion.ChamberA, _processFlow[0]);
            _chambers["PMB"] = new ChamberState("PMB", EquipmentRegion.ChamberB, _processFlow[1]);
            _chambers["PMC"] = new ChamberState("PMC", EquipmentRegion.ChamberC, _processFlow[2]);
        }

        /// <summary>
        /// 챔버 환경 사양 초기화
        /// </summary>
        private void InitializeEnvironmentSpecs()
        {
            // 기본 환경 사양 설정
            _chamberEnvSpecs["PMA"] = new ChamberEnvironmentSpec(
                "23.0°C", "760.0 Torr", "45.0%", "PR 도포 챔버",
                23.0, 760.0, 45.0);

            _chamberEnvSpecs["PMB"] = new ChamberEnvironmentSpec(
                "22.5°C", "0.005 Torr", "5.0%", "노광 챔버 (1차)",
                22.5, 0.005, 5.0);

            _chamberEnvSpecs["PMC"] = new ChamberEnvironmentSpec(
                "110.0°C", "50.0 Torr", "3.0%", "노광 챔버 (2차/병렬)",
                110.0, 50.0, 3.0);
        }

        /// <summary>
        /// 환경 텔레메트리 초기화
        /// </summary>
        public void InitializeEnvironmentTelemetry()
        {
            foreach (var kvp in _chamberEnvSpecs)
            {
                var spec = kvp.Value;
                _chamberEnvLive[kvp.Key] = new ChamberEnvironmentLive
                {
                    TemperatureC = spec.TargetTemperatureC,
                    PressureTorr = spec.TargetPressureTorr,
                    HumidityPercent = spec.TargetHumidityPercent
                };
            }
        }

        #endregion

        #region Chamber State Management

        /// <summary>
        /// 특정 유닛의 챔버 상태 가져오기
        /// </summary>
        public ChamberState GetChamberStateForUnit(string unitKey)
        {
            return _chambers.TryGetValue(unitKey, out var state) ? state : null;
        }

        /// <summary>
        /// 챔버 상태 초기화
        /// </summary>
        public void ResetChamberStates()
        {
            foreach (var chamber in _chambers.Values)
            {
                chamber.CurrentWafer = null;
                chamber.RemainingSeconds = 0;
                chamber.TotalSeconds = 0;
                chamber.StatusText = ChamberStatusTexts.Idle;
                chamber.ReservedForIncoming = false;
                chamber.PickupScheduled = false;
                chamber.ProcessingAccumulator = 0;
                chamber.LastProcessedTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 챔버에 웨이퍼 할당
        /// </summary>
        public bool AssignWaferToChamber(string unitKey, Wafer wafer)
        {
            var chamber = GetChamberStateForUnit(unitKey);
            if (chamber == null || chamber.CurrentWafer != null)
            {
                return false;
            }

            chamber.CurrentWafer = wafer;
            chamber.RemainingSeconds = chamber.Step.DurationSeconds;
            chamber.TotalSeconds = chamber.Step.DurationSeconds;
            chamber.StatusText = ChamberStatusTexts.Processing;
            return true;
        }

        /// <summary>
        /// 챔버에서 웨이퍼 제거
        /// </summary>
        public Wafer RemoveWaferFromChamber(string unitKey)
        {
            var chamber = GetChamberStateForUnit(unitKey);
            if (chamber == null)
            {
                return null;
            }

            var wafer = chamber.CurrentWafer;
            chamber.CurrentWafer = null;
            chamber.RemainingSeconds = 0;
            chamber.TotalSeconds = 0;
            chamber.StatusText = ChamberStatusTexts.Idle;
            chamber.PickupScheduled = false;
            return wafer;
        }

        /// <summary>
        /// 챔버가 공정 중인지 확인
        /// </summary>
        public bool IsChamberProcessing(string unitKey)
        {
            var chamber = GetChamberStateForUnit(unitKey);
            return chamber != null && chamber.CurrentWafer != null && chamber.RemainingSeconds > 0;
        }

        /// <summary>
        /// 챔버가 이송 준비 상태인지 확인
        /// </summary>
        public bool IsReadyForTransfer(string unitKey)
        {
            var chamber = GetChamberStateForUnit(unitKey);
            return chamber != null 
                && chamber.CurrentWafer != null 
                && chamber.RemainingSeconds <= 0 
                && !chamber.PickupScheduled;
        }

        #endregion

        #region Environment Management

        /// <summary>
        /// 챔버 환경 사양 가져오기
        /// </summary>
        public ChamberEnvironmentSpec GetEnvironmentSpec(string unitKey)
        {
            return _chamberEnvSpecs.TryGetValue(unitKey, out var spec) ? spec : null;
        }

        /// <summary>
        /// 챔버 환경 실시간 데이터 가져오기
        /// </summary>
        public ChamberEnvironmentLive GetEnvironmentLive(string unitKey)
        {
            return _chamberEnvLive.TryGetValue(unitKey, out var live) ? live : null;
        }

        /// <summary>
        /// 환경 실시간 데이터 업데이트
        /// </summary>
        public ChamberEnvironmentLive UpdateLiveEnvironment(string unitKey, ChamberState chamberState)
        {
            if (!_chamberEnvSpecs.TryGetValue(unitKey, out var spec))
            {
                return null;
            }

            if (!_chamberEnvLive.TryGetValue(unitKey, out var live))
            {
                live = new ChamberEnvironmentLive
                {
                    TemperatureC = spec.TargetTemperatureC,
                    PressureTorr = spec.TargetPressureTorr,
                    HumidityPercent = spec.TargetHumidityPercent
                };
                _chamberEnvLive[unitKey] = live;
            }

            // 실시간 환경 데이터 업데이트 로직은 여기에 구현
            // 현재는 기본값 유지

            return live;
        }

        #endregion

        #region Process Control

        /// <summary>
        /// 공정 단계 시간 업데이트
        /// </summary>
        public void UpdateStepDurations(int durA, int durB, int durC)
        {
            if (ChamberA != null) ChamberA.Step.DurationSeconds = durA;
            if (ChamberB != null) ChamberB.Step.DurationSeconds = durB;
            if (ChamberC != null) ChamberC.Step.DurationSeconds = durC;
        }

        /// <summary>
        /// 공정 진행 업데이트 (시뮬레이션용)
        /// </summary>
        public void UpdateProcessing(double secondsPerTick, bool isRunning)
        {
            if (!isRunning)
            {
                return;
            }

            foreach (var chamber in _chambers.Values)
            {
                if (chamber.CurrentWafer != null && chamber.RemainingSeconds > 0)
                {
                    chamber.ProcessingAccumulator += secondsPerTick;
                    chamber.RemainingSeconds = Math.Max(0, 
                        chamber.TotalSeconds - (int)chamber.ProcessingAccumulator);

                    if (chamber.RemainingSeconds <= 0)
                    {
                        chamber.StatusText = ChamberStatusTexts.Completed;
                    }
                }
            }
        }

        #endregion
    }
}

