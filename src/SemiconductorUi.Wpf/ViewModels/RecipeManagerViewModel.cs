using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SemiconductorUi.Models;
using SemiconductorUi.Wpf.Infrastructure;
using SemiconductorUi.Wpf.Services;

namespace SemiconductorUi.Wpf.ViewModels
{
    /// <summary>
    /// 레시피 관리 화면 (원본 Forms.RecipeManagerForm). 왼쪽 목록, 오른쪽 편집(기본 정보 + 챔버별 공정 시간/환경/가스·RF).
    /// 저장하면 레시피 저장소 전체를 다시 쓰고 OnSavedAll로 알린다.
    /// </summary>
    public sealed class RecipeManagerViewModel : ObservableObject
    {
        private readonly IDialogService _dialogs;
        private readonly Action<List<RecipeSnapshot>> _saveAll;
        private readonly List<RecipeSnapshot> _recipes;
        private RecipeListItem _selected;

        /// <summary>저장/삭제 뒤 (원본 OnSavedAll: 메인 화면 레시피 목록 다시 읽기)</summary>
        public Action<List<RecipeSnapshot>> OnSavedAll;

        /// <summary>닫기 버튼 (창이 듣는다)</summary>
        public event EventHandler CloseRequested;

        public RecipeManagerViewModel(IDialogService dialogs, Func<List<RecipeSnapshot>> loadAll, Action<List<RecipeSnapshot>> saveAll)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _saveAll = saveAll ?? throw new ArgumentNullException(nameof(saveAll));
            try
            {
                _recipes = loadAll() ?? new List<RecipeSnapshot>();
            }
            catch
            {
                _recipes = new List<RecipeSnapshot>();
            }

            NewCommand = new RelayCommand(New);
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);
            CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            BindList();
        }

        public ObservableCollection<RecipeListItem> Items { get; } = new ObservableCollection<RecipeListItem>();

        public RecipeListItem Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                if (value != null) Editor.Fill(value.Recipe);
                RelayCommand.Refresh();
            }
        }

        /// <summary>오른쪽 편집 칸</summary>
        public RecipeEditor Editor { get; } = new RecipeEditor();

        public ICommand NewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CloseCommand { get; }

        private void BindList()
        {
            Items.Clear();
            foreach (var r in _recipes.OrderBy(x => x.Name ?? string.Empty))
            {
                Items.Add(new RecipeListItem(r));
            }
        }

        private void SelectByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            Selected = Items.FirstOrDefault(i => string.Equals(i.Recipe.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
        }

        private void New()
        {
            var r = new RecipeSnapshot { Name = "새 레시피" };
            _recipes.Add(r);
            BindList();
            Selected = Items.FirstOrDefault(i => ReferenceEquals(i.Recipe, r));
        }

        private void Save()
        {
            var target = _selected?.Recipe;
            if (target == null)
            {
                // 선택되지 않은 상태에서는 새로 생성하지 않고 막는다
                _dialogs.Info("수정할 레시피를 먼저 선택하세요.\r\n신규 레시피를 만들려면 '신규' 버튼을 사용하세요.", "안내");
                return;
            }

            Editor.ReadInto(target);
            // 필수값 검증: 이름 비어있으면 저장 불가
            if (string.IsNullOrWhiteSpace(target.Name))
            {
                _dialogs.Warn("레시피 이름을 입력하세요.", "안내");
                return;
            }

            _saveAll(_recipes);
            BindList();
            SelectByName(target.Name);
            OnSavedAll?.Invoke(_recipes);
            _dialogs.Info("저장/수정이 완료되었습니다.", "완료");
        }

        private void Delete()
        {
            var r = _selected?.Recipe;
            if (r == null) return;
            if (!_dialogs.Confirm($"'{r.Name}' 레시피를 삭제하시겠습니까?", "확인", true)) return;
            _recipes.Remove(r);
            _saveAll(_recipes);
            BindList();
            _selected = null;
            OnPropertyChanged(nameof(Selected));
            Editor.Clear();
            OnSavedAll?.Invoke(_recipes);
            RelayCommand.Refresh();
        }

        /// <summary>목록 한 줄: 위에는 레시피 이름(" / " 앞부분)을 굵게, 아래에는 나머지 설명을 흐리게 (원본 listRecipes_DrawItem)</summary>
        public sealed class RecipeListItem
        {
            public RecipeListItem(RecipeSnapshot recipe)
            {
                Recipe = recipe;
                string text = recipe.Name ?? "(이름없음)";
                int cut = text.IndexOf(" / ", StringComparison.Ordinal);
                Name = cut >= 0 ? text.Substring(0, cut) : text;
                Detail = cut >= 0 ? text.Substring(cut + 3).Replace(" / ", " · ") : string.Empty;
            }

            public RecipeSnapshot Recipe { get; }
            public string Name { get; }
            public string Detail { get; }
            public bool HasDetail => Detail.Length > 0;
        }
    }

    /// <summary>레시피 편집 칸. 값 범위는 원본 NumericUpDown의 Minimum/Maximum과 같다.</summary>
    public sealed class RecipeEditor : ObservableObject
    {
        private string _name = string.Empty;
        private int _waferCount = 1;
        private bool _secondExposure;

        public RecipeEditor()
        {
            ChamberA = new ChamberRecipeEditor("Chamber A");
            ChamberB = new ChamberRecipeEditor("Chamber B");
            ChamberC = new ChamberRecipeEditor("Chamber C");
        }

        public string Name { get => _name; set => SetProperty(ref _name, value ?? string.Empty); }
        public int WaferCount { get => _waferCount; set => SetProperty(ref _waferCount, Clamp(value, 1, 50)); }
        public bool SecondExposure { get => _secondExposure; set => SetProperty(ref _secondExposure, value); }

        public ChamberRecipeEditor ChamberA { get; }
        public ChamberRecipeEditor ChamberB { get; }
        public ChamberRecipeEditor ChamberC { get; }
        public IReadOnlyList<ChamberRecipeEditor> Chambers => new[] { ChamberA, ChamberB, ChamberC };

        public void Fill(RecipeSnapshot r)
        {
            Name = r.Name ?? string.Empty;
            WaferCount = r.WaferCount;
            SecondExposure = r.SecondExposure;
            ChamberA.Fill(r.DurA, r.PMA, r.GasRfPMA);
            ChamberB.Fill(r.DurB, r.PMB, r.GasRfPMB);
            ChamberC.Fill(r.DurC, r.PMC, r.GasRfPMC);
        }

        public void ReadInto(RecipeSnapshot r)
        {
            r.Name = Name?.Trim();
            r.WaferCount = WaferCount;
            r.SecondExposure = SecondExposure;
            r.DurA = ChamberA.Duration;
            r.DurB = ChamberB.Duration;
            r.DurC = ChamberC.Duration;
            r.PMA = ChamberA.ReadEnv(r.PMA);
            r.PMB = ChamberB.ReadEnv(r.PMB);
            r.PMC = ChamberC.ReadEnv(r.PMC);
            r.GasRfPMA = ChamberA.ReadGas(r.GasRfPMA);
            r.GasRfPMB = ChamberB.ReadGas(r.GasRfPMB);
            r.GasRfPMC = ChamberC.ReadGas(r.GasRfPMC);
        }

        public void Clear()
        {
            Name = string.Empty;
            WaferCount = 1;
            SecondExposure = false;
            foreach (var c in Chambers) c.Clear();
        }

        internal static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        internal static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }

    /// <summary>Chamber 한 대의 설정 카드: 공정 시간, 환경(온도/압력/습도), 가스·RF</summary>
    public sealed class ChamberRecipeEditor : ObservableObject
    {
        private int _duration = 1;
        private double _temp, _press, _hum, _nf3, _o2, _cf4, _rf;

        public ChamberRecipeEditor(string title) { Title = title; }

        public string Title { get; }
        public int Duration { get => _duration; set => SetProperty(ref _duration, RecipeEditor.Clamp(value, 1, 600)); }
        public double Temperature { get => _temp; set => SetProperty(ref _temp, RecipeEditor.Clamp(value, 0, 400)); }
        public double Pressure { get => _press; set => SetProperty(ref _press, RecipeEditor.Clamp(value, 0, 10000)); }
        public double Humidity { get => _hum; set => SetProperty(ref _hum, RecipeEditor.Clamp(value, 0, 100)); }
        public double NF3 { get => _nf3; set => SetProperty(ref _nf3, RecipeEditor.Clamp(value, 0, 5000)); }
        public double O2 { get => _o2; set => SetProperty(ref _o2, RecipeEditor.Clamp(value, 0, 5000)); }
        public double CF4 { get => _cf4; set => SetProperty(ref _cf4, RecipeEditor.Clamp(value, 0, 5000)); }
        public double RF { get => _rf; set => SetProperty(ref _rf, RecipeEditor.Clamp(value, 0, 50000)); }

        public void Fill(int duration, RecipeSnapshot.Triple env, RecipeSnapshot.GasRf gas)
        {
            Duration = duration;
            Temperature = env?.T ?? 0; Pressure = env?.P ?? 0; Humidity = env?.H ?? 0;
            // PMB/PMC 가스RF는 존재하지 않을 수 있어 0으로 대체
            NF3 = gas?.NF3 ?? 0; O2 = gas?.O2 ?? 0; CF4 = gas?.CF4 ?? 0; RF = gas?.RF ?? 0;
        }

        public RecipeSnapshot.Triple ReadEnv(RecipeSnapshot.Triple env)
        {
            env = env ?? new RecipeSnapshot.Triple();
            env.T = Temperature; env.P = Pressure; env.H = Humidity;
            return env;
        }

        public RecipeSnapshot.GasRf ReadGas(RecipeSnapshot.GasRf gas)
        {
            gas = gas ?? new RecipeSnapshot.GasRf();
            gas.NF3 = NF3; gas.O2 = O2; gas.CF4 = CF4; gas.RF = RF;
            return gas;
        }

        public void Clear()
        {
            Duration = 1;
            Temperature = Pressure = Humidity = NF3 = O2 = CF4 = RF = 0;
        }
    }
}
