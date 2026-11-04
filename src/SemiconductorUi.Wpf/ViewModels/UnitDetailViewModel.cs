using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using SemiconductorUi.Process;
using SemiconductorUi.Wpf.Infrastructure;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 챔버/FOUP 상세 창 (원본 Forms.UnitDetailForm + Controls.UnitDetailPopup).
    /// 메인 화면을 잠그지 않는 창 하나를 다시 쓰며, 값은 화면 갱신 때마다 엔진에서 다시 채운다.
    /// 화면 표시만 하며 장비에는 아무 명령도 보내지 않는다.
    /// </summary>
    public sealed class UnitDetailViewModel : ObservableObject
    {
        private readonly Func<string, UnitDetailContent> _build;
        private string _unitKey;
        private UnitDetailContent _content = new UnitDetailContent();

        public UnitDetailViewModel(Func<string, UnitDetailContent> build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
        }

        /// <summary>지금 보여 주는 대상 (예: "PMA", "FOUP A"). 닫혀 있으면 null</summary>
        public string UnitKey
        {
            get => _unitKey;
            private set
            {
                if (SetProperty(ref _unitKey, value)) OnPropertyChanged(nameof(IsOpen));
            }
        }

        public bool IsOpen => !string.IsNullOrEmpty(_unitKey);

        /// <summary>창을 띄우거나, 떠 있으면 내용만 바꾼다 (자리는 그대로).</summary>
        public void Show(string unitKey)
        {
            UnitKey = unitKey;
            Refresh();
        }

        /// <summary>원본 CloseUnitDetail: 창은 없애지 않고 숨긴다.</summary>
        public void Close() => UnitKey = null;

        /// <summary>원본 RefreshUnitDetail (화면 갱신마다)</summary>
        public void Refresh()
        {
            if (!IsOpen) return;
            _content = _build(_unitKey) ?? new UnitDetailContent();
            var c = _content;

            WindowTitle = (string.IsNullOrWhiteSpace(c.Title) ? "장비" : c.Title) + " 상세";
            Info = c.Info.Select(kv => new InfoRow(kv.Key, kv.Value ?? "-")).ToList();
            Chips = c.Chips.Select(kv => new ChipItem(kv.Key, kv.Value)).ToList();
            EnvRows = c.Env?.Where(r => r != null && r.Length >= 4).Select(r => new EnvRow(r[0], r[1], r[2], r[3])).ToList();
            Slots = c.Slots?.Select(kv => new SlotItem(kv.Key, kv.Value)).ToList();
            OnPropertyChanged(string.Empty);
        }

        public string WindowTitle { get; private set; } = "장비 상세";
        public string Title => _content.Title;
        public string Status => _content.Status;
        public bool HasStatus => !string.IsNullOrEmpty(_content.Status);
        public bool Live => _content.Live;
        public Brush LampBrush => Tones.Lamp(_content.Tone);
        public Brush PillBackground => Tones.PillBack(_content.Tone);
        public Brush PillForeground => Tones.PillFore(_content.Tone);

        public IReadOnlyList<InfoRow> Info { get; private set; } = Array.Empty<InfoRow>();
        public IReadOnlyList<ChipItem> Chips { get; private set; } = Array.Empty<ChipItem>();
        public IReadOnlyList<EnvRow> EnvRows { get; private set; }
        public IReadOnlyList<SlotItem> Slots { get; private set; }
        public bool HasEnv => EnvRows != null;
        public bool HasSlots => EnvRows == null && Slots != null;

        public bool HasProgress => _content.Progress.HasValue;
        public int Progress => Math.Max(0, Math.Min(100, _content.Progress ?? 0));
        public string ProgressText => _content.ProgressText ?? "";

        public sealed class InfoRow
        {
            public InfoRow(string key, string value) { Key = key; Value = value; }
            public string Key { get; }
            public string Value { get; }
        }

        /// <summary>도어/램프/장착 칩. 켜짐이면 초록, 아니면 회색 (원본 UnitDetailPopup 칩 색)</summary>
        public sealed class ChipItem
        {
            public ChipItem(string text, bool active) { Text = text; Active = active; }
            public string Text { get; }
            public bool Active { get; }
            public Brush Background => Active ? Tones.ChipOnBack : Tones.SurfaceAlt;
            public Brush Foreground => Active ? Tones.Success : Tones.TextSecondary;
            public Brush BorderBrush => Active ? Tones.ChipOnBorder : Tones.Border;
        }

        public sealed class EnvRow
        {
            public EnvRow(string name, string pv, string sv, string unit) { Name = name; Pv = pv; Sv = sv; Unit = unit; }
            public string Name { get; }
            public string Pv { get; }
            public string Sv { get; }
            public string Unit { get; }
            public bool HasPv => Pv != "-";
            public Brush PvBrush => HasPv ? Tones.PvGreen : Tones.Disabled;
            public Brush SvBrush => Sv != "-" ? Tones.Primary : Tones.Disabled;
        }

        public sealed class SlotItem
        {
            public SlotItem(string text, bool hasWafer) { Text = text; HasWafer = hasWafer; }
            public string Text { get; }
            public bool HasWafer { get; }
            public Brush Background => HasWafer ? Tones.SlotOnBack : Tones.SurfaceAlt;
            public Brush BorderBrush => HasWafer ? Tones.SlotOnBorder : Tones.Border;
            public Brush Foreground => HasWafer ? Tones.Primary : Tones.TextSecondary;
            public Brush LampBrush => Tones.Lamp(HasWafer ? LampTone.Blue : LampTone.Off);
        }

        /// <summary>원본 UiTheme / UnitDetailPopup.ToneColors / LampButtonPainter 색</summary>
        public static class Tones
        {
            private static Brush B(byte r, byte g, byte b)
            {
                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                brush.Freeze();
                return brush;
            }

            public static readonly Brush Primary = B(0x2D, 0x62, 0xC4);
            public static readonly Brush Success = B(0x24, 0x96, 0x5A);
            public static readonly Brush Danger = B(0xCE, 0x38, 0x42);
            public static readonly Brush TextSecondary = B(0x64, 0x70, 0x80);
            public static readonly Brush SurfaceAlt = B(0xF2, 0xF5, 0xF9);
            public static readonly Brush Border = B(0xD0, 0xD7, 0xE1);
            public static readonly Brush ButtonFill = B(0xE8, 0xED, 0xF4);
            public static readonly Brush Disabled = B(0x96, 0xA0, 0xAE);
            public static readonly Brush PvGreen = B(28, 122, 73);
            public static readonly Brush ChipOnBack = B(226, 243, 234);
            public static readonly Brush ChipOnBorder = B(191, 227, 207);
            public static readonly Brush SlotOnBack = B(232, 239, 250);
            public static readonly Brush SlotOnBorder = B(160, 186, 230);

            private static readonly Brush LampGreen = Success;
            private static readonly Brush LampAmber = B(0xD6, 0x8A, 0x14);
            private static readonly Brush LampRed = Danger;
            private static readonly Brush LampBlue = Primary;
            private static readonly Brush LampOff = B(200, 207, 217);

            public static Brush Lamp(LampTone tone)
            {
                switch (tone)
                {
                    case LampTone.Green: return LampGreen;
                    case LampTone.Amber: return LampAmber;
                    case LampTone.Red: return LampRed;
                    case LampTone.Blue: return LampBlue;
                    default: return LampOff;
                }
            }

            private static readonly Brush PillGreenBack = B(226, 243, 234);
            private static readonly Brush PillAmberBack = B(251, 240, 220);
            private static readonly Brush PillAmberFore = B(154, 98, 8);
            private static readonly Brush PillRedBack = B(250, 228, 230);
            private static readonly Brush PillBlueBack = B(227, 236, 251);

            public static Brush PillBack(LampTone tone)
            {
                switch (tone)
                {
                    case LampTone.Green: return PillGreenBack;
                    case LampTone.Amber: return PillAmberBack;
                    case LampTone.Red: return PillRedBack;
                    case LampTone.Blue: return PillBlueBack;
                    default: return ButtonFill;
                }
            }

            public static Brush PillFore(LampTone tone)
            {
                switch (tone)
                {
                    case LampTone.Green: return Success;
                    case LampTone.Amber: return PillAmberFore;
                    case LampTone.Red: return Danger;
                    case LampTone.Blue: return Primary;
                    default: return TextSecondary;
                }
            }
        }
    }
}
