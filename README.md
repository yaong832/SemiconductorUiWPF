# Semiconductor UI (WPF)

[yaong832/semiconductorui](https://github.com/yaong832/semiconductorui) (WinForms, .NET Framework 4.7.2)의 WPF(.NET 8) 버전입니다.

## 구성

| 프로젝트 | 대상 | 설명 |
|---|---|---|
| `src/SemiconductorUi.Core` | net8.0 | UI 무관 로직: Hardware(EtherCAT/IEG3268), Controllers(TM 상태 머신, 챔버, 시뮬레이션), Repositories(XML), Models, Services, ViewModels |
| `src/SemiconductorUi.Wpf` | net8.0-windows | WPF 표현 계층 (MVVM) |
| `tests/SemiconductorUi.Core.Tests` | net8.0 | 원본 MSTest 테스트 이관 |

## 원본 대비 변경점 (Core)

- `System.Windows.Forms.Timer` → `ITickTimer` 인터페이스 (WPF는 `DispatcherTickTimer`)
- `System.Drawing.Color` → UI 독립 `ArgbColor` (WPF에서 `ToMediaColor()`/`ToBrush()`로 변환)
- `EquipmentRegion` 열거형을 Controls에서 Core로 이동 (네임스페이스 유지)
- `ExceptionHandler`의 MessageBox → `ExceptionHandler.ShowErrorMessage` 훅

## 빌드

```
dotnet build
dotnet test
```

실행은 Windows에서 `src/SemiconductorUi.Wpf`를 시작 프로젝트로 지정하세요.
