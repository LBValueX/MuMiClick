namespace MuMiClick;

internal static class ActionEditService
{
    internal static bool IsEditable(MacroEvent item) => item.Kind is MacroEventKind.MouseMove or MacroEventKind.MouseDown or MacroEventKind.MouseUp or MacroEventKind.MouseWheel or MacroEventKind.KeyDown or MacroEventKind.KeyUp;

    internal static List<MacroEvent> FindLogicalAction(IReadOnlyList<MacroEvent> events, MacroEvent selected)
    {
        var index = IndexOfReference(events, selected);
        if (index < 0 || !IsEditable(selected)) return [];
        if (selected.Kind == MacroEventKind.KeyDown)
            for (var i = index + 1; i < events.Count; i++)
                if (IsMatchingKeyUp(selected, events[i])) return [selected, events[i]];
        if (selected.Kind == MacroEventKind.KeyUp)
            for (var i = index - 1; i >= 0; i--)
                if (IsMatchingKeyUp(events[i], selected)) return [events[i], selected];
        if (selected.Kind == MacroEventKind.MouseDown)
        {
            for (var i = index + 1; i < events.Count; i++)
            {
                if (events[i].Kind == MacroEventKind.MouseMove) continue;
                if (IsMatchingMouseUp(selected, events[i])) return [selected, events[i]];
                break;
            }
        }
        if (selected.Kind == MacroEventKind.MouseUp)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                if (events[i].Kind == MacroEventKind.MouseMove) continue;
                if (IsMatchingMouseUp(events[i], selected)) return [events[i], selected];
                break;
            }
        }
        return [selected];
    }

    internal static List<MacroEvent> ReplaceLogicalAction(IReadOnlyList<MacroEvent> events, IReadOnlyList<MacroEvent> source, IReadOnlyList<MacroEvent> replacements)
    {
        if (source.Count == 0 || replacements.Count == 0) return events.ToList();
        var result = new List<MacroEvent>(events.Count - source.Count + replacements.Count);
        foreach (var item in events)
        {
            var sourceIndex = IndexOfReference(source, item);
            if (sourceIndex < 0) { result.Add(item); continue; }
            if (source.Count == 1) result.AddRange(replacements);
            else if (sourceIndex < replacements.Count) result.Add(replacements[sourceIndex]);
        }
        return result;
    }

    private static bool IsMatchingKeyUp(MacroEvent down, MacroEvent up) => down.Kind == MacroEventKind.KeyDown && up.Kind == MacroEventKind.KeyUp &&
        down.ScanCode == up.ScanCode && down.Extended == up.Extended;
    private static bool IsMatchingMouseUp(MacroEvent down, MacroEvent up) => down.Kind == MacroEventKind.MouseDown && up.Kind == MacroEventKind.MouseUp && down.Button == up.Button;
    private static int IndexOfReference(IReadOnlyList<MacroEvent> events, MacroEvent item)
    {
        for (var i = 0; i < events.Count; i++) if (ReferenceEquals(events[i], item)) return i;
        return -1;
    }
}
