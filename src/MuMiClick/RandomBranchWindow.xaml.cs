using System.Collections.ObjectModel;
using System.Windows;
using System.Text.Json;

namespace MuMiClick;

public partial class RandomBranchWindow : Window
{
    private readonly List<MacroEvent> _sourceEvents;
    private readonly CoordinateMode _coordinateMode;
    private readonly TargetWindowInfo? _targetWindow;
    private readonly ObservableCollection<MacroBranch> _items;
    private readonly Dictionary<MacroBranch, HashSet<MacroEvent>> _consumedByBranch = [];
    public List<MacroBranch> Branches { get; private set; } = [];
    public HashSet<MacroEvent> ConsumedEvents { get; private set; } = [];

    public RandomBranchWindow(IReadOnlyList<MacroEvent> sourceEvents, IReadOnlyCollection<MacroEvent> initiallySelected,
        IReadOnlyList<MacroBranch>? branches, CoordinateMode coordinateMode, TargetWindowInfo? targetWindow, bool darkMode)
    {
        InitializeComponent();
        ThemeService.Apply(this, darkMode);
        _sourceEvents = sourceEvents.ToList();
        _coordinateMode = coordinateMode;
        _targetWindow = targetWindow;
        _items = new ObservableCollection<MacroBranch>(Clone(branches ?? []));
        BranchesGrid.ItemsSource = _items;
        var rows = BuildRows(sourceEvents);
        SourceList.ItemsSource = rows;
        foreach (var row in rows.Where(x => x.SourceEvents.Any(initiallySelected.Contains))) SourceList.SelectedItems.Add(row);
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = SourceList.SelectedItems.Cast<EventListItem>().SelectMany(x => x.SourceEvents).Distinct()
            .OrderBy(x => _sourceEvents.IndexOf(x)).ToList();
        if (selected.Count == 0) { System.Windows.MessageBox.Show(this, LocalizationService.T("SelectActionsForBranch")); return; }
        var events = CloneEvents(selected);
        var start = events.Min(x => x.TimeMs);
        foreach (var item in events) item.TimeMs = Math.Max(0, item.TimeMs - start);
        var branch = new MacroBranch
        {
            Name = LocalizationService.F("BranchDefaultNameFormat", _items.Count + 1),
            CoordinateMode = _coordinateMode,
            TargetWindow = _targetWindow,
            Events = events
        };
        _items.Add(branch);
        _consumedByBranch[branch] = selected.ToHashSet();
        BranchesGrid.SelectedItem = branch;
        SourceList.SelectedItems.Clear();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (BranchesGrid.SelectedItem is not MacroBranch selected) return;
        _items.Remove(selected);
        _consumedByBranch.Remove(selected);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        BranchesGrid.CommitEdit(); BranchesGrid.CommitEdit();
        if (_items.Count < 2) { System.Windows.MessageBox.Show(this, LocalizationService.T("NeedTwoBranches")); return; }
        Branches = Clone(_items);
        ConsumedEvents = _consumedByBranch.Values.SelectMany(x => x).ToHashSet();
        DialogResult = true;
    }

    private static List<EventListItem> BuildRows(IReadOnlyList<MacroEvent> events)
    {
        var rows = new List<EventListItem>(); EventListItem? group = null;
        foreach (var item in events)
        {
            if (item.Kind == MacroEventKind.MouseMove)
            {
                if (group is null) { group = EventListItem.Group(item); rows.Add(group); }
                else group.AddToGroup(item);
            }
            else { group = null; rows.Add(EventListItem.Single(item)); }
        }
        return rows;
    }

    private static List<MacroBranch> Clone(IEnumerable<MacroBranch> branches) =>
        JsonSerializer.Deserialize<List<MacroBranch>>(JsonSerializer.Serialize(branches, JsonOptions()), JsonOptions()) ?? [];
    private static List<MacroEvent> CloneEvents(IEnumerable<MacroEvent> events) =>
        JsonSerializer.Deserialize<List<MacroEvent>>(JsonSerializer.Serialize(events, JsonOptions()), JsonOptions()) ?? [];
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}
