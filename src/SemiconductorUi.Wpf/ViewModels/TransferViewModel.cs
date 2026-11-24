using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>이송 대기열 한 줄 (원본 TransferForm ListView "출발 → 도착 · 웨이퍼 ID")</summary>
    public sealed class TransferQueueRow
    {
        public TransferQueueRow(TransferQueueEntry entry)
        {
            IsCurrent = entry.IsCurrent;
            OrderText = entry.IsCurrent ? "진행 중" : entry.Order.ToString();
            From = entry.From;
            To = entry.To;
            WaferText = entry.WaferId.HasValue ? $"#{entry.WaferId}" : "-";
            RetryText = entry.RetryCount > 0 ? $"재시도 {entry.RetryCount}/{TransferController.TransferTask.MaxRetryCount}" : "";
        }

        public bool IsCurrent { get; }
        public string OrderText { get; }
        public string From { get; }
        public string To { get; }
        public string WaferText { get; }
        public string RetryText { get; }
    }

    /// <summary>이송 탭 위쪽 경로 카드 (FOUP A → Chamber A/B/C → FOUP B)</summary>
    public sealed class TransferUnitCard : ObservableObject
    {
        private string _primary = "";
        private string _secondary = "";
        private bool _hasWafer;
        private bool _isSource;
        private bool _isTarget;

        public TransferUnitCard(string title, EquipmentRegion region)
        {
            Title = title;
            Region = region;
        }

        public string Title { get; }
        public EquipmentRegion Region { get; }
        public string Primary { get => _primary; internal set => SetProperty(ref _primary, value); }
        public string Secondary { get => _secondary; internal set => SetProperty(ref _secondary, value); }
        public bool HasWafer { get => _hasWafer; internal set => SetProperty(ref _hasWafer, value); }

        /// <summary>진행 중인 이송의 출발지</summary>
        public bool IsSource { get => _isSource; internal set => SetProperty(ref _isSource, value); }

        /// <summary>진행 중인 이송의 도착지</summary>
        public bool IsTarget { get => _isTarget; internal set => SetProperty(ref _isTarget, value); }
    }

    /// <summary>
    /// 운전 화면 "이송" 탭 (원본 Forms.TransferForm + Form1.AutoPlanTransferIfNeeded / ClearTransferQueue / GetTransferQueueLines).
    /// 원본은 탭을 누를 때 모달 창으로 대기열 글자 목록을 보였다. WPF는 탭 안에서 TM 상태, 진행 중 작업, 대기열, 경로별 웨이퍼를
    /// 보이는 동안 0.5초마다 고쳐 보인다.
    /// </summary>
    public sealed class TransferViewModel : ObservableObject
    {
        private readonly Func<ProcessEngine> _engine;
        private readonly IDialogService _dialogs;
        private DispatcherTimer _timer;
        private string _queueSignature;
        private string _tmStatusText = "";
        private string _tmPhaseText = "";
        private bool _isTmBusy;
        private string _currentRouteText = "";
        private string _currentWaferText = "";
        private string _currentRetryText = "";
        private bool _hasCurrent;
        private int _queuedCount;
        private string _lastActionText = "";
        private bool _isLotRunning;

        public TransferViewModel(Func<ProcessEngine> engine, IDialogService dialogs)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _dialogs = dialogs;
            Units = new[]
            {
                new TransferUnitCard("FOUP A", EquipmentRegion.FoupA),
                new TransferUnitCard("Chamber A", EquipmentRegion.ChamberA),
                new TransferUnitCard("Chamber B", EquipmentRegion.ChamberB),
                new TransferUnitCard("Chamber C", EquipmentRegion.ChamberC),
                new TransferUnitCard("FOUP B", EquipmentRegion.FoupB)
            };
            AutoPlanCommand = new RelayCommand(AutoPlan);
            ClearQueueCommand = new RelayCommand(ClearQueue, () => !IsLotRunning);
            CopyQueueCommand = new RelayCommand(CopyQueue);
        }

        public IReadOnlyList<TransferUnitCard> Units { get; }

        public ObservableCollection<TransferQueueRow> QueueRows { get; } = new ObservableCollection<TransferQueueRow>();

        /// <summary>TM 상태 (모니터링 화면 TM 표시와 같은 글자, 원본 Form1.GetTmStatusText)</summary>
        public string TmStatusText { get => _tmStatusText; private set => SetProperty(ref _tmStatusText, value); }

        /// <summary>TM 단계 이름 (TransferController.TmPhase)</summary>
        public string TmPhaseText { get => _tmPhaseText; private set => SetProperty(ref _tmPhaseText, value); }

        public bool IsTmBusy { get => _isTmBusy; private set => SetProperty(ref _isTmBusy, value); }

        public bool HasCurrent { get => _hasCurrent; private set => SetProperty(ref _hasCurrent, value); }

        public string CurrentRouteText { get => _currentRouteText; private set => SetProperty(ref _currentRouteText, value); }

        public string CurrentWaferText { get => _currentWaferText; private set => SetProperty(ref _currentWaferText, value); }

        public string CurrentRetryText { get => _currentRetryText; private set => SetProperty(ref _currentRetryText, value); }

        /// <summary>진행 중 작업을 뺀 대기 건수</summary>
        public int QueuedCount
        {
            get => _queuedCount;
            private set
            {
                if (SetProperty(ref _queuedCount, value)) OnPropertyChanged(nameof(QueueHeaderText));
            }
        }

        public string QueueHeaderText => QueuedCount > 0 ? $"대기 중 이송 작업 {QueuedCount}건" : "대기 중 이송 작업 없음";

        public bool HasQueueRows => QueueRows.Count > 0;

        /// <summary>공정(로트)이 돌고 있는 동안은 큐 비우기를 막는다 (WPF 안전장치)</summary>
        public bool IsLotRunning
        {
            get => _isLotRunning;
            private set
            {
                if (SetProperty(ref _isLotRunning, value))
                {
                    OnPropertyChanged(nameof(ClearQueueToolTip));
                    RelayCommand.Refresh();
                }
            }
        }

        public string ClearQueueToolTip => IsLotRunning
            ? "공정 중에는 비울 수 없습니다. 비우면 챔버 예약이 풀리지 않아 공정이 멈춥니다."
            : "대기 중인 이송 작업을 모두 지웁니다.";

        /// <summary>마지막 버튼 결과 (계획 없음 등)</summary>
        public string LastActionText { get => _lastActionText; private set => SetProperty(ref _lastActionText, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        public ICommand AutoPlanCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand CopyQueueCommand { get; }

        /// <summary>탭이 보이기 시작했다: 원본처럼 자동 계획 훅을 부르고, 0.5초 갱신을 시작한다.</summary>
        public void OnShown()
        {
            LastActionText = "";
            _engine()?.AutoPlanTransferIfNeeded();
            Refresh();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _timer.Tick += (s, e) => Refresh();
            }
            _timer.Start();
            OnPropertyChanged(nameof(IsRunning));
        }

        public void OnHidden()
        {
            _timer?.Stop();
            OnPropertyChanged(nameof(IsRunning));
        }

        private void AutoPlan()
        {
            var engine = _engine();
            if (engine == null) return;
            LastActionText = engine.AutoPlanTransferIfNeeded()
                ? "이송 계획을 로그에 남겼습니다 (데모: 실제 이송은 공정 시작 때 자동으로 예약됩니다)."
                : "계획할 이송이 없습니다. TM이 대기 중이고 FOUP에 웨이퍼가 있어야 합니다.";
            Refresh();
        }

        private void ClearQueue()
        {
            var engine = _engine();
            if (engine == null || IsLotRunning) return;
            int count = engine.TransferQueueCount;
            if (count > 0 && _dialogs != null && !_dialogs.Confirm($"대기 중인 이송 작업 {count}건을 모두 지울까요?", "이송 큐 비우기", true)) return;
            engine.ClearTransferQueue();
            LastActionText = $"이송 큐를 비웠습니다 ({count}건).";
            Refresh();
        }

        private void CopyQueue()
        {
            var engine = _engine();
            if (engine == null) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, engine.GetTransferQueueLines()));
                LastActionText = "대기열을 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                LastActionText = $"복사하지 못했습니다: {ex.Message}";
            }
        }

        /// <summary>TM, 진행 중 작업, 대기열, 경로 카드를 다시 읽는다</summary>
        public void Refresh()
        {
            var engine = _engine();
            if (engine == null) return;

            IsLotRunning = engine.SimulationRunning;
            TmStatusText = engine.GetTmStatusText();
            TmPhaseText = $"단계 {engine.TmPhase}";
            IsTmBusy = engine.TmPhase != TransferController.TmPhase.Idle;

            var entries = engine.GetTransferQueueEntries();
            var current = entries.FirstOrDefault(e => e.IsCurrent);
            HasCurrent = current != null;
            CurrentRouteText = current != null ? $"{current.From} → {current.To}" : "진행 중인 이송 없음";
            CurrentWaferText = current?.WaferId != null ? $"웨이퍼 #{current.WaferId}" : "";
            CurrentRetryText = current != null && current.RetryCount > 0
                ? $"재시도 {current.RetryCount}/{TransferController.TransferTask.MaxRetryCount}"
                : "";
            QueuedCount = entries.Count(e => !e.IsCurrent);

            string signature = string.Join("|", entries.Select(e => $"{e.Order}:{e.From}:{e.To}:{e.WaferId}:{e.RetryCount}"));
            if (signature != _queueSignature)
            {
                _queueSignature = signature;
                QueueRows.Clear();
                foreach (var e in entries) QueueRows.Add(new TransferQueueRow(e));
                OnPropertyChanged(nameof(HasQueueRows));
            }

            var task = engine.CurrentTransfer;
            foreach (var card in Units)
            {
                card.IsSource = task != null && task.Pickup == card.Region;
                card.IsTarget = task != null && task.Dropoff == card.Region;
                switch (card.Region)
                {
                    case EquipmentRegion.FoupA:
                        SetFoup(card, engine.ViewModel.CurrentFoupACount, engine.ViewModel.IsFoupAMounted);
                        break;
                    case EquipmentRegion.FoupB:
                        SetFoup(card, engine.ViewModel.CurrentFoupBCount, engine.ViewModel.IsFoupBMounted);
                        break;
                    case EquipmentRegion.ChamberA:
                        SetChamber(card, engine.ChamberAState);
                        break;
                    case EquipmentRegion.ChamberB:
                        SetChamber(card, engine.ChamberBState);
                        break;
                    case EquipmentRegion.ChamberC:
                        SetChamber(card, engine.ChamberCState);
                        break;
                }
            }
        }

        private static void SetFoup(TransferUnitCard card, int count, bool mounted)
        {
            card.HasWafer = count > 0;
            card.Primary = mounted ? $"{count}장" : "미장착";
            card.Secondary = mounted ? (count > 0 ? "웨이퍼 있음" : "비어 있음") : "FOUP 없음";
        }

        private static void SetChamber(TransferUnitCard card, ChamberController.ChamberState chamber)
        {
            var wafer = chamber?.CurrentWafer;
            card.HasWafer = wafer != null;
            card.Primary = wafer != null ? $"웨이퍼 #{wafer.Id}" : "비어 있음";
            string status = chamber?.StatusText ?? "";
            if (chamber != null && chamber.ReservedForIncoming && wafer == null) status = "반입 예약";
            if (chamber != null && wafer != null && chamber.RemainingSeconds > 0) status = $"{status} · {chamber.RemainingSeconds}초";
            card.Secondary = status;
        }
    }
}
