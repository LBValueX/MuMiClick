using System.Diagnostics;
using System.Text;

namespace MuMiClick;

internal static class WindowLocator
{
    public static bool IsSaveDialogForeground() => IsSaveDialog(NativeMethods.GetForegroundWindow());
    public static async Task<IntPtr> WaitForSaveDialogAsync(int timeoutMs, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        IntPtr candidate = IntPtr.Zero;
        long stableSince = 0;
        while (clock.ElapsedMilliseconds < Math.Max(1000, timeoutMs))
        {
            ct.ThrowIfCancellationRequested();
            var found = FindSaveDialog();
            if (found != IntPtr.Zero && NativeMethods.IsWindowEnabled(found) && IsDialogReady(found))
            {
                if (found != candidate) { candidate = found; stableSince = clock.ElapsedMilliseconds; }
                else if (clock.ElapsedMilliseconds - stableSince >= 250)
                {
                    NativeMethods.SetForegroundWindow(found);
                    await Task.Delay(100, ct);
                    return found;
                }
            }
            else { candidate = IntPtr.Zero; stableSince = 0; }
            await Task.Delay(50, ct);
        }
        throw new TimeoutException(LocalizationService.F("SaveDialogTimeoutFormat", Math.Max(1, timeoutMs / 1000)));
    }
    private static IntPtr FindSaveDialog()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (IsSaveDialog(foreground)) return foreground;
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) => { if (IsSaveDialog(h)) { result = h; return false; } return true; }, IntPtr.Zero);
        return result;
    }
    private static bool IsSaveDialog(IntPtr h)
    {
        if (h == IntPtr.Zero || !NativeMethods.IsWindowVisible(h)) return false;
        var cls = new StringBuilder(128); NativeMethods.GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() != "#32770") return false;
        var title = new StringBuilder(512); NativeMethods.GetWindowText(h, title, title.Capacity);
        var text = title.ToString();
        return text.Contains("저장", StringComparison.OrdinalIgnoreCase) || text.Contains("Save As", StringComparison.OrdinalIgnoreCase) || text.Contains("Save Image", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsDialogReady(IntPtr dialog)
    {
        var childCount = 0;
        var hasEditableControl = false;
        NativeMethods.EnumChildWindows(dialog, (child, _) =>
        {
            if (!NativeMethods.IsWindowVisible(child) || !NativeMethods.IsWindowEnabled(child)) return true;
            childCount++;
            var cls = new StringBuilder(64); NativeMethods.GetClassName(child, cls, cls.Capacity);
            if (NativeMethods.GetDlgCtrlID(child) == 0x47C || cls.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase)) hasEditableControl = true;
            return true;
        }, IntPtr.Zero);
        return hasEditableControl || childCount >= 8;
    }
    public static List<(IntPtr Handle, TargetWindowInfo Info)> GetWindows()
    {
        var list = new List<(IntPtr, TargetWindowInfo)>();
        NativeMethods.EnumWindows((h, _) => { if (TryDescribe(h, out var i)) list.Add((h, i)); return true; }, IntPtr.Zero);
        return list.OrderBy(x => x.Item2.ProcessName).ThenBy(x => x.Item2.Title).ToList();
    }
    private static bool TryDescribe(IntPtr h, out TargetWindowInfo info)
    {
        info = new(); if (!NativeMethods.IsWindowVisible(h)) return false;
        var title = new StringBuilder(512); NativeMethods.GetWindowText(h, title, title.Capacity); if (title.Length == 0) return false;
        var cls = new StringBuilder(256); NativeMethods.GetClassName(h, cls, cls.Capacity); NativeMethods.GetWindowThreadProcessId(h, out var pid);
        try { info = new TargetWindowInfo { Title = title.ToString(), ClassName = cls.ToString(), ProcessId = (int)pid, ProcessName = Process.GetProcessById((int)pid).ProcessName }; return true; } catch { return false; }
    }
    public static IntPtr Find(TargetWindowInfo wanted)
    {
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) => { if (TryDescribe(h, out var i) && i.ClassName == wanted.ClassName && i.ProcessName.Equals(wanted.ProcessName, StringComparison.OrdinalIgnoreCase) && i.Title == wanted.Title) { result = h; return false; } return true; }, IntPtr.Zero);
        return result;
    }
    public static (int X, int Y) ToRelative(IntPtr h, int x, int y) { var p = new NativeMethods.POINT { X = x, Y = y }; NativeMethods.ScreenToClient(h, ref p); return (p.X, p.Y); }
    public static (int X, int Y) ToScreen(IntPtr h, int x, int y) { var p = new NativeMethods.POINT { X = x, Y = y }; NativeMethods.ClientToScreen(h, ref p); return (p.X, p.Y); }
}
