using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MuMiClick;

/// <summary>Dedicated low-level hook. The delegates are rooted for the whole hook lifetime.</summary>
internal sealed class InputHook : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardProc, _mouseProc;
    private IntPtr _keyboardHook, _mouseHook;
    public bool Recording { get; set; }
    public event Action<MacroEvent>? Recorded;
    public event Action? PhysicalInput;
    public const ulong InjectionMarker = 0x4D754D69; // MuMi (safe on both x86/x64)
    // Reserved application controls must never become part of a newly recorded macro.
    private static readonly HashSet<uint> ReservedHotkeys = [0x76, 0x77, 0x78, 0x7A]; // F7, F8, F9, F11
    private readonly Stopwatch _clock = new();
    private int _lastMoveX, _lastMoveY;
    private long _lastMoveMs;

    public InputHook()
    {
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }
    public void Install()
    {
        if (_keyboardHook != IntPtr.Zero) return;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero) throw new InvalidOperationException("전역 입력 훅을 설치할 수 없습니다.");
    }
    public void StartRecording()
    {
        _clock.Restart(); _lastMoveMs = 0; _lastMoveX = int.MinValue; _lastMoveY = int.MinValue; Recording = true;
    }
    public void StopRecording() { Recording = false; _clock.Stop(); }
    private IntPtr KeyboardCallback(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lp);
            bool injected = (k.flags & NativeMethods.LLKHF_INJECTED) != 0 || k.dwExtraInfo.ToUInt64() == InjectionMarker;
            if (!injected && !ReservedHotkeys.Contains(k.vkCode)) PhysicalInput?.Invoke();
            if (Recording && !injected && !ReservedHotkeys.Contains(k.vkCode))
            {
                int msg = wp.ToInt32();
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                    Recorded?.Invoke(new MacroEvent { TimeMs = _clock.ElapsedMilliseconds, Kind = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN ? MacroEventKind.KeyDown : MacroEventKind.KeyUp, VirtualKey = k.vkCode, ScanCode = k.scanCode, Extended = (k.flags & NativeMethods.LLKHF_EXTENDED) != 0 });
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wp, lp);
    }
    private IntPtr MouseCallback(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            var m = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lp);
            bool injected = (m.flags & NativeMethods.LLMHF_INJECTED) != 0 || m.dwExtraInfo.ToUInt64() == InjectionMarker;
            if (!injected) PhysicalInput?.Invoke();
            if (Recording && !injected)
            {
                var msg = wp.ToInt32(); var now = _clock.ElapsedMilliseconds;
                MacroEvent? e = msg switch
                {
                    NativeMethods.WM_MOUSEMOVE when ShouldRecordMove(m.pt.X, m.pt.Y, now) => new() { TimeMs = now, Kind = MacroEventKind.MouseMove, X = m.pt.X, Y = m.pt.Y },
                    NativeMethods.WM_LBUTTONDOWN => Mouse(now, MacroEventKind.MouseDown, MouseButtonKind.Left, m),
                    NativeMethods.WM_LBUTTONUP => Mouse(now, MacroEventKind.MouseUp, MouseButtonKind.Left, m),
                    NativeMethods.WM_RBUTTONDOWN => Mouse(now, MacroEventKind.MouseDown, MouseButtonKind.Right, m),
                    NativeMethods.WM_RBUTTONUP => Mouse(now, MacroEventKind.MouseUp, MouseButtonKind.Right, m),
                    NativeMethods.WM_MBUTTONDOWN => Mouse(now, MacroEventKind.MouseDown, MouseButtonKind.Middle, m),
                    NativeMethods.WM_MBUTTONUP => Mouse(now, MacroEventKind.MouseUp, MouseButtonKind.Middle, m),
                    NativeMethods.WM_XBUTTONDOWN => Mouse(now, MacroEventKind.MouseDown, (m.mouseData >> 16) == 1 ? MouseButtonKind.X1 : MouseButtonKind.X2, m),
                    NativeMethods.WM_XBUTTONUP => Mouse(now, MacroEventKind.MouseUp, (m.mouseData >> 16) == 1 ? MouseButtonKind.X1 : MouseButtonKind.X2, m),
                    NativeMethods.WM_MOUSEWHEEL => new() { TimeMs = now, Kind = MacroEventKind.MouseWheel, X = m.pt.X, Y = m.pt.Y, Delta = (short)(m.mouseData >> 16) },
                    _ => null
                };
                if (e is not null) Recorded?.Invoke(e);
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wp, lp);
    }
    private bool ShouldRecordMove(int x, int y, long now)
    {
        if (_lastMoveX == int.MinValue || Math.Abs(x - _lastMoveX) >= 2 || Math.Abs(y - _lastMoveY) >= 2 || now - _lastMoveMs >= 8)
        { _lastMoveX = x; _lastMoveY = y; _lastMoveMs = now; return true; }
        return false;
    }
    private static MacroEvent Mouse(long time, MacroEventKind kind, MouseButtonKind button, NativeMethods.MSLLHOOKSTRUCT m) => new() { TimeMs = time, Kind = kind, Button = button, X = m.pt.X, Y = m.pt.Y };
    public void Dispose() { StopRecording(); if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook); if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook); }
}
