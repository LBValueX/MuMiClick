using System.Collections.ObjectModel;
using System.Windows;

namespace MuMiClick;

public partial class VariablesWindow : Window
{
    private readonly ObservableCollection<MacroVariable> _items;
    public Dictionary<string, string> Variables { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> VariableGroups { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public VariablesWindow(IReadOnlyDictionary<string, string> variables, IReadOnlyDictionary<string, List<string>> variableGroups, bool darkMode)
    {
        InitializeComponent();
        ThemeService.Apply(this, darkMode);
        var membership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in variableGroups)
            foreach (var variableName in group.Value ?? [])
                membership.TryAdd(variableName, group.Key);
        _items = new ObservableCollection<MacroVariable>(variables.OrderBy(x => x.Key).Select(x => new MacroVariable
        {
            Name = x.Key,
            Value = x.Value,
            Group = membership.GetValueOrDefault(x.Key, "")
        }));
        VariablesGrid.ItemsSource = _items;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var suffix = 1;
        string name;
        do name = $"variable{suffix++}"; while (_items.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var item = new MacroVariable { Name = name };
        _items.Add(item); VariablesGrid.SelectedItem = item; VariablesGrid.ScrollIntoView(item);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesGrid.SelectedItem is MacroVariable selected) _items.Remove(selected);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        VariablesGrid.CommitEdit(); VariablesGrid.CommitEdit();
        if (_items.Any(x => string.IsNullOrWhiteSpace(x.Name))) { System.Windows.MessageBox.Show(this, LocalizationService.T("VariableNameRequired")); return; }
        if (_items.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) { System.Windows.MessageBox.Show(this, LocalizationService.T("DuplicateVariableName")); return; }
        Variables = _items.ToDictionary(x => x.Name.Trim(), x => x.Value ?? "", StringComparer.OrdinalIgnoreCase);
        VariableGroups = _items.Where(x => !string.IsNullOrWhiteSpace(x.Group))
            .GroupBy(x => x.Group.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(item => item.Name.Trim()).ToList(), StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
    }
}
