using MuMiClick;
using System.Text.Json;

static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

Check(new UserSettings().StopOnPhysicalInput, "실제 입력 시 중지 기본 활성화");
Check(new UserSettings().Language == "auto", "첫 실행 언어는 OS 자동 감지");
Check(!new UserSettings().DarkMode, "다크 모드 기본 비활성화");
Check(MainWindow.ParsePlaybackSpeed("20.0x") == 20d && double.IsPositiveInfinity(MainWindow.ParsePlaybackSpeed("Max")), "20배속 및 최고속 선택 해석");
var playbackCallerThread = Environment.CurrentManagedThreadId;
var playbackWorkerThread = playbackCallerThread;
await MainWindow.RunPlaybackWorkerAsync(() => { playbackWorkerThread = Environment.CurrentManagedThreadId; return Task.CompletedTask; });
Check(playbackWorkerThread != playbackCallerThread, "최고속 재생은 키보드 메시지 UI 스레드와 분리");
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
    Variables = new(StringComparer.OrdinalIgnoreCase) { ["fileName"] = "sample-001.png", ["backupName"] = "sample-002.png" },
    VariableGroups = new(StringComparer.OrdinalIgnoreCase) { ["imageNames"] = ["fileName", "backupName"] },
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
        },
        new MacroEvent { TimeMs = 30, Kind = MacroEventKind.SetClipboardVariable, RandomFromVariableGroup = true, VariableGroupName = "imageNames" },
        new MacroEvent
        {
            TimeMs = 40, Kind = MacroEventKind.WaitForWindowText, WaitText = "Download complete", TextExactMatch = false, TimeoutMs = 60000,
            TextTargetWindow = new TargetWindowInfo { Title = "Downloads - Chrome", ClassName = "Chrome_WidgetWin_1", ProcessName = "chrome", ProcessId = 99 }
        }
    ]
};
var restoredWorkflow = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(workflowDoc))!;
Check(restoredWorkflow.FormatVersion == 4 && MacroDocument.IsSupportedFormatVersion(1) && MacroDocument.IsSupportedFormatVersion(3), "format v4 save and v1-v3 backward compatibility");
Check(restoredWorkflow.Variables["fileName"] == "sample-001.png" && restoredWorkflow.Events[0].VariableName == "fileName", "variable and clipboard event JSON round trip");
Check(restoredWorkflow.Events[1].Branches?.Count == 2 && restoredWorkflow.Events[1].Branches![0].Events.Count == 1, "random action bundles JSON round trip");
Check(restoredWorkflow.VariableGroups["imageNames"].Count == 2 && restoredWorkflow.Events[2].RandomFromVariableGroup, "variable group and random clipboard event JSON round trip");
Check(restoredWorkflow.Events[3].Kind == MacroEventKind.WaitForWindowText && restoredWorkflow.Events[3].TextTargetWindow?.ProcessName == "chrome" && restoredWorkflow.Events[3].TimeoutMs == 60000, "window text trigger JSON round trip");
Check(WindowTextLocator.MatchesText("Image download complete", "download complete", false) && !WindowTextLocator.MatchesText("Image download complete", "download complete", true), "window text contains and exact matching");
Exception? accessibilityFailure = null;
var accessibilityReady = new ManualResetEventSlim();
System.Windows.Forms.Form? accessibilityForm = null;
IntPtr accessibilityHandle = IntPtr.Zero;
var accessibilityThread = new Thread(() =>
{
    try
    {
        accessibilityForm = new System.Windows.Forms.Form { Text = "MuMiClick Accessibility Probe", Width = 320, Height = 120, Left = -32000, Top = -32000, ShowInTaskbar = false };
        accessibilityForm.Controls.Add(new System.Windows.Forms.Label { Text = "Chrome text trigger ready", AutoSize = true, Left = 12, Top = 12 });
        accessibilityForm.Shown += (_, _) => { accessibilityHandle = accessibilityForm.Handle; accessibilityReady.Set(); };
        System.Windows.Forms.Application.Run(accessibilityForm);
    }
    catch (Exception ex) { accessibilityFailure = ex; accessibilityReady.Set(); }
});
accessibilityThread.SetApartmentState(ApartmentState.STA);
accessibilityThread.Start();
Check(accessibilityReady.Wait(TimeSpan.FromSeconds(5)) && accessibilityFailure is null, "accessibility probe window starts");
Check(WindowTextLocator.ContainsText(accessibilityHandle, "text trigger ready", false), "Win32 accessibility tree text detection");
accessibilityForm?.BeginInvoke(accessibilityForm.Close);
accessibilityThread.Join();
if (accessibilityFailure is not null) throw accessibilityFailure;
var chromeWindow = WindowLocator.GetWindows().FirstOrDefault(x => x.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase));
if (chromeWindow is not null) Check(WindowTextLocator.CanInspect(WindowLocator.Find(chromeWindow)), "Chrome accessibility root connection");
else Console.WriteLine("SKIP: Chrome accessibility root connection (no Chrome window open)");
var variableRandom = new Random(1234);
var randomVariableChoices = Enumerable.Range(0, 100).Select(_ => MacroPlayer.ChooseVariableName(restoredWorkflow.Events[2], restoredWorkflow.Variables, restoredWorkflow.VariableGroups, variableRandom)).ToHashSet();
Check(randomVariableChoices.SetEquals(new[] { "fileName", "backupName" }), "random clipboard selection uses every member of the selected group");
var chosen = MacroPlayer.ChooseBranch(restoredWorkflow.Events[1].Branches, new Random(1234));
Check(chosen is not null && restoredWorkflow.Events[1].Branches!.Contains(chosen), "random selection stays inside action bundles");
var moveGroup = EventListItem.Group(new MacroEvent { Kind = MacroEventKind.MouseMove, X = 10, Y = 20 });
moveGroup.AddToGroup(new MacroEvent { Kind = MacroEventKind.MouseMove, X = 30, Y = 40 });
Check(moveGroup.IsMouseMoveGroup && moveGroup.SourceEvents.Count == 2, "연속 마우스 이동 표시 그룹 구성");
var highlightedRow = EventListItem.Single(new MacroEvent { Kind = MacroEventKind.KeyDown, VirtualKey = 0x41, ScanCode = 0x1E });
highlightedRow.IsActive = true;
Check(highlightedRow.IsActive, "재생 중인 이벤트 행 강조 상태");
var clickPair = ActionEditorWindow.CreateMouseClickEvents(100, 320, 240, MouseButtonKind.Left, 45);
var dragMove = new MacroEvent { TimeMs = 120, Kind = MacroEventKind.MouseMove, X = 350, Y = 260 };
var clickSequence = new List<MacroEvent> { clickPair[0], dragMove, clickPair[1] };
var logicalClick = ActionEditService.FindLogicalAction(clickSequence, clickPair[0]);
Check(logicalClick.Count == 2 && logicalClick[0].Kind == MacroEventKind.MouseDown && logicalClick[1].Kind == MacroEventKind.MouseUp, "mouse Down/Up logical action pairing across movement");
var keyPair = ActionEditorWindow.CreateKeyPressEvents(100, 0x41, 45);
var convertedSequence = ActionEditService.ReplaceLogicalAction(clickSequence, logicalClick, keyPair);
Check(convertedSequence.Count == 3 && convertedSequence[0].Kind == MacroEventKind.KeyDown && ReferenceEquals(convertedSequence[1], dragMove) && convertedSequence[2].Kind == MacroEventKind.KeyUp, "mouse click converts to balanced key press while preserving intermediate actions");
Check(keyPair[0].ScanCode != 0 && keyPair[0].ScanCode == keyPair[1].ScanCode && keyPair[0].TimeMs < keyPair[1].TimeMs, "edited key press uses matching scan-code Down/Up events");
var controlDown = new MacroEvent { TimeMs = 200, Kind = MacroEventKind.KeyDown, VirtualKey = 0xA2, ScanCode = 0x1D };
var letterPair = ActionEditorWindow.CreateKeyPressEvents(210, 0x41, 20);
var controlUp = new MacroEvent { TimeMs = 240, Kind = MacroEventKind.KeyUp, VirtualKey = 0xA2, ScanCode = 0x1D };
var shortcutSequence = new List<MacroEvent> { controlDown, letterPair[0], letterPair[1], controlUp };
var logicalModifier = ActionEditService.FindLogicalAction(shortcutSequence, controlUp);
Check(logicalModifier.Count == 2 && ReferenceEquals(logicalModifier[0], controlDown) && ReferenceEquals(logicalModifier[1], controlUp), "modifier Down/Up pairing across shortcut actions");
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
        var variablesWindow = new VariablesWindow(workflowDoc.Variables, workflowDoc.VariableGroups, true);
        Check(variablesWindow.Title == "Macro Variables", "variable editor XAML construction");
        variablesWindow.Close();
        var clipboardWindow = new ClipboardEventWindow(workflowDoc.Variables.Keys, workflowDoc.VariableGroups.Keys, true);
        Check(clipboardWindow.Title == "Insert Clipboard Event", "clipboard event option window XAML construction");
        clipboardWindow.Close();
        var textTriggerWindow = new TextTriggerWindow(workflowDoc.Events[3].TextTargetWindow, true);
        Check(textTriggerWindow.Title == "Wait for Window Text", "window text trigger editor XAML construction");
        textTriggerWindow.Close();
        var actionEditorWindow = new ActionEditorWindow(clickPair[0], clickPair[1], true);
        Check(actionEditorWindow.Title == "Edit Action", "action editor XAML construction");
        actionEditorWindow.Close();
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
