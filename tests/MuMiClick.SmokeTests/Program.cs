using MuMiClick;
using System.Text.Json;

static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

Check(new UserSettings().StopOnPhysicalInput, "실제 입력 시 중지 기본 활성화");
Check(new UserSettings().Language == "auto", "첫 실행 언어는 OS 자동 감지");
Check(LocalizationService.Resolve("ko-KR") == "ko-KR" && LocalizationService.Resolve("en-US") == "en-US", "한국어/영어 언어 선택 해석");
var (mods, key) = HotkeyManager.Parse("F7");
Check(mods == 0 && key == 0x76, "단일 키 긴급 중지 단축키 파싱");
var doc = new MacroDocument { CoordinateMode = CoordinateMode.TargetWindow, TargetWindow = new TargetWindowInfo { Title = "테스트", ClassName = "Notepad", ProcessName = "notepad", ProcessId = 42 }, Events = [new MacroEvent { TimeMs = 0, Kind = MacroEventKind.KeyDown, ScanCode = 0x1E }, new MacroEvent { TimeMs = 17, Kind = MacroEventKind.KeyUp, ScanCode = 0x1E }, new MacroEvent { TimeMs = 30, Kind = MacroEventKind.WaitForSaveDialog, TimeoutMs = 15000 }] };
var restored = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(doc))!;
Check(restored.Events.Count == 3 && restored.Events[1].Kind == MacroEventKind.KeyUp && restored.TargetWindow?.Title == "테스트", "매크로 JSON 왕복 및 KeyDown/KeyUp 보존");
Check(restored.Events[2].Kind == MacroEventKind.WaitForSaveDialog && restored.Events[2].TimeoutMs == 15000, "저장 창 대기 이벤트 JSON 보존");
Check(restored.Events.SequenceEqual(restored.Events.OrderBy(e => e.TimeMs)), "재생용 타임스탬프 정렬");
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
