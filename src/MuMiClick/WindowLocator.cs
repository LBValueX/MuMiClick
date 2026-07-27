using System.Diagnostics;
using System.Text;

namespace MuMiClick;

internal static class WindowLocator
{
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
