using System.Text.Json.Serialization;

namespace MuMiClick;

public enum MacroEventKind { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp, WaitForSaveDialog, SetClipboardVariable, RandomBranch, WaitForWindowText }
public enum MouseButtonKind { Left, Right, Middle, X1, X2 }
public enum CoordinateMode { AbsoluteScreen, TargetWindow }

public sealed class MacroEvent
{
    public long TimeMs { get; set; }
    public MacroEventKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Delta { get; set; }
    public MouseButtonKind? Button { get; set; }
    public uint VirtualKey { get; set; }
    public uint ScanCode { get; set; }
    public bool Extended { get; set; }
    public int TimeoutMs { get; set; }
    public string? VariableName { get; set; }
    public bool RandomFromVariableGroup { get; set; }
    public string? VariableGroupName { get; set; }
    public string? WaitText { get; set; }
    public bool TextExactMatch { get; set; }
    public TargetWindowInfo? TextTargetWindow { get; set; }
    public List<MacroBranch>? Branches { get; set; }
    [JsonIgnore] public string Display => $"{TimeMs,7} ms  {KindName(),-14} {Describe()}";
    private string KindName() => Kind switch
    {
        MacroEventKind.MouseMove => LocalizationService.T("MouseMove"),
        MacroEventKind.MouseDown => LocalizationService.T("MouseDown"),
        MacroEventKind.MouseUp => LocalizationService.T("MouseUp"),
        MacroEventKind.MouseWheel => LocalizationService.T("MouseWheel"),
        MacroEventKind.KeyDown => LocalizationService.T("KeyDown"),
        MacroEventKind.KeyUp => LocalizationService.T("KeyUp"),
        MacroEventKind.WaitForSaveDialog => LocalizationService.T("WaitForSaveDialog"),
        MacroEventKind.SetClipboardVariable => LocalizationService.T("SetClipboardVariable"),
        MacroEventKind.RandomBranch => LocalizationService.T("RandomBranch"),
        MacroEventKind.WaitForWindowText => LocalizationService.T("WaitForWindowText"),
        _ => Kind.ToString()
    };
    private string Describe() => Kind switch
    {
        MacroEventKind.MouseMove => $"({X}, {Y})",
        MacroEventKind.MouseDown or MacroEventKind.MouseUp => $"{Button} ({X}, {Y})",
        MacroEventKind.MouseWheel => $"{Delta} ({X}, {Y})",
        MacroEventKind.WaitForSaveDialog => $"{LocalizationService.T("Maximum")} {Math.Max(1, TimeoutMs / 1000)} {LocalizationService.T("Second")}",
        MacroEventKind.SetClipboardVariable => RandomFromVariableGroup
            ? LocalizationService.F("RandomVariableGroupFormat", VariableGroupName ?? LocalizationService.T("VariableGroupNotSelected"))
            : VariableName ?? LocalizationService.T("VariableNotSelected"),
        MacroEventKind.RandomBranch => LocalizationService.F("BranchCountFormat", Branches?.Count ?? 0),
        MacroEventKind.WaitForWindowText => LocalizationService.F("WaitTextDisplayFormat", WaitText ?? "", TimeoutMs <= 0 ? LocalizationService.T("Unlimited") : LocalizationService.F("SecondsFormat", Math.Max(1, TimeoutMs / 1000))),
        _ => $"VK {VirtualKey} / Scan {ScanCode}" + (Extended ? $" ({LocalizationService.T("ExtendedKey")})" : "")
    };
}

public sealed class MacroVariable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Group { get; set; } = "";
}

public sealed class MacroBranch
{
    public string Name { get; set; } = "";
    public CoordinateMode CoordinateMode { get; set; }
    public TargetWindowInfo? TargetWindow { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MacroEvent> Events { get; set; } = [];
    [JsonIgnore] public string Display => LocalizationService.F("BranchDisplayFormat", Name, Events.Count);
    public override string ToString() => Display;
}

public sealed class TargetWindowInfo
{
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public override string ToString() => $"{ProcessName} — {Title}";
}

public sealed class MacroDocument
{
    public const int CurrentFormatVersion = 4;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "새 매크로";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public CoordinateMode CoordinateMode { get; set; }
    public TargetWindowInfo? TargetWindow { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> VariableGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MacroEvent> Events { get; set; } = [];
    public static bool IsSupportedFormatVersion(int version) => version is >= 1 and <= CurrentFormatVersion;
}

public sealed class UserSettings
{
    public int SettingsVersion { get; set; } = 2;
    public string RecordHotkey { get; set; } = "F8";
    public string PlayHotkey { get; set; } = "F9";
    public string PauseHotkey { get; set; } = "F11";
    public string StopHotkey { get; set; } = "F7";
    public string Language { get; set; } = "auto";
    public bool DarkMode { get; set; }
    public bool StopOnPhysicalInput { get; set; } = true;
    public string? LastMacroPath { get; set; }
    public bool SimpleMode { get; set; }
    public bool StabilizeSaveDialog { get; set; } = true;
    public int SaveDialogTimeoutSeconds { get; set; } = 15;
    public bool InstantMouseMovement { get; set; }
    public int InstantMouseDelayMs { get; set; } = 30;
}
