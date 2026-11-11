using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>검증 목록 한 줄 (원본 VerificationForm ListView: 체크 항목 / 결과)</summary>
    public sealed class VerificationRow : ObservableObject
    {
        private InterlockCheckState _state;
        private string _detail;

        public VerificationRow(InterlockCheck check)
        {
            Name = check.Name;
            _state = check.State;
            _detail = check.Detail;
        }

        public string Name { get; }

        public InterlockCheckState State
        {
            get => _state;
            private set
            {
                if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(ResultText));
            }
        }

        /// <summary>원본 결과 칸: OK / FAIL (데이터가 없으면 WPF에서 따로 표시)</summary>
        public string ResultText
        {
            get
            {
                switch (State)
                {
                    case InterlockCheckState.Pass: return "OK";
                    case InterlockCheckState.Fail: return "FAIL";
                    default: return "데이터 없음";
                }
            }
        }

        public string Detail
        {
            get => _detail;
            private set => SetProperty(ref _detail, value);
        }

        internal void Update(InterlockCheck check)
        {
            State = check.State;
            Detail = check.Detail;
        }
    }

    /// <summary>
    /// 운전 화면 "검증" 탭 (원본 Forms.VerificationForm + Form1.RunVerificationChecks).
    /// 원본은 탭을 누를 때 한 번 검사해 모달 창으로 결과를 보였다. WPF는 탭 안에 두고, 보이는 동안 1초마다 다시 판정한다.
    /// 로그는 원본처럼 탭에 들어올 때와 "다시 검사"를 누를 때만 남긴다.
    /// </summary>
    public sealed class VerificationViewModel : ObservableObject
    {
        private readonly Func<ProcessEngine> _engine;
        private readonly Func<DateTime> _now;
        private DispatcherTimer _timer;
        private bool _allPassed;
        private string _summaryText = "";
        private string _reasonsText = "";
        private string _countsText = "";
        private string _checkedAtText = "";
        private bool _isWarningDismissed;

        public VerificationViewModel(Func<ProcessEngine> engine, Func<DateTime> now = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _now = now ?? (() => DateTime.Now);
            RecheckCommand = new RelayCommand(Recheck);
        }

        public ObservableCollection<VerificationRow> Rows { get; } = new ObservableCollection<VerificationRow>();

        public bool AllPassed
        {
            get => _allPassed;
            private set
            {
                if (SetProperty(ref _allPassed, value)) OnPropertyChanged(nameof(HeadlineText));
            }
        }

        /// <summary>요약 굵은 글자</summary>
        public string HeadlineText => AllPassed ? "모든 인터락 통과" : "인터락 미통과";

        /// <summary>원본 VerificationForm 제목 글자: "모든 인터락 통과" / "인터락 미통과: …"</summary>
        public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

        /// <summary>미통과 까닭만 (요약 아래 줄)</summary>
        public string ReasonsText { get => _reasonsText; private set => SetProperty(ref _reasonsText, value); }

        public string CountsText { get => _countsText; private set => SetProperty(ref _countsText, value); }

        public string CheckedAtText { get => _checkedAtText; private set => SetProperty(ref _checkedAtText, value); }

        /// <summary>알람 리셋으로 검증 경고를 숨긴 상태 (원본 VerificationAlarmDismissed)</summary>
        public bool IsWarningDismissed { get => _isWarningDismissed; private set => SetProperty(ref _isWarningDismissed, value); }

        public bool IsRunning => _timer != null && _timer.IsEnabled;

        public string CriteriaText =>
            "판정 기준: 압력은 목표가 1 Torr 미만이면 목표의 5배 이하, 그 밖에는 목표 ±15 Torr 이내. 온도는 목표 ±10 °C 이내.\n" +
            "챔버 PV는 공정을 시작하면 들어오며, 그 전에는 \"데이터 없음\"으로 보이고 인터락 판정에서 빠집니다.";

        public ICommand RecheckCommand { get; }

        /// <summary>탭이 보이기 시작했다: 원본처럼 한 번 검사해 로그를 남기고, 1초 갱신을 시작한다.</summary>
        public void OnShown()
        {
            _engine()?.RunVerificationChecks();
            Refresh();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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

        private void Recheck()
        {
            _engine()?.RunVerificationChecks();
            Refresh();
        }

        /// <summary>목록과 요약을 다시 판정한다 (로그 없음)</summary>
        public void Refresh()
        {
            var engine = _engine();
            if (engine == null) return;

            IReadOnlyList<InterlockCheck> checks = engine.BuildVerificationChecklist();
            bool sameShape = Rows.Count == checks.Count && Rows.Select(r => r.Name).SequenceEqual(checks.Select(c => c.Name));
            if (sameShape)
            {
                for (int i = 0; i < checks.Count; i++) Rows[i].Update(checks[i]);
            }
            else
            {
                Rows.Clear();
                foreach (var c in checks) Rows.Add(new VerificationRow(c));
            }

            bool ok = engine.EvaluateInterlocks(out var reasons);
            AllPassed = ok;
            SummaryText = ok ? "모든 인터락 통과" : $"인터락 미통과: {reasons}";
            ReasonsText = ok ? "운전을 시작할 수 있는 상태입니다." : reasons;
            int pass = checks.Count(c => c.State == InterlockCheckState.Pass);
            int fail = checks.Count(c => c.State == InterlockCheckState.Fail);
            int noData = checks.Count(c => c.State == InterlockCheckState.NoData);
            CountsText = noData > 0 ? $"통과 {pass} · 실패 {fail} · 데이터 없음 {noData}" : $"통과 {pass} · 실패 {fail}";
            CheckedAtText = $"{_now():HH:mm:ss} 기준";
            IsWarningDismissed = !ok && engine.ViewModel.VerificationAlarmDismissed;
        }
    }
}
