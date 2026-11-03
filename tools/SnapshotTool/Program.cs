using System;
using System.IO;
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
            failures += Try(() => SnapMainWindow(Path.Combine(outDir, "main-window-1600x900.png")));
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

        private static void SnapMainWindow(string path)
        {
            var vm = new MainViewModel(new DialogService(), new SemiconductorUi.Repositories.UserRepositoryImpl(Path.GetTempFileName()), new OfflineDevice());
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
            Pump(800);
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

    /// <summary>연결되지 않은 장비 (스냅샷용)</summary>
    internal sealed class OfflineDevice : IEthercatDevice
    {
        public bool CIFX_50RE_Connect() => false;
        public void CIFX_50RE_Disconnect() { }
        public void ReadData_Send_Start(int interval) { }
        public void ReadData_Timer_Start() { }
        public bool Digital_Input(int numbering) => false;
        public bool GetDigitalOutput(int numbering) => false;
        public void Digital_Output(int numbering, bool value) { }
        public void Axis1_ON() { }
        public void Axis1_OFF() { }
        public string Axis1_is_PosData() => "-";
        public void Axis1_UD_Config_Update(long a, long d, long m, long v) { }
        public void Axis1_UD_POS_Update(long position) { }
        public void Axis1_UD_Move_Send() { }
        public void Axis1_UD_Homming() { }
        public bool Axis1_Status(string status) => false;
        public void Axis2_ON() { }
        public void Axis2_OFF() { }
        public string Axis2_is_PosData() => "-";
        public void Axis2_LR_Config_Update(long a, long d, long m, long v) { }
        public void Axis2_LR_POS_Update(long position) { }
        public void Axis2_LR_Move_Send() { }
        public void Axis2_LR_Homming() { }
        public bool Axis2_Status(string status) => false;
        public bool IsAxisFault(int axis) => false;
        public ushort GetStatusWord(int axis) => 0;
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
