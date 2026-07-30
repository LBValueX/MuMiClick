using System.Windows;

namespace MuMiClick;

public partial class ClipboardEventWindow : Window
{
    public string? VariableName { get; private set; }
    public string? VariableGroupName { get; private set; }
    public bool RandomFromVariableGroup { get; private set; }

    public ClipboardEventWindow(IEnumerable<string> variableNames, IEnumerable<string> groupNames, bool darkMode)
    {
        InitializeComponent();
        ThemeService.Apply(this, darkMode);
        VariableCombo.ItemsSource = variableNames.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        GroupCombo.ItemsSource = groupNames.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        VariableCombo.SelectedIndex = VariableCombo.Items.Count > 0 ? 0 : -1;
        GroupCombo.SelectedIndex = GroupCombo.Items.Count > 0 ? 0 : -1;
        RandomGroupRadio.IsEnabled = GroupCombo.Items.Count > 0;
        UpdateMode();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (VariableCombo is not null && GroupCombo is not null) UpdateMode();
    }

    private void UpdateMode()
    {
        var random = RandomGroupRadio.IsChecked == true;
        VariableCombo.IsEnabled = !random;
        GroupCombo.IsEnabled = random;
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        RandomFromVariableGroup = RandomGroupRadio.IsChecked == true;
        if (RandomFromVariableGroup)
        {
            if (GroupCombo.SelectedItem is not string groupName) { System.Windows.MessageBox.Show(this, LocalizationService.T("VariableGroupNotSelected")); return; }
            VariableGroupName = groupName;
            VariableName = null;
        }
        else
        {
            if (VariableCombo.SelectedItem is not string variableName) { System.Windows.MessageBox.Show(this, LocalizationService.T("VariableNotSelected")); return; }
            VariableName = variableName;
            VariableGroupName = null;
        }
        DialogResult = true;
    }
}
