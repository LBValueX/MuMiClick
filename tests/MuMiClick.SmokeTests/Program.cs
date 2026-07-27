using MuMiClick;
using System.Text.Json;

static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

var (mods, key) = HotkeyManager.Parse("F7");
Check(mods == 0 && key == 0x76, "단일 키 긴급 중지 단축키 파싱");
var doc = new MacroDocument { CoordinateMode = CoordinateMode.TargetWindow, TargetWindow = new TargetWindowInfo { Title = "테스트", ClassName = "Notepad", ProcessName = "notepad", ProcessId = 42 }, Events = [new MacroEvent { TimeMs = 0, Kind = MacroEventKind.KeyDown, ScanCode = 0x1E }, new MacroEvent { TimeMs = 17, Kind = MacroEventKind.KeyUp, ScanCode = 0x1E }] };
var restored = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(doc))!;
Check(restored.Events.Count == 2 && restored.Events[1].Kind == MacroEventKind.KeyUp && restored.TargetWindow?.Title == "테스트", "매크로 JSON 왕복 및 KeyDown/KeyUp 보존");
Check(restored.Events.SequenceEqual(restored.Events.OrderBy(e => e.TimeMs)), "재생용 타임스탬프 정렬");
Console.WriteLine("모든 자동 스모크 테스트 통과");
