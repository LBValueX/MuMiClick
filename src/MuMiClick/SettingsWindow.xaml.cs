using System.Windows;
using System.Windows.Controls;

namespace MuMiClick;

public partial class SettingsWindow : Window
{
    public string RecordHotkey => RecordHotkeyBox.Text.Trim();
    public string PlayHotkey => PlayHotkeyBox.Text.Trim();
    public string PauseHotkey => PauseHotkeyBox.Text.Trim();
    public string StopHotkey => StopHotkeyBox.Text.Trim();
    public string SelectedLanguage => (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";

    public SettingsWindow(UserSettings settings)
    {
        InitializeComponent();
        RecordHotkeyBox.Text = settings.RecordHotkey;
        PlayHotkeyBox.Text = settings.PlayHotkey;
        PauseHotkeyBox.Text = settings.PauseHotkey;
        StopHotkeyBox.Text = settings.StopHotkey;
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), settings.Language, StringComparison.OrdinalIgnoreCase))
            ?? LanguageBox.Items[0];
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parsed = new[] { RecordHotkey, PlayHotkey, PauseHotkey, StopHotkey }
                .Select(HotkeyManager.Parse)
                .ToArray();
            if (parsed.Distinct().Count() != parsed.Length)
                throw new InvalidOperationException(LocalizationService.T("DuplicateHotkey"));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, LocalizationService.T("HotkeyRegistrationError"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
