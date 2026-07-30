using Accessibility;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MuMiClick;

internal static class WindowTextLocator
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int ChildIdSelf = 0;
    private const int StateSystemInvisible = 0x8000;
    private const int MaxNodes = 20000;
    private const int MaxDepth = 100;
    private static readonly Guid AccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    public static async Task WaitForTextAsync(TargetWindowInfo target, string expectedText, bool exactMatch, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedText)) throw new InvalidOperationException(LocalizationService.T("TextTriggerRequired"));
        var clock = Stopwatch.StartNew();
        while (timeoutMs <= 0 || clock.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var handle = WindowLocator.FindFlexible(target);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var found = await Task.Run(() => ContainsText(handle, expectedText, exactMatch), ct)
                        .WaitAsync(TimeSpan.FromSeconds(5), ct);
                    if (found) return;
                }
                catch (TimeoutException)
                {
                    throw new InvalidOperationException(LocalizationService.T("TextScanUnavailable"));
                }
            }
            await Task.Delay(350, ct);
        }
        throw new TimeoutException(LocalizationService.F("TextTriggerTimeoutFormat", Math.Max(1, timeoutMs / 1000)));
    }

    internal static bool ContainsText(IntPtr window, string expectedText, bool exactMatch)
    {
        if (!TryGetRoot(window, out var root)) return false;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var inspected = 0;
        return Search(root, ChildIdSelf, expectedText.Trim(), exactMatch, 0, visited, ref inspected);
    }

    internal static bool CanInspect(IntPtr window) => TryGetRoot(window, out _);

    internal static bool MatchesText(string? actual, string expected, bool exactMatch)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var candidate = actual.Trim();
        return exactMatch
            ? candidate.Equals(expected, StringComparison.CurrentCultureIgnoreCase)
            : candidate.Contains(expected, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool Search(IAccessible accessible, object childId, string expected, bool exactMatch, int depth,
        HashSet<object> visited, ref int inspected)
    {
        if (depth > MaxDepth || inspected++ >= MaxNodes || !visited.Add(accessible)) return false;
        if (IsInvisible(accessible, childId)) return false;
        if (MatchesText(SafeGet(() => accessible.get_accName(childId)), expected, exactMatch) ||
            MatchesText(SafeGet(() => accessible.get_accValue(childId)), expected, exactMatch) ||
            MatchesText(SafeGet(() => accessible.get_accDescription(childId)), expected, exactMatch)) return true;

        int childCount;
        try { childCount = accessible.accChildCount; }
        catch { return false; }
        if (childCount <= 0) return false;
        var children = new object[Math.Min(childCount, MaxNodes - inspected)];
        if (children.Length == 0) return false;
        if (AccessibleChildren(accessible, 0, children.Length, children, out var obtained) < 0) return false;
        for (var i = 0; i < obtained && inspected < MaxNodes; i++)
        {
            var child = children[i];
            if (child is IAccessible childAccessible)
            {
                if (Search(childAccessible, ChildIdSelf, expected, exactMatch, depth + 1, visited, ref inspected)) return true;
            }
            else if (child is int simpleChildId)
            {
                inspected++;
                if (!IsInvisible(accessible, simpleChildId) &&
                    (MatchesText(SafeGet(() => accessible.get_accName(simpleChildId)), expected, exactMatch) ||
                     MatchesText(SafeGet(() => accessible.get_accValue(simpleChildId)), expected, exactMatch) ||
                     MatchesText(SafeGet(() => accessible.get_accDescription(simpleChildId)), expected, exactMatch))) return true;
            }
        }
        return false;
    }

    private static bool IsInvisible(IAccessible accessible, object childId)
    {
        try { return (Convert.ToInt32(accessible.get_accState(childId)) & StateSystemInvisible) != 0; }
        catch { return false; }
    }

    private static string? SafeGet(Func<string?> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private static bool TryGetRoot(IntPtr window, out IAccessible root)
    {
        root = null!;
        object rootObject;
        var id = AccessibleId;
        if (AccessibleObjectFromWindow(window, ObjIdClient, ref id, out rootObject) < 0 || rootObject is not IAccessible accessible) return false;
        root = accessible;
        return true;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] IAccessible container, int childStart,
        int childCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
}
