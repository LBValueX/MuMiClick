using System.Text.Json.Serialization;

namespace MuMiClick;

public enum MacroEventKind { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp, WaitForSaveDialog }
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
        _ => Kind.ToString()
    };
    private string Describe() => Kind switch
    {
        MacroEventKind.MouseMove => $"({X}, {Y})",
        MacroEventKind.MouseDown or MacroEventKind.MouseUp => $"{Button} ({X}, {Y})",
        MacroEventKind.MouseWheel => $"{Delta} ({X}, {Y})",
        MacroEventKind.WaitForSaveDialog => $"{LocalizationService.T("Maximum")} {Math.Max(1, TimeoutMs / 1000)} {LocalizationService.T("Second")}",
        _ => $"VK {VirtualKey} / Scan {ScanCode}" + (Extended ? $" ({LocalizationService.T("ExtendedKey")})" : "")
    };
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
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "새 매크로";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public CoordinateMode CoordinateMode { get; set; }
    public TargetWindowInfo? TargetWindow { get; set; }
    public List<MacroEvent> Events { get; set; } = [];
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
