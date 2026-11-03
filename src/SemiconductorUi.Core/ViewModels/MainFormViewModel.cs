using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SemiconductorUi.Helpers;
using SemiconductorUi.Controls;

namespace SemiconductorUi.ViewModels
{
    /// <summary>
    /// Form1의 UI 상태를 관리하는 ViewModel
    /// MVVM 패턴을 적용하여 UI 로직과 비즈니스 로직을 분리
    /// </summary>
    public class MainFormViewModel : INotifyPropertyChanged
    {
        #region Enums

        public enum ProcessState
        {
            Idle,
            Running,
            Paused,
            Error
        }

        public enum WaferLoadStateType
        {
            None,
            Loading,
            Unloading
        }

        #endregion

        #region Fields

        private bool _isLoggedIn;
        private string _currentUser = "Guest";
        private string _currentRole = "없음";
        private ProcessState _currentProcessState = ProcessState.Idle;
        private bool _hasAlarm;
        private readonly Dictionary<string, bool> _chamberAlarmStatus = new Dictionary<string, bool>();
        private bool _verificationAlarmDismissed;
        private string _lastSelectedRecipe = "Default Recipe";
        
        private int _currentFoupACount;
        private int _currentFoupBCount;
        private string _currentFoupExchangeState = "교환 상태: Standby";
        private string _currentFoupQueueInfo = "대기 FOUP: 없음";
        private string _currentFoupAStatusText = "대기";
        private string _currentFoupBStatusText = "대기";
        private bool _isFoupMounted;
        private bool _isFoupAMounted;
        private bool _isFoupBMounted;
        private bool _demoSetupApplied;
        private bool _demoSimulationStarted;
        private WaferLoadStateType _waferLoadState = WaferLoadStateType.None;
        
        private int _configuredFoupALoadCount;
        private int _foupARemainingInventoryCount;
        private int _currentRecipeWaferCount = AppSettings.MaxFoupCapacity;
        private int _userWaferLoadCount = AppSettings.MaxFoupCapacity;
        private int _activeBatchTargetCount;
        
        private string _statusProcessText = "공정 상태: 대기";
        private string _statusProcessDetail = "공정 상태: 대기";
        private string _statusPressureText = "챔버 압력: 안정";
        private string _statusTemperatureText = "온도: 정상";
        private string _statusDoorText = "문 상태: 모두 닫힘";
        
        private int _chamberCompletedCountA;
        private int _chamberCompletedCountB;
        private int _chamberCompletedCountC;
        
        private EquipmentRegion _tmVisualTarget = EquipmentRegion.TM;
        private bool _tmCarryingVisual;
        private EquipmentRegion _tmCurrentPosition = EquipmentRegion.TM;
        private float _tmBladeExtensionFactor = 0.55f;
        private bool _mainLampBlinkState;
        private bool _secondExposureEnabled;
        
        private readonly Dictionary<EquipmentRegion, bool> _doorOpenStates = new Dictionary<EquipmentRegion, bool>();

        #endregion

        #region Properties - User & Authentication

        /// <summary>
        /// 로그인 상태
        /// </summary>
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set => SetProperty(ref _isLoggedIn, value);
        }

        /// <summary>
        /// 현재 사용자명
        /// </summary>
        public string CurrentUser
        {
            get => _currentUser;
            set => SetProperty(ref _currentUser, value ?? "Guest");
        }

        /// <summary>
        /// 현재 사용자 역할
        /// </summary>
        public string CurrentRole
        {
            get => _currentRole;
            set => SetProperty(ref _currentRole, value ?? "없음");
        }

        #endregion

        #region Properties - Process State

        /// <summary>
        /// 현재 공정 상태
        /// </summary>
        public ProcessState CurrentProcessState
        {
            get => _currentProcessState;
            set => SetProperty(ref _currentProcessState, value);
        }

        /// <summary>
        /// 알람 상태
        /// </summary>
        public bool HasAlarm
        {
            get => _hasAlarm;
            set => SetProperty(ref _hasAlarm, value);
        }

        /// <summary>
        /// 챔버별 알람 상태
        /// </summary>
        public Dictionary<string, bool> ChamberAlarmStatus => _chamberAlarmStatus;

        /// <summary>
        /// 특정 챔버의 알람 상태 설정
        /// </summary>
        public void SetChamberAlarmStatus(string chamberId, bool hasAlarm)
        {
            if (_chamberAlarmStatus.ContainsKey(chamberId))
            {
                if (_chamberAlarmStatus[chamberId] == hasAlarm) return;
                _chamberAlarmStatus[chamberId] = hasAlarm;
            }
            else
            {
                _chamberAlarmStatus[chamberId] = hasAlarm;
            }
            OnPropertyChanged(nameof(ChamberAlarmStatus));
            UpdateHasAlarm();
        }

        /// <summary>
        /// Verification 알람 해제 여부
        /// </summary>
        public bool VerificationAlarmDismissed
        {
            get => _verificationAlarmDismissed;
            set => SetProperty(ref _verificationAlarmDismissed, value);
        }

        #endregion

        #region Properties - Recipe

        /// <summary>
        /// 마지막으로 선택된 레시피
        /// </summary>
        public string LastSelectedRecipe
        {
            get => _lastSelectedRecipe;
            set => SetProperty(ref _lastSelectedRecipe, value ?? "Default Recipe");
        }

        /// <summary>
        /// 현재 레시피 웨이퍼 수
        /// </summary>
        public int CurrentRecipeWaferCount
        {
            get => _currentRecipeWaferCount;
            set => SetProperty(ref _currentRecipeWaferCount, value);
        }

        #endregion

        #region Properties - FOUP

        /// <summary>
        /// FOUP A 현재 웨이퍼 수
        /// </summary>
        public int CurrentFoupACount
        {
            get => _currentFoupACount;
            set => SetProperty(ref _currentFoupACount, value);
        }

        /// <summary>
        /// FOUP B 현재 웨이퍼 수
        /// </summary>
        public int CurrentFoupBCount
        {
            get => _currentFoupBCount;
            set => SetProperty(ref _currentFoupBCount, value);
        }

        /// <summary>
        /// FOUP 교환 상태 텍스트
        /// </summary>
        public string CurrentFoupExchangeState
        {
            get => _currentFoupExchangeState;
            set => SetProperty(ref _currentFoupExchangeState, value ?? "교환 상태: Standby");
        }

        /// <summary>
        /// FOUP 대기열 정보
        /// </summary>
        public string CurrentFoupQueueInfo
        {
            get => _currentFoupQueueInfo;
            set => SetProperty(ref _currentFoupQueueInfo, value ?? "대기 FOUP: 없음");
        }

        /// <summary>
        /// FOUP A 상태 텍스트
        /// </summary>
        public string CurrentFoupAStatusText
        {
            get => _currentFoupAStatusText;
            set => SetProperty(ref _currentFoupAStatusText, value ?? "대기");
        }

        /// <summary>
        /// FOUP B 상태 텍스트
        /// </summary>
        public string CurrentFoupBStatusText
        {
            get => _currentFoupBStatusText;
            set => SetProperty(ref _currentFoupBStatusText, value ?? "대기");
        }

        /// <summary>
        /// FOUP 장착 여부
        /// </summary>
        public bool IsFoupMounted
        {
            get => _isFoupMounted;
            set => SetProperty(ref _isFoupMounted, value);
        }

        /// <summary>
        /// FOUP A 장착 여부
        /// </summary>
        public bool IsFoupAMounted
        {
            get => _isFoupAMounted;
            set => SetProperty(ref _isFoupAMounted, value);
        }

        /// <summary>
        /// FOUP B 장착 여부
        /// </summary>
        public bool IsFoupBMounted
        {
            get => _isFoupBMounted;
            set => SetProperty(ref _isFoupBMounted, value);
        }

        /// <summary>
        /// 설정된 FOUP A 로드 수
        /// </summary>
        public int ConfiguredFoupALoadCount
        {
            get => _configuredFoupALoadCount;
            set => SetProperty(ref _configuredFoupALoadCount, value);
        }

        /// <summary>
        /// FOUP A 남은 재고 수
        /// </summary>
        public int FoupARemainingInventoryCount
        {
            get => _foupARemainingInventoryCount;
            set => SetProperty(ref _foupARemainingInventoryCount, value);
        }

        #endregion

        #region Properties - Wafer Load

        /// <summary>
        /// 웨이퍼 로드 상태
        /// </summary>
        public WaferLoadStateType WaferLoadState
        {
            get => _waferLoadState;
            set => SetProperty(ref _waferLoadState, value);
        }

        /// <summary>
        /// 사용자 설정 웨이퍼 로드 수
        /// </summary>
        public int UserWaferLoadCount
        {
            get => _userWaferLoadCount;
            set => SetProperty(ref _userWaferLoadCount, value);
        }

        /// <summary>
        /// 활성 배치 목표 수
        /// </summary>
        public int ActiveBatchTargetCount
        {
            get => _activeBatchTargetCount;
            set => SetProperty(ref _activeBatchTargetCount, value);
        }

        #endregion

        #region Properties - Status Text

        /// <summary>
        /// 공정 상태 텍스트
        /// </summary>
        public string StatusProcessText
        {
            get => _statusProcessText;
            set => SetProperty(ref _statusProcessText, value ?? "공정 상태: 대기");
        }

        /// <summary>
        /// 공정 상세 상태 텍스트
        /// </summary>
        public string StatusProcessDetail
        {
            get => _statusProcessDetail;
            set => SetProperty(ref _statusProcessDetail, value ?? "공정 상태: 대기");
        }

        /// <summary>
        /// 압력 상태 텍스트
        /// </summary>
        public string StatusPressureText
        {
            get => _statusPressureText;
            set => SetProperty(ref _statusPressureText, value ?? "챔버 압력: 안정");
        }

        /// <summary>
        /// 온도 상태 텍스트
        /// </summary>
        public string StatusTemperatureText
        {
            get => _statusTemperatureText;
            set => SetProperty(ref _statusTemperatureText, value ?? "온도: 정상");
        }

        /// <summary>
        /// 문 상태 텍스트
        /// </summary>
        public string StatusDoorText
        {
            get => _statusDoorText;
            set => SetProperty(ref _statusDoorText, value ?? "문 상태: 모두 닫힘");
        }

        #endregion

        #region Properties - Chamber Completion

        /// <summary>
        /// 챔버 A 완료 수
        /// </summary>
        public int ChamberCompletedCountA
        {
            get => _chamberCompletedCountA;
            set => SetProperty(ref _chamberCompletedCountA, value);
        }

        /// <summary>
        /// 챔버 B 완료 수
        /// </summary>
        public int ChamberCompletedCountB
        {
            get => _chamberCompletedCountB;
            set => SetProperty(ref _chamberCompletedCountB, value);
        }

        /// <summary>
        /// 챔버 C 완료 수
        /// </summary>
        public int ChamberCompletedCountC
        {
            get => _chamberCompletedCountC;
            set => SetProperty(ref _chamberCompletedCountC, value);
        }

        #endregion

        #region Properties - Transfer Module

        /// <summary>
        /// TM 시각적 타겟 영역
        /// </summary>
        public EquipmentRegion TmVisualTarget
        {
            get => _tmVisualTarget;
            set => SetProperty(ref _tmVisualTarget, value);
        }

        /// <summary>
        /// TM 웨이퍼 운반 중 여부
        /// </summary>
        public bool TmCarryingVisual
        {
            get => _tmCarryingVisual;
            set => SetProperty(ref _tmCarryingVisual, value);
        }

        /// <summary>
        /// TM 현재 위치
        /// </summary>
        public EquipmentRegion TmCurrentPosition
        {
            get => _tmCurrentPosition;
            set => SetProperty(ref _tmCurrentPosition, value);
        }

        /// <summary>
        /// TM 블레이드 확장 비율
        /// </summary>
        public float TmBladeExtensionFactor
        {
            get => _tmBladeExtensionFactor;
            set => SetProperty(ref _tmBladeExtensionFactor, value);
        }

        #endregion

        #region Properties - Door States

        /// <summary>
        /// 도어 열림 상태 (Region별)
        /// </summary>
        public Dictionary<EquipmentRegion, bool> DoorOpenStates => _doorOpenStates;

        /// <summary>
        /// 특정 Region의 도어 상태 설정
        /// </summary>
        public void SetDoorState(EquipmentRegion region, bool isOpen)
        {
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return;
            }

            if (_doorOpenStates.TryGetValue(region, out var current) && current == isOpen)
            {
                return;
            }

            _doorOpenStates[region] = isOpen;
            OnPropertyChanged(nameof(DoorOpenStates));
            UpdateStatusDoorText();
        }

        /// <summary>
        /// 특정 Region의 도어 상태 가져오기
        /// </summary>
        public bool IsDoorOpen(EquipmentRegion region)
        {
            if (!EquipmentRegionHelper.RequiresDoor(region))
            {
                return true; // 도어가 필요 없는 Region은 항상 열림으로 간주
            }

            if (!_doorOpenStates.TryGetValue(region, out var isOpen))
            {
                _doorOpenStates[region] = false;
                return false;
            }

            return isOpen;
        }

        /// <summary>
        /// 모든 도어 상태 초기화
        /// </summary>
        public void ResetDoorStates()
        {
            _doorOpenStates[EquipmentRegion.ChamberA] = false;
            _doorOpenStates[EquipmentRegion.ChamberB] = false;
            _doorOpenStates[EquipmentRegion.ChamberC] = false;
            OnPropertyChanged(nameof(DoorOpenStates));
            UpdateStatusDoorText();
        }

        /// <summary>
        /// 도어 상태 텍스트 업데이트
        /// </summary>
        private void UpdateStatusDoorText()
        {
            int openCount = 0;
            foreach (var kvp in _doorOpenStates)
            {
                if (kvp.Value) openCount++;
            }

            if (openCount == 0)
            {
                StatusDoorText = "문 상태: 모두 닫힘";
            }
            else if (openCount == _doorOpenStates.Count)
            {
                StatusDoorText = "문 상태: 모두 열림";
            }
            else
            {
                StatusDoorText = $"문 상태: {openCount}개 열림";
            }
        }

        #endregion

        #region Properties - Simulation & Demo

        /// <summary>
        /// 데모 설정 적용 여부
        /// </summary>
        public bool DemoSetupApplied
        {
            get => _demoSetupApplied;
            set => SetProperty(ref _demoSetupApplied, value);
        }

        /// <summary>
        /// 데모 시뮬레이션 시작 여부
        /// </summary>
        public bool DemoSimulationStarted
        {
            get => _demoSimulationStarted;
            set => SetProperty(ref _demoSimulationStarted, value);
        }

        #endregion

        #region Properties - UI State

        /// <summary>
        /// 메인 램프 깜빡임 상태
        /// </summary>
        public bool MainLampBlinkState
        {
            get => _mainLampBlinkState;
            set => SetProperty(ref _mainLampBlinkState, value);
        }

        /// <summary>
        /// 두 번째 노광 활성화 여부
        /// </summary>
        public bool SecondExposureEnabled
        {
            get => _secondExposureEnabled;
            set => SetProperty(ref _secondExposureEnabled, value);
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 전체 알람 상태 업데이트
        /// </summary>
        private void UpdateHasAlarm()
        {
            bool anyAlarm = false;
            foreach (var alarmStatus in _chamberAlarmStatus.Values)
            {
                if (alarmStatus)
                {
                    anyAlarm = true;
                    break;
                }
            }
            HasAlarm = anyAlarm;
        }

        /// <summary>
        /// 모든 챔버 알람 상태 초기화
        /// </summary>
        public void ClearAllChamberAlarms()
        {
            var keys = new List<string>(_chamberAlarmStatus.Keys);
            foreach (var key in keys)
            {
                _chamberAlarmStatus[key] = false;
            }
            OnPropertyChanged(nameof(ChamberAlarmStatus));
            UpdateHasAlarm();
        }

        #endregion
    }
}

