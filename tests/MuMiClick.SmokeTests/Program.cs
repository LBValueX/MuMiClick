using MuMiClick;
using System.Text.Json;

static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

Check(new UserSettings().StopOnPhysicalInput, "실제 입력 시 중지 기본 활성화");
Check(new UserSettings().Language == "auto", "첫 실행 언어는 OS 자동 감지");
Check(!new UserSettings().DarkMode, "다크 모드 기본 비활성화");
Check(LocalizationService.Resolve("ko-KR") == "ko-KR" && LocalizationService.Resolve("en-US") == "en-US", "한국어/영어 언어 선택 해석");
var (mods, key) = HotkeyManager.Parse("F7");
Check(mods == 0 && key == 0x76, "단일 키 긴급 중지 단축키 파싱");
var doc = new MacroDocument { CoordinateMode = CoordinateMode.TargetWindow, TargetWindow = new TargetWindowInfo { Title = "테스트", ClassName = "Notepad", ProcessName = "notepad", ProcessId = 42 }, Events = [new MacroEvent { TimeMs = 0, Kind = MacroEventKind.KeyDown, ScanCode = 0x1E }, new MacroEvent { TimeMs = 17, Kind = MacroEventKind.KeyUp, ScanCode = 0x1E }, new MacroEvent { TimeMs = 30, Kind = MacroEventKind.WaitForSaveDialog, TimeoutMs = 15000 }] };
var restored = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(doc))!;
Check(restored.Events.Count == 3 && restored.Events[1].Kind == MacroEventKind.KeyUp && restored.TargetWindow?.Title == "테스트", "매크로 JSON 왕복 및 KeyDown/KeyUp 보존");
Check(restored.Events[2].Kind == MacroEventKind.WaitForSaveDialog && restored.Events[2].TimeoutMs == 15000, "저장 창 대기 이벤트 JSON 보존");
Check(restored.Events.SequenceEqual(restored.Events.OrderBy(e => e.TimeMs)), "재생용 타임스탬프 정렬");
var workflowDoc = new MacroDocument
{
    Variables = new(StringComparer.OrdinalIgnoreCase) { ["fileName"] = "sample-001.png" },
    Events =
    [
        new MacroEvent { TimeMs = 10, Kind = MacroEventKind.SetClipboardVariable, VariableName = "fileName" },
        new MacroEvent
        {
            TimeMs = 20, Kind = MacroEventKind.RandomBranch, Branches =
            [
                new MacroBranch { Name = "Left", Events = [new MacroEvent { TimeMs = 0, Kind = MacroEventKind.MouseDown, Button = MouseButtonKind.Left, X = 10, Y = 20 }] },
                new MacroBranch { Name = "Right", Events = [new MacroEvent { TimeMs = 0, Kind = MacroEventKind.MouseDown, Button = MouseButtonKind.Right, X = 30, Y = 40 }] }
            ]
        }
    ]
};
var restoredWorkflow = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(workflowDoc))!;
Check(restoredWorkflow.FormatVersion == 2 && MacroDocument.IsSupportedFormatVersion(1), "format v2 save and v1 backward compatibility");
Check(restoredWorkflow.Variables["fileName"] == "sample-001.png" && restoredWorkflow.Events[0].VariableName == "fileName", "variable and clipboard event JSON round trip");
Check(restoredWorkflow.Events[1].Branches?.Count == 2 && restoredWorkflow.Events[1].Branches![0].Events.Count == 1, "random action bundles JSON round trip");
var chosen = MacroPlayer.ChooseBranch(restoredWorkflow.Events[1].Branches, new Random(1234));
Check(chosen is not null && restoredWorkflow.Events[1].Branches!.Contains(chosen), "random selection stays inside action bundles");
var moveGroup = EventListItem.Group(new MacroEvent { Kind = MacroEventKind.MouseMove, X = 10, Y = 20 });
moveGroup.AddToGroup(new MacroEvent { Kind = MacroEventKind.MouseMove, X = 30, Y = 40 });
Check(moveGroup.IsMouseMoveGroup && moveGroup.SourceEvents.Count == 2, "연속 마우스 이동 표시 그룹 구성");
Exception? localizationFailure = null;
var localizationThread = new Thread(() =>
{
    try
    {
        var app = new System.Windows.Application();
        LocalizationService.Apply("en-US");
        Check(LocalizationService.T("SettingsTitle") == "MuMiClick Settings", "영문 UI 리소스 로드");
        var settingsWindow = new SettingsWindow(new UserSettings());
        Check(settingsWindow.Title == "MuMiClick Settings", "설정 팝업 XAML 생성");
        var variablesWindow = new VariablesWindow(workflowDoc.Variables, true);
        Check(variablesWindow.Title == "Macro Variables", "variable editor XAML construction");
        variablesWindow.Close();
        var branchSource = workflowDoc.Events[1].Branches![0].Events;
        var branchWindow = new RandomBranchWindow(branchSource, branchSource, restoredWorkflow.Events[1].Branches,
            CoordinateMode.AbsoluteScreen, null, true);
        Check(branchWindow.Title == "Edit Random Branches", "action bundle editor XAML construction");
        branchWindow.Close();
        var targetList = new System.Windows.Controls.ListBox { ItemsSource = new[] { new TargetWindowInfo { ProcessName = "notepad", Title = "Notes" } } };
        Check(targetList.Items.Count == 1 && targetList.Items[0] is TargetWindowInfo, "대상 창 목록은 창 정보 객체로 바인딩");
        var probeTitle = "MuMiClick WindowLocator Smoke Probe";
        var probe = new System.Windows.Window { Title = probeTitle, Width = 1, Height = 1, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false };
        probe.Show();
        Check(WindowLocator.GetWindows().Any(x => x.Title == probeTitle), "대상 창 열거는 표시 가능한 창을 찾음");
        probe.Close();
        ThemeService.Apply(settingsWindow, true);
        var darkBackground = (System.Windows.Media.SolidColorBrush)settingsWindow.Resources["AppBackgroundBrush"];
        Check(darkBackground.Color == System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x27), "다크 모드 팔레트 적용");
        settingsWindow.Close();
        app.Shutdown();
    }
    catch (Exception ex) { localizationFailure = ex; }
});
localizationThread.SetApartmentState(ApartmentState.STA);
localizationThread.Start();
localizationThread.Join();
if (localizationFailure is not null) throw localizationFailure;
Console.WriteLine("모든 자동 스모크 테스트 통과");
