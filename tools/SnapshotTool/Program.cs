using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SemiconductorUi.Controls;
using SemiconductorUi.Hardware;
using SemiconductorUi.Wpf.Controls;
using SemiconductorUi.Wpf.Rendering;
using SemiconductorUi.Wpf.Services;
using SemiconductorUi.Wpf.ViewModels;

namespace SnapshotTool
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "snapshots";
            Directory.CreateDirectory(outDir);

            var app = new SemiconductorUi.Wpf.App();
            app.InitializeComponent(); // 테마 리소스만 불러온다 (OnStartup은 Run을 부르지 않으므로 실행되지 않음)

            int failures = 0;
            failures += Try(() => SnapCanvas(Path.Combine(outDir, "canvas-idle-1000x560.png"), 1000, 560, Idle()));
            failures += Try(() => SnapCanvas(Path.Combine(outDir, "canvas-transfer-1400x720.png"), 1400, 720, Transfer()));
            failures += Try(() => SnapCanvas(Path.Combine(outDir, "canvas-narrow-820x560.png"), 820, 560, Transfer()));
            failures += Try(() => SnapCanvas(Path.Combine(outDir, "canvas-foup-1400x720.png"), 1400, 720, PickFromFoup()));
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "main-window-1600x900.png"), 0));
            // 시뮬레이션 공정을 돌린 중간 화면 (공정 엔진이 그림을 움직이는지 확인)
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "main-window-running-1600x900.png"), 75));
            // 챔버/FOUP 상세 창 (공정 중)
            failures += Try(() => SnapUnitDetail(outDir, 75, "PMA", "PMB", "FOUP A", "FOUP B"));
            failures += Try(() => SnapConfig(Path.Combine(outDir, "config.png")));
            // 설정 백업 · 복원 창: 복원 예약이 있고, 수동 백업에서 두 항목을 되돌릴지 묻는 중
            failures += Try(() => SnapSettingsBackup(Path.Combine(outDir, "settings-backup.png")));
            failures += Try(() => SnapRecipeManager(Path.Combine(outDir, "recipe-manager.png")));
            failures += Try(() => SnapUserManagement(Path.Combine(outDir, "user-management.png")));
            failures += Try(() => SnapTeaching(outDir));
            // 추이 화면: 1초 간격 시각으로 150틱 수집
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "trend-1600x900.png"), 150,
                vm =>
                {
                    var t = new DateTime(2026, 11, 4, 9, 0, 0);
                    vm.Engine.TrendClock = () => t = t.AddSeconds(1);
                    vm.Engine.StartTrendCapture();
                },
                vm => { vm.CurrentPage = NavPage.Trend; vm.Trend.Units[2].IsChecked = false; }));
            // 알람 이력 창, 상단 바 알람 수 표시
            failures += Try(() => SnapAlarmHistory(Path.Combine(outDir, "alarm-history.png")));
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "main-window-alarms-1600x900.png"), 0, null,
                vm => { vm.UseAlarmHistory(SampleAlarmHistory()); vm.AddLog("PMA 온도 이탈(알람): PV=31.0°C, SV=23.0°C (Δ=8.0°C)", "ALARM"); }));
            // 보고서 화면: 공정 중 시험 알람을 하나 넣은 로그
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "report-1600x900.png"), 75, null,
                vm =>
                {
                    foreach (var unit in new[] { "PMA", "PMB", "PMC" })
                    {
                        if (vm.Monitoring.CanInjectTestAlarm(unit)) { vm.Monitoring.InjectTestAlarm(unit); break; }
                    }
                    vm.CurrentPage = NavPage.Report;
                }));
            // 보고서 → 변경 이력 탭: 티칭·레시피·임계값·사용자 예시 기록
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "change-history-1600x900.png"), 0, null,
                vm =>
                {
                    SeedChangeHistory();
                    vm.CurrentPage = NavPage.Report;
                    vm.CurrentReportTab = ReportTab.Changes;
                }));
            // 보고서 → 웨이퍼 이력 탭: 진행 중 2장, 완료 4장, 중단 1장. 레시피보다 오래 걸린 웨이퍼를 고른 모습
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "wafer-history-1600x900.png"), 0, null,
                vm =>
                {
                    vm.Engine.WaferHistory = SeedWaferHistory();
                    vm.CurrentPage = NavPage.Report;
                    vm.CurrentReportTab = ReportTab.Wafers;
                    vm.WaferHistoryView.SelectedRow = vm.WaferHistoryView.Rows.First(r => r.BState == WaferStepState.Over);
                }));
            // 유지보수 화면: 시뮬레이션 모드라 EtherCAT 미연결 상태
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "maintenance-1600x900.png"), 0, null,
                vm => vm.CurrentPage = NavPage.Maintenance));
            // 시스템 화면: 공정 중 (시뮬레이션이라 EtherCAT 미연결)
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "system-info-1600x900.png"), 75, null,
                vm => vm.CurrentPage = NavPage.System));
            // I/O 모니터 별도 창: 연결된 장비의 값, 방금 바뀐 점 강조
            failures += Try(() => SnapIoMonitor(Path.Combine(outDir, "io-monitor.png")));
            // 유지보수 카운터 탭: 예시 횟수 (곧 점검 2개, 점검 필요 2개)
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "maintenance-counters-1600x900.png"), 0, null,
                vm =>
                {
                    var usage = new SemiconductorUi.Hardware.HardwareUsageCounters();
                    vm.StartMaintenanceCounters(usage, NewTempFolder("counters-main"));
                    SeedCounters(vm.MaintenanceCounters, usage);
                    vm.Maintenance.CurrentTab = MaintenanceTab.Counters;
                    vm.CurrentPage = NavPage.Maintenance;
                }));
            // 유지보수 카운터 별도 창: 배기 점검 완료를 묻는 중
            failures += Try(() => SnapCounterWindow(Path.Combine(outDir, "maintenance-counters-window.png")));
            Console.WriteLine(failures == 0 ? "OK" : $"{failures} snapshot(s) failed");
            return failures == 0 ? 0 : 1;
        }

        private static int Try(Action a)
        {
            try { a(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static EquipmentScene Idle()
        {
            var s = new EquipmentScene();
            s.LampYellow = Color.FromRgb(218, 165, 32);
            s.FoupA.WaferCount = 5; s.FoupA.Capacity = 5;
            s.FoupB.WaferCount = 0; s.FoupB.Capacity = 5;
            return s;
        }

        /// <summary>FOUP A에서 웨이퍼를 꺼내는 순간: 블레이드가 로드포트 판 아래로 들어가 보여야 한다.</summary>
        private static EquipmentScene PickFromFoup()
        {
            var s = Transfer();
            s.Chambers[EquipmentRegion.ChamberB].ValveOpen = 0;
            s.TmTarget = EquipmentRegion.FoupA;
            s.TmCarrying = false;
            s.TmExtension = 1.3;
            return s;
        }

        private static EquipmentScene Transfer()
        {
            var s = Idle();
            s.LampYellow = Palette.LampOff;
            s.LampGreen = Color.FromRgb(50, 205, 50);
            s.FoupA.WaferCount = 3;
            s.FoupB.WaferCount = 1;
            s.FoupB.Mounted = true;
            s.Chambers[EquipmentRegion.ChamberA].HasWafer = true;
            s.Chambers[EquipmentRegion.ChamberA].WaferColor = Color.FromRgb(210, 230, 255);
            s.Chambers[EquipmentRegion.ChamberA].LampColor = Color.FromRgb(34, 139, 34);
            s.Chambers[EquipmentRegion.ChamberB].ValveOpen = 1;
            s.TmTarget = EquipmentRegion.ChamberB;
            s.TmCarrying = true;
            s.TmExtension = 1.3;
            s.SideView = new TmSideViewState
            {
                Target = EquipmentRegion.ChamberB,
                Height = 1156931,
                Extension = 1,
                VacuumOn = true,
                WaferOnBlade = true,
            };
            return s;
        }

        private static void SnapCanvas(string path, int w, int h, EquipmentScene scene)
        {
            var canvas = new EquipmentCanvas { Scene = scene, Width = w, Height = h };
            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
            canvas.UpdateLayout();
            Pump(3000); // TM 회전(가속/감속)과 전진 애니메이션이 끝나도록
            canvas.UpdateLayout();
            Save(canvas, w, h, path);
        }

        private static MainViewModel CreateMain(int simulationTicks, Action<MainViewModel> beforeTicks = null)
        {
            var vm = new MainViewModel(new AutoDialogs(), new SemiconductorUi.Repositories.UserRepositoryImpl(Path.GetTempFileName()), new OfflineDevice(), new SemiconductorUi.Services.LoggerService(null));
            if (simulationTicks > 0)
            {
                vm.State.IsLoggedIn = true;
                vm.State.CurrentUser = "operator";
                vm.State.CurrentRole = "운영자";
                // 첫 레시피를 적용해 환경 표에 SV가 나오게 한다
                var first = SemiconductorUi.Repositories.RecipeRepository.LoadAll().OrderBy(r => r.Name).First();
                vm.Monitoring.SelectedRecipeName = first.Name;
                vm.Monitoring.ApplyRecipeCommand.Execute(null);
                vm.Engine.ToggleFoupMountButton();
                vm.Engine.WaferLoadingButton();
                vm.Engine.StartButton();
                vm.Simulation.StopTimer(); // 실제 타이머 대신 직접 틱을 넣는다
                beforeTicks?.Invoke(vm);
                for (int i = 0; i < simulationTicks; i++) vm.Engine.OnSimulationTick();
            }
            return vm;
        }

        private static void SnapUnitDetail(string outDir, int simulationTicks, params string[] unitKeys)
        {
            var vm = CreateMain(simulationTicks);
            foreach (var key in unitKeys)
            {
                vm.Monitoring.UnitDetail.Show(key);
                var window = new SemiconductorUi.Wpf.Views.UnitDetailWindow();
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                var host = new Border { Background = window.Background, Child = root, DataContext = vm.Monitoring.UnitDetail, Width = window.Width };
                TextElement.SetFontFamily(host, window.FontFamily);
                TextElement.SetFontSize(host, window.FontSize);
                foreach (var k in window.Resources.Keys) host.Resources[k] = window.Resources[k];
                // 바인딩과 목록 칸이 만들어진 뒤의 높이로 다시 잰다
                host.Measure(new Size(window.Width, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                Pump(300);
                host.InvalidateMeasure();
                host.Measure(new Size(window.Width, double.PositiveInfinity));
                var size = host.DesiredSize;
                host.Arrange(new Rect(size));
                host.UpdateLayout();
                string name = key.Replace(" ", "").ToLowerInvariant();
                Save(host, (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), Path.Combine(outDir, $"unit-detail-{name}.png"));
                window.ForceClose();
            }
        }

        /// <summary>임시 폴더에 기록하는 알람 이력: 비상정지·서보 알람은 해제되고 챔버 환경 알람 둘이 남은 모습</summary>
        private static SemiconductorUi.Services.AlarmHistoryService SampleAlarmHistory()
        {
            var folder = Path.Combine(Path.GetTempPath(), "SnapshotAlarms_" + Guid.NewGuid().ToString("N"));
            var service = new SemiconductorUi.Services.AlarmHistoryService(
                new SemiconductorUi.Services.DailyLogFileWriter(folder, 0,
                    SemiconductorUi.Services.AlarmHistoryService.FilePrefix, SemiconductorUi.Services.AlarmHistoryService.FileHeader));
            service.Raise(SemiconductorUi.Services.AlarmCodes.EmergencyStop, "장비", "긴급 정지 버튼");
            service.Raise(SemiconductorUi.Services.AlarmCodes.ServoFaultAxis1, "TM", "Axis1(상하) 상태워드 0x0218");
            service.ClearEmergencyStopGroup("operator");
            service.Raise(SemiconductorUi.Services.AlarmCodes.ForEnvironment("PMA", SemiconductorUi.Services.AlarmCodes.EnvTemperature), "Chamber A", "PV=31.0°C, SV=23.0°C (Δ=8.0°C)");
            service.Raise(SemiconductorUi.Services.AlarmCodes.ForEnvironment("PMB", SemiconductorUi.Services.AlarmCodes.EnvGasLeakO2), "Chamber B", "PV=6.2 sccm (SV=0)");
            return service;
        }

        private static void SnapAlarmHistory(string path)
        {
            var model = new AlarmHistoryViewModel(SampleAlarmHistory(), _ => { });
            model.Open();
            SnapDialog(new SemiconductorUi.Wpf.Views.AlarmHistoryWindow(), model, path);
        }

        private static void SnapConfig(string path)
        {
            var model = new ConfigViewModel(new AutoDialogs(), ConfigViewModel.CreateDefault(), t => true);
            // 경고가 알람보다 큰 칸이 빨갛게 보이는지
            model.Gas.Fields[0].Value = 20;
            SnapDialog(new SemiconductorUi.Wpf.Views.ConfigWindow(model), model, path);
        }

        private static void SnapUserManagement(string path)
        {
            var repo = new SemiconductorUi.Repositories.UserRepositoryImpl(Path.GetTempFileName());
            var users = repo.EnsureSeedDefaults();
            users.Add(new SemiconductorUi.Models.User { Username = "operator1", Password = SemiconductorUi.PasswordHelper.HashPassword("x"), Role = "작업자" });
            repo.SaveAll(users);
            var model = new UserManagementViewModel(new AutoDialogs(), repo, "admin");
            model.Selected = model.Users.FirstOrDefault(u => u.Username == "operator1");
            SnapDialog(new SemiconductorUi.Wpf.Views.UserManagementWindow(model), model, path);
        }

        private static void SnapRecipeManager(string path)
        {
            var model = new RecipeManagerViewModel(new AutoDialogs(), SemiconductorUi.Repositories.RecipeRepository.LoadAll, list => { });
            model.Selected = model.Items.FirstOrDefault();
            SnapDialog(new SemiconductorUi.Wpf.Views.RecipeManagerWindow(model), model, path);
        }

        /// <summary>모달 창을 띄우지 않고 내용만 떼어 창 크기로 찍는다.</summary>
        private static void SnapDialog(Window window, object model, string path)
        {
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            var host = new Border { Background = window.Background, Child = root, DataContext = model };
            TextElement.SetFontFamily(host, window.FontFamily);
            TextElement.SetFontSize(host, window.FontSize);
            TextElement.SetForeground(host, window.Foreground);
            foreach (var k in window.Resources.Keys) host.Resources[k] = window.Resources[k];
            int w = (int)window.Width, h = (int)window.Height - 32;
            host.Measure(new Size(w, h));
            host.Arrange(new Rect(0, 0, w, h));
            Pump(300);
            host.UpdateLayout();
            Save(host, w, h, path);
            window.Close();
        }

        private static void SnapSettingsBackup(string path)
        {
            var store = new SampleBackupStore();
            var model = new SettingsBackupViewModel(() => true, () => "admin", () => true, (m, l) => { }, f => { }, store,
                () => new DateTime(2026, 11, 11, 9, 10, 0));
            model.RefreshAll();
            model.SelectedBackup = model.Backups.First(b => b.IsManual);
            foreach (var c in model.Choices.Where(c => c.Key == "Teaching" || c.Key == "Recipes")) c.IsChecked = true;
            model.RestoreCommand.Execute(null);
            SnapDialog(new SemiconductorUi.Wpf.Views.SettingsBackupWindow(model), model, path);
        }

        /// <summary>파일을 건드리지 않는 예시 백업 목록</summary>
        private sealed class SampleBackupStore : ISettingsBackupStore
        {
            private static readonly string Root = @"C:\Users\operator\Documents\SemiconductorUi_Logs\Backups";

            public string BackupRoot => Root;
            public System.Collections.Generic.IReadOnlyList<SemiconductorUi.Services.SettingsBackupComponent> Components =>
                SemiconductorUi.Services.SettingsBackup.Components;
            public int AutoBackupKeepCount => 30;

            public System.Collections.Generic.List<SemiconductorUi.Services.SettingsBackupInfo> ListBackups()
            {
                var all = Components.Select(c => c.Key).ToList();
                SemiconductorUi.Services.SettingsBackupInfo B(DateTime t, string kind, string user, string note, System.Collections.Generic.List<string> keys) =>
                    new SemiconductorUi.Services.SettingsBackupInfo
                    {
                        Folder = Path.Combine(Root, t.ToString("yyyyMMdd_HHmmss") + "_" + kind),
                        Time = t, Kind = kind, User = user, Note = note, Components = keys
                    };
                return new System.Collections.Generic.List<SemiconductorUi.Services.SettingsBackupInfo>
                {
                    B(new DateTime(2026, 11, 11, 8, 58, 2), "자동", "(자동)", "프로그램 시작", all),
                    B(new DateTime(2026, 11, 10, 17, 42, 10), "수동", "admin", "레시피 B 시간 바꾸기 전", all),
                    B(new DateTime(2026, 11, 10, 8, 55, 31), "자동", "(자동)", "프로그램 시작", all),
                    B(new DateTime(2026, 11, 9, 8, 57, 12), "복원전", "admin", "복원 전 지금 설정", all),
                    B(new DateTime(2026, 11, 9, 8, 50, 40), "자동", "(자동)", "프로그램 시작", all.Where(k => k != "Users").ToList()),
                };
            }

            public SemiconductorUi.Services.SettingsBackupInfo CreateBackup(string kind, string user, string note, DateTime now) =>
                throw new NotSupportedException();

            public SemiconductorUi.Services.PendingRestoreInfo GetPendingRestore() => new SemiconductorUi.Services.PendingRestoreInfo
            {
                BackupFolder = Path.Combine(Root, "20261110_174210_수동"),
                Components = new System.Collections.Generic.List<string> { "Thresholds" },
                User = "admin",
                RequestedAt = new DateTime(2026, 11, 11, 9, 5, 0),
            };

            public void ScheduleRestore(SemiconductorUi.Services.SettingsBackupInfo backup, System.Collections.Generic.IEnumerable<string> componentKeys, string user, DateTime now) { }
            public bool CancelPendingRestore() => false;
        }

        /// <summary>임시 폴더에 변경 이력 예시를 쓴다 (오늘, 어제, 사흘 전)</summary>
        /// <summary>임시 폴더에 웨이퍼 이력 예시를 쓴다: 끝난 작업 2개(완료 4장, 중단 1장)와 지금 진행 중인 작업 (2장)</summary>
        private static SemiconductorUi.Services.WaferHistoryService SeedWaferHistory()
        {
            var now = DateTime.Now;
            var t = now.AddHours(-2).AddMinutes(-10);
            var h = new SemiconductorUi.Services.WaferHistoryService(NewTempFolder("wafers"), (m, l) => { }, () => t);

            void Wafer(int id, params (string Chamber, int Set, double Actual)[] steps)
            {
                h.NotePickup(id, "FOUP A"); t = t.AddSeconds(9);
                foreach (var s in steps)
                {
                    h.NoteDropoff(id, s.Chamber); t = t.AddSeconds(4);
                    h.NoteProcessStart(id, s.Chamber, s.Set); t = t.AddSeconds(s.Actual);
                    h.NoteProcessEnd(id, s.Chamber); t = t.AddSeconds(5);
                    h.NotePickup(id, s.Chamber); t = t.AddSeconds(9);
                }
                h.NoteDropoff(id, "FOUP B"); t = t.AddSeconds(3);
            }

            // 작업 1: 병렬 모드 3장. 3번은 Chamber B에서 알람 홀드로 31초 더 걸림
            h.LotStarted(3, "PR-Coat-200", false, true, "kim");
            Wafer(1, ("A", 60, 60.2), ("B", 120, 120.4));
            Wafer(2, ("A", 60, 60.1), ("C", 120, 120.3));
            Wafer(3, ("A", 60, 60.3), ("B", 120, 151.0));
            h.LotFinished();
            t = t.AddMinutes(25);

            // 작업 2: 2차 노광 2장. 2번이 Chamber A 공정 중 비상정지
            h.LotStarted(2, "PR-Expose", true, true, "lee");
            Wafer(1, ("A", 45, 45.2), ("B", 90, 90.1), ("C", 90, 90.4));
            h.NotePickup(2, "FOUP A"); t = t.AddSeconds(9);
            h.NoteDropoff(2, "A"); t = t.AddSeconds(4);
            h.NoteProcessStart(2, "A", 45); t = t.AddSeconds(21);
            h.LotAborted("비상정지. 알람 리셋으로 해제한 뒤 서보 ON과 원점복귀를 다시 하세요.");

            // 작업 3 (진행 중): 1번은 Chamber B 공정 중, 2번은 Chamber A 공정 중
            t = now.AddSeconds(-120);
            h.LotStarted(4, "PR-Coat-200", false, true, "kim");
            h.NotePickup(1, "FOUP A"); t = t.AddSeconds(9);
            h.NoteDropoff(1, "A"); t = t.AddSeconds(4);
            h.NoteProcessStart(1, "A", 60); t = t.AddSeconds(60.2);
            h.NoteProcessEnd(1, "A"); t = t.AddSeconds(5);
            h.NotePickup(1, "A"); t = t.AddSeconds(9);
            h.NoteDropoff(1, "B"); t = t.AddSeconds(4);
            h.NoteProcessStart(1, "B", 120); t = t.AddSeconds(2);
            h.NotePickup(2, "FOUP A"); t = t.AddSeconds(9);
            h.NoteDropoff(2, "A"); t = t.AddSeconds(4);
            h.NoteProcessStart(2, "A", 60);
            return h;
        }

        private static void SeedChangeHistory()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SnapshotTool_changes_" + Guid.NewGuid().ToString("N"));
            string user = "admin", role = "관리자";
            SemiconductorUi.Services.ChangeHistory.Initialize(folder, () => user, () => role, (m, l) => { });
            var today = DateTime.Today;

            var teachBefore = SemiconductorUi.Models.TeachingPositionsData.CreateDefault();
            var teachAfter = SemiconductorUi.Models.TeachingPositionsData.CreateDefault();
            teachAfter.ChamberB_X = -190910;
            teachAfter.FoupA_LandY[2] = 1432120;
            Record(SemiconductorUi.Services.ChangeHistory.CategoryTeaching,
                SemiconductorUi.Services.ChangeHistory.Diff("", teachBefore, teachAfter, SemiconductorUi.Services.ChangeHistory.TeachingLabel),
                today.AddDays(-3).AddHours(14).AddMinutes(5));

            var users = new[] { new SemiconductorUi.Models.User { Username = "admin", Password = "x", Role = "관리자" } };
            var usersAfter = new[]
            {
                users[0],
                new SemiconductorUi.Models.User { Username = "kim", Password = "y", Role = "작업자" },
            };
            Record(SemiconductorUi.Services.ChangeHistory.CategoryUser,
                SemiconductorUi.Services.ChangeHistory.DiffUsers(users, usersAfter), today.AddDays(-1).AddHours(9).AddMinutes(12));

            var envBefore = new SemiconductorUi.Models.EnvThresholdSnapshot { TempWarn = 5, TempAlarm = 10, GasWarn = 5, GasAlarm = 10 };
            var envAfter = new SemiconductorUi.Models.EnvThresholdSnapshot { TempWarn = 4, TempAlarm = 8, GasWarn = 5, GasAlarm = 10 };
            Record(SemiconductorUi.Services.ChangeHistory.CategoryThreshold,
                SemiconductorUi.Services.ChangeHistory.Diff("", envBefore, envAfter, SemiconductorUi.Services.ChangeHistory.ThresholdLabel),
                today.AddHours(10).AddMinutes(40));

            var recipes = SemiconductorUi.Repositories.RecipeRepository.LoadAll();
            if (recipes != null && recipes.Count > 0)
            {
                var before = recipes.Take(1).ToList();
                var changed = SemiconductorUi.Repositories.RecipeRepository.LoadAll().Take(1).ToList();
                changed[0].DurB += 5;
                changed[0].PMB.T += 10;
                Record(SemiconductorUi.Services.ChangeHistory.CategoryRecipe,
                    SemiconductorUi.Services.ChangeHistory.DiffRecipes(before, changed), today.AddHours(13).AddMinutes(22));
            }

            void Record(string category, System.Collections.Generic.List<SemiconductorUi.Services.ChangeEntry> entries, DateTime at) =>
                SemiconductorUi.Services.ChangeHistory.Record(category, entries, at);
        }

        private static void SnapIoMonitor(string path)
        {
            var device = new DemoIoDevice();
            var t = new DateTime(2026, 11, 11, 14, 30, 12, 400);
            var model = new IoMonitorViewModel(() => device, () => true, () => t);
            model.Refresh();
            // 실린더 전진: 전진 출력 ON, 후진 출력 OFF, 0.6초 뒤 전진 센서 ON → 바뀐 네 점이 강조된다
            device.Outputs[12] = true;
            device.Outputs[13] = false;
            device.Inputs[12] = false;
            device.Inputs[13] = true;
            t = t.AddSeconds(0.6);
            model.Refresh();
            SnapDialog(new SemiconductorUi.Wpf.Views.IoMonitorWindow(model), model, path);
        }

        private static string NewTempFolder(string name)
        {
            string folder = Path.Combine(Path.GetTempPath(), "SnapshotTool_" + name + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>일주일 쓴 모습: 주기 설정, Chamber C 도어 점검 한 번, 배기·Chamber B 도어 주기 도달</summary>
        private static void SeedCounters(SemiconductorUi.Services.MaintenanceCounterService service, SemiconductorUi.Hardware.HardwareUsageCounters usage)
        {
            var t0 = new DateTime(2026, 11, 4, 9, 0, 0);
            foreach (var (key, limit) in new[] { ("CylinderExtend", 20000L), ("Vacuum", 20000L), ("Exhaust", 10000L),
                         ("DoorA", 5000L), ("DoorB", 5000L), ("DoorC", 5000L), ("Axis1", 50000L) })
            {
                service.SetLimit(key, limit, "admin", t0);
            }

            void Outputs(int numbering, int count)
            {
                for (int i = 0; i < count; i++) usage.NoteOutput(numbering, false, true);
            }

            Outputs(11, 4100);
            service.Collect(t0.AddDays(2));
            service.MarkServiced("DoorC", "kim", "도어 O-링 교체", t0.AddDays(2).AddHours(1));

            Outputs(12, 18412);
            Outputs(14, 18390);
            Outputs(15, 10250);
            Outputs(5, 3120);
            Outputs(8, 5004);
            Outputs(11, 2870);
            for (int i = 0; i < 31544; i++) usage.NoteAxisCommand(1);
            for (int i = 0; i < 31560; i++) usage.NoteAxisCommand(2);
            service.Collect(t0.AddDays(7));
        }

        private static void SnapCounterWindow(string path)
        {
            var usage = new SemiconductorUi.Hardware.HardwareUsageCounters();
            var service = new SemiconductorUi.Services.MaintenanceCounterService(NewTempFolder("counters-window"), usage, (m, l) => { });
            SeedCounters(service, usage);
            var model = new MaintenanceCounterViewModel(() => service, () => true, () => "kim", () => false, f => { },
                () => new DateTime(2026, 11, 11, 15, 20, 0));
            model.RefreshAll();
            model.SelectedRow = model.Rows.First(r => r.Key == "Exhaust");
            model.Note = "배기 밸브 시트 점검";
            model.AskServicedCommand.Execute(null);
            SnapDialog(new SemiconductorUi.Wpf.Views.MaintenanceCounterWindow(model), model, path);
            service.Dispose();
        }

        private sealed class NullTeachingStore : ITeachingStore
        {
            public void SaveTeaching(SemiconductorUi.Models.TeachingPositionsData data) { }
            public SemiconductorUi.Repositories.ServoSpeedSettings SaveSpeeds(SemiconductorUi.Repositories.ServoSpeedSettings speeds) => speeds;
        }

        /// <summary>티칭 위치 창: 연결된 상태에서 몇 칸을 바꾼 모습 (Chamber A, FOUP A)</summary>
        private static void SnapTeaching(string outDir)
        {
            foreach (var (section, file) in new[] { ("Chamber A", "teaching-chamber.png"), ("FOUP A", "teaching-foup.png") })
            {
                var model = new TeachingPositionViewModel(new AutoDialogs(),
                    SemiconductorUi.Models.TeachingPositionsData.CreateDefault(),
                    SemiconductorUi.Repositories.ServoSpeedSettings.CreateDefault(),
                    () => new ServoPositionReading("-59120", "806874"), new NullTeachingStore());
                model.ChamberA.Groups[0].Fields[0].Text = "-59120";
                model.FoupA.Levels[1].Raise.Text = "700000";
                model.FoupA.Levels[3].Land.Text = "21l9399";
                model.SelectedSection = model.Sections.First(s => s.Title == section);
                SnapDialog(new SemiconductorUi.Wpf.Views.TeachingPositionWindow(model), model, Path.Combine(outDir, file));
            }
        }

        private static void SnapMainWindow(string path, int simulationTicks, Action<MainViewModel> beforeTicks = null, Action<MainViewModel> afterTicks = null)
        {
            var vm = CreateMain(simulationTicks, beforeTicks);
            afterTicks?.Invoke(vm);
            // 창을 띄우면 CI 화면 크기로 줄어들기 때문에, 띄우지 않고 내용만 떼어 1600x900으로 배치한다.
            var window = new SemiconductorUi.Wpf.MainWindow();
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            var host = new Border { Background = window.Background, Child = root, DataContext = vm };
            TextElement.SetFontFamily(host, window.FontFamily);
            foreach (var key in window.Resources.Keys) host.Resources[key] = window.Resources[key];
            host.Measure(new Size(1600, 900));
            host.Arrange(new Rect(0, 0, 1600, 900));
            host.UpdateLayout();
            Pump(simulationTicks > 0 ? 3000 : 800); // TM 애니메이션이 자리 잡도록
            host.UpdateLayout();
            Save(host, 1600, 900, path);
            window.Close();
        }

        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Save(Visual visual, int w, int h, string path)
        {
            var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("saved " + path);
        }
    }

    /// <summary>창을 띄우지 않는 대화상자: 확인은 예, 웨이퍼 수는 최대값</summary>
    internal sealed class AutoDialogs : IDialogService
    {
        public void Info(string message, string title = "안내") { }
        public void Warn(string message, string title = "경고") { }
        public void Error(string message, string title = "오류") { }
        public bool Confirm(string message, string title = "확인") => true;
        public bool Confirm(string message, string title, bool defaultNo) => true;
        public int? PromptWaferCount(int current, int min, int max) => max;
        public void ShowUserManagement(UserManagementViewModel model) { }
        public bool TryLogin(out string username, out string password) { username = null; password = null; return false; }
        public void ShowUnitDetail(UnitDetailViewModel detail) { }
        public void ShowConfig(ConfigViewModel model) { }
        public void ShowRecipeManager(RecipeManagerViewModel model) { }
        public void ShowTeachingPositions(TeachingPositionViewModel model) { }
        public void ShowSettingsBackup(SettingsBackupViewModel model) { }
        public void ShowIoMonitor(IoMonitorViewModel model) { }
        public void ShowMaintenanceCounters(MaintenanceCounterViewModel model) { }
        public void ShowAlarmHistory(AlarmHistoryViewModel model) { }
        public void ShowWaferHistory(WaferHistoryViewModel model) { }
    }

    /// <summary>I/O 모니터 스냅샷용: 공정 대기 중 모습 (녹색 램프, 도어 닫힘, 실린더 후진, 진공 ON)</summary>
    internal sealed class DemoIoDevice : OfflineDevice
    {
        public DemoIoDevice()
        {
            foreach (int i in new[] { 2, 3, 4, 7, 10, 13, 14 }) Outputs[i] = true;
            Inputs[12] = true;
        }

        public bool[] Inputs { get; } = new bool[IoMonitorSnapshot.PointCount];
        public bool[] Outputs { get; } = new bool[IoMonitorSnapshot.PointCount];
        public override bool Digital_Input(int numbering) => Inputs[numbering];
        public override bool GetDigitalOutput(int numbering) => Outputs[numbering];
        // Axis1: 서보 ON + 목표 도달, Axis2: 서보 ON + 경고 비트
        public override ushort GetStatusWord(int axis) => axis == 1 ? (ushort)0x1637 : (ushort)0x02B7;
        public override string Axis1_is_PosData() => "806874";
        public override string Axis2_is_PosData() => "-59120";
    }

    /// <summary>연결되지 않은 장비 (스냅샷용)</summary>
    internal class OfflineDevice : IEthercatDevice
    {
        public bool CIFX_50RE_Connect() => false;
        public void CIFX_50RE_Disconnect() { }
        public void ReadData_Send_Start(int interval) { }
        public void ReadData_Timer_Start() { }
        public virtual bool Digital_Input(int numbering) => false;
        public virtual bool GetDigitalOutput(int numbering) => false;
        public void Digital_Output(int numbering, bool value) { }
        public void Axis1_ON() { }
        public void Axis1_OFF() { }
        public virtual string Axis1_is_PosData() => "-";
        public void Axis1_UD_Config_Update(long a, long d, long m, long v) { }
        public void Axis1_UD_POS_Update(long position) { }
        public void Axis1_UD_Move_Send() { }
        public void Axis1_UD_Homming() { }
        public bool Axis1_Status(string status) => false;
        public void Axis2_ON() { }
        public void Axis2_OFF() { }
        public virtual string Axis2_is_PosData() => "-";
        public void Axis2_LR_Config_Update(long a, long d, long m, long v) { }
        public void Axis2_LR_POS_Update(long position) { }
        public void Axis2_LR_Move_Send() { }
        public void Axis2_LR_Homming() { }
        public bool Axis2_Status(string status) => false;
        public bool IsAxisFault(int axis) => false;
        public virtual ushort GetStatusWord(int axis) => 0;
        public void QuickStopAll() { }
        public void ResetAxisFault(int axis) { }
        public EthercatCommunicationStatus CheckCommunication() => null;
        public bool IsAxisOperationEnabled(int axis) => false;
        public bool IsEmergencyStopped => false;
        public void EmergencyStop() { }
        public void ClearEmergencyStop() { }
        public void ThrowIfEmergencyStopped() { }
        public IDisposable BeginOperation() => null;
    }
}
