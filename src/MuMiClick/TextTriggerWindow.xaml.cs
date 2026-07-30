using System.Windows;

namespace MuMiClick;

public partial class TextTriggerWindow : Window
{
    private readonly TargetWindowInfo? _preferredTarget;
    public TargetWindowInfo? TargetWindow { get; private set; }
    public string ExpectedText { get; private set; } = "";
    public bool ExactMatch { get; private set; }
    public int TimeoutMs { get; private set; }

    public TextTriggerWindow(TargetWindowInfo? preferredTarget, bool darkMode)
    {
        _preferredTarget = preferredTarget;
        InitializeComponent();
        ThemeService.Apply(this, darkMode);
        RefreshWindows();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        var windows = WindowLocator.GetWindows().Where(x => x.ProcessId != Environment.ProcessId)
            .Select(x => new WindowChoice(x)).ToList();
        WindowCombo.ItemsSource = windows;
        var preferred = windows.FirstOrDefault(x => IsSameWindow(x.Window, _preferredTarget));
        var chrome = windows.FirstOrDefault(x => x.Window.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase));
        WindowCombo.SelectedItem = preferred ?? chrome ?? windows.FirstOrDefault();
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (WindowCombo.SelectedItem is not WindowChoice choice) { System.Windows.MessageBox.Show(this, LocalizationService.T("TextTriggerWindowRequired")); return; }
        var text = ExpectedTextBox.Text.Trim();
        if (text.Length == 0) { System.Windows.MessageBox.Show(this, LocalizationService.T("TextTriggerRequired")); return; }
        if (!int.TryParse(TimeoutBox.Text, out var seconds) || seconds < 0 || seconds > 3600)
        {
            System.Windows.MessageBox.Show(this, LocalizationService.T("TextTriggerTimeoutInvalid"));
            return;
        }
        TargetWindow = choice.Window;
        ExpectedText = text;
        ExactMatch = ExactRadio.IsChecked == true;
        TimeoutMs = seconds == 0 ? 0 : seconds * 1000;
        DialogResult = true;
    }

    private static bool IsSameWindow(TargetWindowInfo candidate, TargetWindowInfo? preferred) => preferred is not null &&
        candidate.ClassName == preferred.ClassName && candidate.ProcessName.Equals(preferred.ProcessName, StringComparison.OrdinalIgnoreCase) && candidate.Title == preferred.Title;

    private sealed class WindowChoice(TargetWindowInfo window)
    {
        public TargetWindowInfo Window { get; } = window;
        public string Display => Window.ToString();
    }
}
