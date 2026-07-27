using System.Globalization;

namespace MuMiClick;

internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _window;
    public event Action<int>? Pressed;
    public HotkeyManager(IntPtr window) => _window = window;
    public void Register(UserSettings s)
    {
        foreach (var (id, text) in new[] { (1, s.RecordHotkey), (2, s.PlayHotkey), (3, s.PauseHotkey), (4, s.StopHotkey) })
        {
            NativeMethods.UnregisterHotKey(_window, id);
            var (mods, key) = Parse(text);
            if (!NativeMethods.RegisterHotKey(_window, id, mods | NativeMethods.MOD_NOREPEAT, key)) throw new InvalidOperationException($"단축키 '{text}'를 등록할 수 없습니다. 다른 프로그램이 사용 중일 수 있습니다.");
        }
    }
    public bool Handle(IntPtr msg, IntPtr wParam) { if (msg.ToInt32() != NativeMethods.WM_HOTKEY) return false; Pressed?.Invoke(wParam.ToInt32()); return true; }
    public static (uint Modifiers, uint Key) Parse(string text)
    {
        uint m = 0, key = 0;
        foreach (var p in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) m |= NativeMethods.MOD_CONTROL;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) m |= NativeMethods.MOD_ALT;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) m |= NativeMethods.MOD_SHIFT;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase)) m |= NativeMethods.MOD_WIN;
            else if (p.Length == 1) key = char.ToUpperInvariant(p[0]);
            else if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[1..], CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
            else if (Enum.TryParse<ConsoleKey>(p, true, out var ck)) key = (uint)ck;
            else throw new FormatException("알 수 없는 키: " + p);
        }
        if (key == 0) throw new FormatException("단축키에 F키 또는 문자 키가 필요합니다.");
        return (m, key);
    }
    public void Dispose() { for (int i = 1; i <= 4; i++) NativeMethods.UnregisterHotKey(_window, i); }
}
