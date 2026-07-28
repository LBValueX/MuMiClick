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
    [JsonIgnore] public string Display => $"{TimeMs,7} ms  {Kind,-10} {Describe()}";
    private string Describe() => Kind switch
    {
        MacroEventKind.MouseMove => $"({X}, {Y})",
        MacroEventKind.MouseDown or MacroEventKind.MouseUp => $"{Button} ({X}, {Y})",
        MacroEventKind.MouseWheel => $"{Delta} ({X}, {Y})",
        MacroEventKind.WaitForSaveDialog => $"최대 {Math.Max(1, TimeoutMs / 1000)}초",
        _ => $"VK {VirtualKey} / Scan {ScanCode}" + (Extended ? " (확장)" : "")
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
    public string RecordHotkey { get; set; } = "F8";
    public string PlayHotkey { get; set; } = "F9";
    public string PauseHotkey { get; set; } = "F11";
    public string StopHotkey { get; set; } = "F7";
    public bool StopOnPhysicalInput { get; set; }
    public string? LastMacroPath { get; set; }
    public bool SimpleMode { get; set; }
    public bool StabilizeSaveDialog { get; set; } = true;
    public int SaveDialogTimeoutSeconds { get; set; } = 15;
}
