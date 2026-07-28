using System.Globalization;

namespace MuMiClick;

internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _window;
    public event Action<int>? Pressed;
    public HotkeyManager(IntPtr window) => _window = window;

    public void Register(UserSettings settings)
    {
        var hotkeys = new[]
        {
            (Id: 1, Text: settings.RecordHotkey),
            (Id: 2, Text: settings.PlayHotkey),
            (Id: 3, Text: settings.PauseHotkey),
            (Id: 4, Text: settings.StopHotkey)
        };
        var parsed = hotkeys.Select(x => (x.Id, x.Text, Parsed: Parse(x.Text))).ToArray();

        foreach (var id in Enumerable.Range(1, 4)) NativeMethods.UnregisterHotKey(_window, id);
        foreach (var (id, text, value) in parsed)
        {
            if (NativeMethods.RegisterHotKey(_window, id, value.Modifiers | NativeMethods.MOD_NOREPEAT, value.Key)) continue;
            foreach (var registeredId in Enumerable.Range(1, 4)) NativeMethods.UnregisterHotKey(_window, registeredId);
            throw new InvalidOperationException(LocalizationService.F("HotkeyUnavailableFormat", text));
        }
    }

    public bool Handle(IntPtr msg, IntPtr wParam)
    {
        if (msg.ToInt32() != NativeMethods.WM_HOTKEY) return false;
        Pressed?.Invoke(wParam.ToInt32());
        return true;
    }

    public static (uint Modifiers, uint Key) Parse(string text)
    {
        uint modifiers = 0, key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_CONTROL;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_ALT;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_SHIFT;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_WIN;
            else if (part.Length == 1) key = char.ToUpperInvariant(part[0]);
            else if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[1..], CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
            else if (Enum.TryParse<ConsoleKey>(part, true, out var consoleKey)) key = (uint)consoleKey;
            else throw new FormatException(LocalizationService.F("UnknownKeyFormat", part));
        }
        if (key == 0) throw new FormatException(LocalizationService.T("HotkeyNeedsKey"));
        return (modifiers, key);
    }

    public void Dispose()
    {
        for (var id = 1; id <= 4; id++) NativeMethods.UnregisterHotKey(_window, id);
    }
}
