using System;
using SemiconductorUi.Controllers;
using SemiconductorUi.Controls;
using SemiconductorUi.Helpers;
using SemiconductorUi.Models;
using SemiconductorUi.Services;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// 원본 Form1.WaferHistory (18d3e85): 공정 흐름의 몇 곳(공정 시작, 집기, 놓기, 챔버 공정 시작·끝, 공정 완료,
    /// 긴급 정지·공정 리셋)에서 시각만 알린다. 장비 명령과 공정 흐름은 바꾸지 않으며, 기록 중 오류는 모두 감싼다.
    /// </summary>
    public partial class ProcessEngine
    {
        private readonly object _waferHistoryLock = new object();
        private WaferHistoryService _waferHistory;

        /// <summary>웨이퍼 이력 서비스 (처음 쓸 때 설정으로 만든다. 시험·스냅샷에서는 미리 넣을 수 있다)</summary>
        public WaferHistoryService WaferHistory
        {
            get
            {
                lock (_waferHistoryLock)
                {
                    return _waferHistory ?? (_waferHistory = new WaferHistoryService(AppSettings.WaferHistoryFolder, AddLogMessage));
                }
            }
            set
            {
                lock (_waferHistoryLock)
                {
                    _waferHistory = value;
                }
            }
        }

        private string ChamberLetter(ChamberController.ChamberState chamber)
        {
            if (chamber == null) return null;
            if (chamber == ChamberAState) return "A";
            if (chamber == ChamberBState) return "B";
            if (chamber == ChamberCState) return "C";
            return null;
        }

        private static string RegionName(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA: return "FOUP A";
                case EquipmentRegion.FoupB: return "FOUP B";
                case EquipmentRegion.ChamberA: return "A";
                case EquipmentRegion.ChamberB: return "B";
                case EquipmentRegion.ChamberC: return "C";
                default: return null;
            }
        }

        private void RecordWafer(Action<WaferHistoryService> action)
        {
            try
            {
                var history = WaferHistory;
                if (history != null)
                {
                    action(history);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"웨이퍼 이력 오류: {ex.Message}");
            }
        }

        internal void RecordWaferLotStarted(int waferCount)
        {
            RecordWafer(h => h.LotStarted(waferCount, ViewModel.LastSelectedRecipe, SecondExposureEnabled, IsTmHardwareModeAvailable(),
                ViewModel.IsLoggedIn ? ViewModel.CurrentUser : ""));
        }

        internal void RecordWaferLotFinished()
        {
            RecordWafer(h => h.LotFinished());
        }

        internal void RecordWaferLotAborted(string reason)
        {
            RecordWafer(h => h.LotAborted(reason));
        }

        private void RecordWaferPickup(TransferController.TransferTask task)
        {
            if (task?.Wafer == null) return;
            string from = task.SourceChamber != null ? ChamberLetter(task.SourceChamber) : (task.FromFoup ? "FOUP A" : RegionName(task.Pickup));
            if (from == null) return;
            int id = task.Wafer.Id;
            RecordWafer(h => h.NotePickup(id, from));
        }

        private void RecordWaferDropoff(TransferController.TransferTask task)
        {
            if (task?.Wafer == null) return;
            string to = task.DestinationChamber != null ? ChamberLetter(task.DestinationChamber) : RegionName(task.Dropoff);
            if (to == null) return;
            int id = task.Wafer.Id;
            RecordWafer(h => h.NoteDropoff(id, to));
        }

        private void RecordWaferProcessStart(ChamberController.ChamberState chamber, Wafer wafer)
        {
            string letter = ChamberLetter(chamber);
            if (letter == null || wafer == null || !ChamberStatusTexts.IsProcessing(chamber.StatusText)) return;
            int seconds = chamber.TotalSeconds;
            RecordWafer(h => h.NoteProcessStart(wafer.Id, letter, seconds));
        }

        private void RecordWaferProcessEnd(ChamberController.ChamberState chamber)
        {
            string letter = ChamberLetter(chamber);
            var wafer = chamber?.CurrentWafer;
            if (letter == null || wafer == null) return;
            RecordWafer(h => h.NoteProcessEnd(wafer.Id, letter));
        }
    }
}
