using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MuMiClick;

internal sealed class MacroPlayer
{
    private readonly HashSet<uint> _keys = [];
    private readonly HashSet<MouseButtonKind> _buttons = [];
    private CancellationTokenSource? _cancel;
    private readonly object _pauseSync = new();
    private TaskCompletionSource<bool> _resumeSignal = CompletedSignal();
    private Stopwatch? _activeClock;
    private volatile bool _paused;
    public bool IsPlaying => _cancel is not null;
    public bool IsPaused => _paused;
    public event Action<long, long, double>? Progress;
    public event Action<string>? Completed;

    public async Task PlayAsync(MacroDocument doc, int repeat, bool infinite, double speed, int intervalMs, bool instantMouseMovement, int instantMouseDelayMs, CancellationToken appToken)
    {
        if (doc.Events.Count == 0) throw new InvalidOperationException(LocalizationService.T("NoEvents"));
        if (IsPlaying) return;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(appToken); var ct = _cancel.Token;
        try
        {
            IntPtr target = IntPtr.Zero;
            if (doc.CoordinateMode == CoordinateMode.TargetWindow)
            {
                if (doc.TargetWindow is null || (target = WindowLocator.Find(doc.TargetWindow)) == IntPtr.Zero) throw new InvalidOperationException(LocalizationService.T("TargetWindowMissing"));
                NativeMethods.SetForegroundWindow(target);
                await Task.Delay(120, ct);
            }
            long count = Math.Max(1, repeat);
            var end = Math.Max(1, doc.Events[^1].TimeMs);
            for (long loop = 1; infinite || loop <= count; loop++)
            {
                await PlayOnce(doc.Events, target, doc.Variables ?? [], doc.VariableGroups ?? [], speed, loop, infinite ? long.MaxValue : count, end, instantMouseMovement, Math.Clamp(instantMouseDelayMs, 0, 500), ct);
                if ((infinite || loop < count) && intervalMs > 0) await DelayWithPauseAsync(intervalMs, ct);
                if (infinite && loop == long.MaxValue) loop = 0;
            }
            Completed?.Invoke(LocalizationService.T("PlaybackCompleted"));
        }
        catch (OperationCanceledException) { Completed?.Invoke(LocalizationService.T("PlaybackStopped")); }
        catch (Exception ex) { Completed?.Invoke(LocalizationService.F("PlaybackErrorFormat", ex.Message)); }
        finally { ReleaseAll(); ResumeWaiters(); _activeClock = null; _cancel?.Dispose(); _cancel = null; }
    }
    private async Task PlayOnce(IReadOnlyList<MacroEvent> events, IntPtr target, IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, List<string>> variableGroups, double speed, long loop, long total, long end, bool instantMouseMovement, int instantMouseDelayMs, CancellationToken ct)
    {
        await PlaySequenceAsync(events, target, variables, variableGroups, speed, instantMouseMovement, instantMouseDelayMs, ct,
            e => Progress?.Invoke(loop, total, Math.Min(1, e.TimeMs / (double)end)));
    }
    private async Task PlaySequenceAsync(IReadOnlyList<MacroEvent> events, IntPtr target, IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, List<string>> variableGroups, double speed, bool instantMouseMovement, int instantMouseDelayMs, CancellationToken ct, Action<MacroEvent>? afterEvent = null)
    {
        var previousClock = _activeClock;
        var sw = Stopwatch.StartNew(); _activeClock = sw;
        long externalWaitOffset = 0;
        long compressedPlaybackMs = 0;
        long skippedMovementStartedAt = -1;
        try
        {
            foreach (var e in events)
            {
                ct.ThrowIfCancellationRequested(); await WaitWhilePausedAsync(ct);
                if (instantMouseMovement && e.Kind == MacroEventKind.MouseMove)
                {
                    if (skippedMovementStartedAt < 0) skippedMovementStartedAt = e.TimeMs;
                    continue;
                }
                if (instantMouseMovement && skippedMovementStartedAt >= 0)
                {
                    var recordedMovementPlaybackMs = (long)(Math.Max(0, e.TimeMs - skippedMovementStartedAt) / Math.Max(0.05, speed));
                    compressedPlaybackMs += Math.Max(0, recordedMovementPlaybackMs - instantMouseDelayMs);
                    skippedMovementStartedAt = -1;
                }
                long due = (long)(e.TimeMs / Math.Max(0.05, speed)) - compressedPlaybackMs + externalWaitOffset;
                while (true)
                {
                    long remain = due - sw.ElapsedMilliseconds; if (remain <= 0) break;
                    await Task.Delay((int)Math.Min(remain, 15), ct); await WaitWhilePausedAsync(ct);
                }
                if (e.Kind == MacroEventKind.WaitForSaveDialog)
                {
                    var waitStarted = sw.ElapsedMilliseconds;
                    await WindowLocator.WaitForSaveDialogAsync(e.TimeoutMs <= 0 ? 15000 : e.TimeoutMs, ct);
                    externalWaitOffset += sw.ElapsedMilliseconds - waitStarted;
                }
                else if (e.Kind == MacroEventKind.WaitForWindowText)
                {
                    if (e.TextTargetWindow is null) throw new InvalidOperationException(LocalizationService.T("TextTriggerWindowRequired"));
                    var waitStarted = sw.ElapsedMilliseconds;
                    await WindowTextLocator.WaitForTextAsync(e.TextTargetWindow, e.WaitText ?? "", e.TextExactMatch, e.TimeoutMs, ct);
                    externalWaitOffset += sw.ElapsedMilliseconds - waitStarted;
                }
                else if (e.Kind == MacroEventKind.SetClipboardVariable) await SetClipboardVariableAsync(e, variables, variableGroups, ct);
                else if (e.Kind == MacroEventKind.RandomBranch) await PlayRandomBranchAsync(e.Branches, variables, variableGroups, speed, instantMouseMovement, instantMouseDelayMs, ct);
                else Send(e, target);
                afterEvent?.Invoke(e);
            }
        }
        finally { sw.Stop(); _activeClock = previousClock; }
    }
    internal static MacroBranch? ChooseBranch(IReadOnlyList<MacroBranch>? branches, Random? random = null)
    {
        if (branches is null || branches.Count == 0) return null;
        return branches[(random ?? Random.Shared).Next(branches.Count)];
    }
    private async Task PlayRandomBranchAsync(IReadOnlyList<MacroBranch>? branches, IReadOnlyDictionary<string, string> parentVariables,
        IReadOnlyDictionary<string, List<string>> variableGroups, double speed, bool instantMouseMovement, int instantMouseDelayMs, CancellationToken ct)
    {
        var branch = ChooseBranch(branches) ?? throw new InvalidOperationException(LocalizationService.T("NeedTwoBranches"));
        var parentClock = _activeClock;
        parentClock?.Stop();
        try
        {
            IntPtr target = IntPtr.Zero;
            if (branch.CoordinateMode == CoordinateMode.TargetWindow)
            {
                if (branch.TargetWindow is null || (target = WindowLocator.Find(branch.TargetWindow)) == IntPtr.Zero)
                    throw new InvalidOperationException(LocalizationService.T("TargetWindowMissing"));
                NativeMethods.SetForegroundWindow(target);
                await Task.Delay(120, ct);
            }
            var variables = new Dictionary<string, string>(parentVariables, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in branch.Variables ?? []) variables[pair.Key] = pair.Value;
            await PlaySequenceAsync(branch.Events, target, variables, variableGroups, speed, instantMouseMovement, instantMouseDelayMs, ct);
        }
        finally
        {
            _activeClock = parentClock;
            if (!_paused) parentClock?.Start();
        }
    }
    internal static string? ChooseVariableName(MacroEvent e, IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, List<string>> variableGroups, Random? random = null)
    {
        if (!e.RandomFromVariableGroup) return e.VariableName;
        if (string.IsNullOrWhiteSpace(e.VariableGroupName)) return null;
        var group = variableGroups.FirstOrDefault(x => x.Key.Equals(e.VariableGroupName, StringComparison.OrdinalIgnoreCase));
        if (group.Key is null || group.Value is null) return null;
        var candidates = group.Value.Where(variables.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) return null;
        return candidates[(random ?? Random.Shared).Next(candidates.Count)];
    }
    private static async Task SetClipboardVariableAsync(MacroEvent e, IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, List<string>> variableGroups, CancellationToken ct)
    {
        var variableName = ChooseVariableName(e, variables, variableGroups);
        if (string.IsNullOrWhiteSpace(variableName) || !variables.TryGetValue(variableName, out var value))
        {
            if (e.RandomFromVariableGroup)
                throw new InvalidOperationException(LocalizationService.F("VariableGroupMissingFormat", e.VariableGroupName ?? ""));
            throw new InvalidOperationException(LocalizationService.F("VariableMissingFormat", variableName ?? ""));
        }
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { System.Windows.Clipboard.SetText(value ?? ""); return; }
            catch (COMException) when (attempt < 7) { await Task.Delay(25, ct); }
            catch (ExternalException) when (attempt < 7) { await Task.Delay(25, ct); }
        }
        throw new InvalidOperationException(LocalizationService.T("ClipboardBusy"));
    }
    public void TogglePause()
    {
        if (!IsPlaying) return;
        TaskCompletionSource<bool>? signal = null;
        lock (_pauseSync)
        {
            if (!_paused)
            {
                _paused = true;
                _activeClock?.Stop();
                _resumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                _paused = false;
                _activeClock?.Start();
                signal = _resumeSignal;
            }
        }
        signal?.TrySetResult(true);
    }
    public void Stop() { _cancel?.Cancel(); ResumeWaiters(); ReleaseAll(); }
    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            Task waiter;
            lock (_pauseSync)
            {
                if (!_paused) return;
                waiter = _resumeSignal.Task;
            }
            await waiter.WaitAsync(ct);
        }
    }
    private async Task DelayWithPauseAsync(int delayMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew(); _activeClock = sw;
        while (sw.ElapsedMilliseconds < delayMs)
        {
            await WaitWhilePausedAsync(ct);
            var remaining = delayMs - sw.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay((int)Math.Min(remaining, 15), ct);
        }
    }
    private void ResumeWaiters()
    {
        TaskCompletionSource<bool> signal;
        lock (_pauseSync)
        {
            _paused = false;
            _activeClock?.Start();
            signal = _resumeSignal;
        }
        signal.TrySetResult(true);
    }
    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult(true);
        return signal;
    }
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
