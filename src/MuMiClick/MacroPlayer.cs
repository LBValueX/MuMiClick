using System.Diagnostics;

namespace MuMiClick;

internal sealed class MacroPlayer
{
    private readonly HashSet<uint> _keys = [];
    private readonly HashSet<MouseButtonKind> _buttons = [];
    private CancellationTokenSource? _cancel;
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private Stopwatch? _activeClock;
    private volatile bool _paused;
    public bool IsPlaying => _cancel is not null;
    public event Action<long, long, double>? Progress;
    public event Action<string>? Completed;

    public async Task PlayAsync(MacroDocument doc, int repeat, bool infinite, double speed, int intervalMs, CancellationToken appToken)
    {
        if (doc.Events.Count == 0) throw new InvalidOperationException("재생할 녹화 이벤트가 없습니다.");
        if (IsPlaying) return;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(appToken); var ct = _cancel.Token;
        try
        {
            IntPtr target = IntPtr.Zero;
            if (doc.CoordinateMode == CoordinateMode.TargetWindow)
            {
                if (doc.TargetWindow is null || (target = WindowLocator.Find(doc.TargetWindow)) == IntPtr.Zero) throw new InvalidOperationException("저장된 대상 창을 찾을 수 없습니다. 창을 연 뒤 다시 선택하세요.");
                NativeMethods.SetForegroundWindow(target);
                await Task.Delay(120, ct);
            }
            long count = Math.Max(1, repeat);
            var end = Math.Max(1, doc.Events[^1].TimeMs);
            for (long loop = 1; infinite || loop <= count; loop++)
            {
                await PlayOnce(doc.Events, target, speed, loop, infinite ? long.MaxValue : count, end, ct);
                if ((infinite || loop < count) && intervalMs > 0) await Task.Delay(intervalMs, ct);
                if (infinite && loop == long.MaxValue) loop = 0;
            }
            Completed?.Invoke("재생 완료");
        }
        catch (OperationCanceledException) { Completed?.Invoke("재생 중지됨"); }
        catch (Exception ex) { Completed?.Invoke("재생 오류: " + ex.Message); }
        finally { ReleaseAll(); _activeClock = null; _cancel?.Dispose(); _cancel = null; _paused = false; _pauseGate.Set(); }
    }
    private async Task PlayOnce(IReadOnlyList<MacroEvent> events, IntPtr target, double speed, long loop, long total, long end, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew(); _activeClock = sw;
        foreach (var e in events)
        {
            ct.ThrowIfCancellationRequested(); _pauseGate.Wait(ct);
            long due = (long)(e.TimeMs / Math.Max(0.05, speed));
            while (true)
            {
                long remain = due - sw.ElapsedMilliseconds; if (remain <= 0) break;
                await Task.Delay((int)Math.Min(remain, 15), ct); _pauseGate.Wait(ct);
            }
            Send(e, target); Progress?.Invoke(loop, total, Math.Min(1, e.TimeMs / (double)end));
        }
    }
    public void TogglePause()
    {
        if (!IsPlaying) return;
        _paused = !_paused;
        if (_paused) { _activeClock?.Stop(); _pauseGate.Reset(); }
        else { _activeClock?.Start(); _pauseGate.Set(); }
    }
    public void Stop() { _cancel?.Cancel(); _pauseGate.Set(); ReleaseAll(); }
    private void Send(MacroEvent e, IntPtr target)
    {
        int x = e.X, y = e.Y; if (target != IntPtr.Zero && e.Kind is MacroEventKind.MouseMove or MacroEventKind.MouseDown or MacroEventKind.MouseUp or MacroEventKind.MouseWheel) (x, y) = WindowLocator.ToScreen(target, x, y);
        // Button and wheel messages do not carry a usable position for SendInput. Move first so
        // a click is exact even when movement filtering omitted the preceding mouse move.
        if (e.Kind is MacroEventKind.MouseDown or MacroEventKind.MouseUp or MacroEventKind.MouseWheel) MoveToScreen(x, y);
        NativeMethods.INPUT input;
        if (e.Kind is MacroEventKind.KeyDown or MacroEventKind.KeyUp)
        {
            input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wScan = (ushort)e.ScanCode, dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (e.Extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0) | (e.Kind == MacroEventKind.KeyUp ? NativeMethods.KEYEVENTF_KEYUP : 0), dwExtraInfo = (UIntPtr)InputHook.InjectionMarker } } };
            if (e.Kind == MacroEventKind.KeyDown) _keys.Add(e.ScanCode | (e.Extended ? 0x10000u : 0)); else _keys.Remove(e.ScanCode | (e.Extended ? 0x10000u : 0));
        }
        else
        {
            uint flags; uint data = 0;
            if (e.Kind == MacroEventKind.MouseMove) flags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
            else if (e.Kind == MacroEventKind.MouseWheel) { flags = NativeMethods.MOUSEEVENTF_WHEEL; data = unchecked((uint)e.Delta); }
            else { flags = MouseFlag(e.Button!.Value, e.Kind == MacroEventKind.MouseDown); if (e.Kind == MacroEventKind.MouseDown) _buttons.Add(e.Button.Value); else _buttons.Remove(e.Button.Value); if (e.Button is MouseButtonKind.X1 or MouseButtonKind.X2) data = e.Button == MouseButtonKind.X1 ? 1u : 2u; }
            if (e.Kind != MacroEventKind.MouseWheel) { var vX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN); var vY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN); var w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN); var h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN); x = (int)Math.Round((x - vX) * 65535d / Math.Max(1, w - 1)); y = (int)Math.Round((y - vY) * 65535d / Math.Max(1, h - 1)); }
            input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dx = x, dy = y, mouseData = data, dwFlags = flags, dwExtraInfo = (UIntPtr)InputHook.InjectionMarker } } };
        }
        NativeMethods.SendInput(1, [input], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
    private static uint MouseFlag(MouseButtonKind b, bool down) => b switch { MouseButtonKind.Left => down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP, MouseButtonKind.Right => down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP, MouseButtonKind.Middle => down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP, _ => down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP };
    private static void MoveToScreen(int x, int y)
    {
        var vX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN); var vY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN); var w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN); var h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        var move = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dx = (int)Math.Round((x - vX) * 65535d / Math.Max(1, w - 1)), dy = (int)Math.Round((y - vY) * 65535d / Math.Max(1, h - 1)), dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK, dwExtraInfo = (UIntPtr)InputHook.InjectionMarker } } };
        NativeMethods.SendInput(1, [move], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
    private static void ReleaseMouseButton(MouseButtonKind button)
    {
        var data = button == MouseButtonKind.X1 ? 1u : button == MouseButtonKind.X2 ? 2u : 0u;
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { mouseData = data, dwFlags = MouseFlag(button, false), dwExtraInfo = (UIntPtr)InputHook.InjectionMarker } } };
        NativeMethods.SendInput(1, [input], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
    public void ReleaseAll()
    {
        foreach (var key in _keys.ToArray()) Send(new MacroEvent { Kind = MacroEventKind.KeyUp, ScanCode = key & 0xFFFF, Extended = (key & 0x10000) != 0 }, IntPtr.Zero);
        foreach (var button in _buttons.ToArray()) ReleaseMouseButton(button);
        _keys.Clear(); _buttons.Clear();
    }
}
