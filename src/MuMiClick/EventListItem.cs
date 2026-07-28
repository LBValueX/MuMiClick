using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MuMiClick;

internal sealed class EventListItem : INotifyPropertyChanged
{
    private readonly List<MacroEvent>? _groupEvents;
    private bool _isExpanded;

    private EventListItem(MacroEvent? item, List<MacroEvent>? groupEvents, EventListItem? parentGroup)
    {
        Event = item;
        _groupEvents = groupEvents;
        ParentGroup = parentGroup;
    }

    public MacroEvent? Event { get; }
    public EventListItem? ParentGroup { get; }
    public bool IsMouseMoveGroup => _groupEvents is not null;
    public bool IsChild => ParentGroup is not null;
    public Visibility ExpandVisibility => IsMouseMoveGroup ? Visibility.Visible : Visibility.Hidden;
    public Thickness ContentMargin => IsChild ? new Thickness(18, 0, 0, 0) : new Thickness(0);
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public IReadOnlyList<MacroEvent> SourceEvents => _groupEvents ?? (Event is null ? [] : [Event]);
    public string Display
    {
        get
        {
            if (_groupEvents is null) return (IsChild ? "↳ " : "") + (Event?.Display ?? string.Empty);
            var first = _groupEvents[0];
            var last = _groupEvents[^1];
            return LocalizationService.F("MouseMoveGroupFormat", first.TimeMs, _groupEvents.Count, first.X, first.Y, last.X, last.Y);
        }
    }

    public static EventListItem Group(MacroEvent first) => new(null, [first], null);
    public static EventListItem Single(MacroEvent item) => new(item, null, null);
    public static EventListItem Child(MacroEvent item, EventListItem parent) => new(item, null, parent);

    public void AddToGroup(MacroEvent item)
    {
        if (_groupEvents is null) throw new InvalidOperationException();
        _groupEvents.Add(item);
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(SourceEvents));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
