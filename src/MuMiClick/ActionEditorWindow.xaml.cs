using System.Windows;
using System.Windows.Input;

namespace MuMiClick;

internal enum EditableActionType { MouseMove, MouseClick, MouseWheel, KeyPress }

public partial class ActionEditorWindow : Window
{
    private readonly long _startTime;
    private readonly List<KeyChoice> _keyChoices;
    public List<MacroEvent> ReplacementEvents { get; private set; } = [];

    public ActionEditorWindow(MacroEvent primary, MacroEvent? paired, bool darkMode)
    {
        InitializeComponent();
        ThemeService.Apply(this, darkMode);
        _startTime = paired is null ? primary.TimeMs : Math.Min(primary.TimeMs, paired.TimeMs);
        var duration = paired is null ? 30 : (int)Math.Clamp(Math.Abs(paired.TimeMs - primary.TimeMs), 1, 10000);
        DurationBox.Text = duration.ToString();

        var types = Enum.GetValues<EditableActionType>().Select(x => new ActionTypeChoice(x)).ToList();
        ActionTypeCombo.ItemsSource = types;
        MouseButtonCombo.ItemsSource = Enum.GetValues<MouseButtonKind>().Select(x => new MouseButtonChoice(x)).ToList();
        _keyChoices = Enum.GetValues<Key>().Select(key => new { Key = key, VirtualKey = SafeVirtualKey(key) })
            .Where(x => x.VirtualKey > 0 && (NativeMethods.MapVirtualKey(x.VirtualKey, NativeMethods.MAPVK_VK_TO_VSC_EX) & 0xFF) != 0)
            .GroupBy(x => x.VirtualKey).Select(x => new KeyChoice(x.First().Key, (uint)x.Key)).ToList();
        KeyCombo.ItemsSource = _keyChoices;

        var mouseSource = new[] { primary, paired }.Where(x => x is not null && x.Kind is MacroEventKind.MouseMove or MacroEventKind.MouseDown or MacroEventKind.MouseUp or MacroEventKind.MouseWheel).FirstOrDefault();
        if (mouseSource is not null) { XBox.Text = mouseSource.X.ToString(); YBox.Text = mouseSource.Y.ToString(); }
        else if (NativeMethods.GetCursorPos(out var cursor)) { XBox.Text = cursor.X.ToString(); YBox.Text = cursor.Y.ToString(); }
        else { XBox.Text = "0"; YBox.Text = "0"; }
        WheelDeltaBox.Text = (primary.Kind == MacroEventKind.MouseWheel ? primary.Delta : 120).ToString();
        var button = primary.Button ?? paired?.Button ?? MouseButtonKind.Left;
        MouseButtonCombo.SelectedItem = MouseButtonCombo.Items.Cast<MouseButtonChoice>().First(x => x.Button == button);

        var initialType = primary.Kind switch
        {
            MacroEventKind.MouseMove => EditableActionType.MouseMove,
            MacroEventKind.MouseDown or MacroEventKind.MouseUp => EditableActionType.MouseClick,
            MacroEventKind.MouseWheel => EditableActionType.MouseWheel,
            MacroEventKind.KeyDown or MacroEventKind.KeyUp => EditableActionType.KeyPress,
            _ => throw new ArgumentException("Unsupported event type", nameof(primary))
        };
        ActionTypeCombo.SelectedItem = types.First(x => x.Type == initialType);

        var virtualKey = primary.VirtualKey != 0 ? primary.VirtualKey : paired?.VirtualKey ?? 0;
        if (virtualKey == 0 && primary.ScanCode != 0) virtualKey = NativeMethods.MapVirtualKey(primary.ScanCode, NativeMethods.MAPVK_VSC_TO_VK_EX);
        KeyCombo.SelectedItem = _keyChoices.FirstOrDefault(x => x.VirtualKey == virtualKey) ?? _keyChoices.FirstOrDefault(x => x.Key == Key.A);
        UpdatePanels();
        UpdateKeyDetails();
    }

    private void ActionType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PositionPanel is not null) UpdatePanels();
    }

    private void Key_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (KeyDetailsText is not null) UpdateKeyDetails();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((ActionTypeCombo.SelectedItem as ActionTypeChoice)?.Type != EditableActionType.KeyPress || !KeyCombo.IsKeyboardFocusWithin || e.Key is Key.Tab or Key.Enter or Key.Escape) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var choice = _keyChoices.FirstOrDefault(x => x.VirtualKey == SafeVirtualKey(key));
        if (choice is null) return;
        KeyCombo.SelectedItem = choice;
        e.Handled = true;
    }

    private void UpdatePanels()
    {
        var type = (ActionTypeCombo.SelectedItem as ActionTypeChoice)?.Type ?? EditableActionType.MouseMove;
        PositionPanel.Visibility = type is EditableActionType.MouseMove or EditableActionType.MouseClick or EditableActionType.MouseWheel ? Visibility.Visible : Visibility.Collapsed;
        MouseButtonPanel.Visibility = type == EditableActionType.MouseClick ? Visibility.Visible : Visibility.Collapsed;
        WheelPanel.Visibility = type == EditableActionType.MouseWheel ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = type == EditableActionType.KeyPress ? Visibility.Visible : Visibility.Collapsed;
        DurationPanel.Visibility = type is EditableActionType.MouseClick or EditableActionType.KeyPress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateKeyDetails()
    {
        if (KeyCombo.SelectedItem is not KeyChoice key) { KeyDetailsText.Text = ""; return; }
        var mapped = NativeMethods.MapVirtualKey(key.VirtualKey, NativeMethods.MAPVK_VK_TO_VSC_EX);
        KeyDetailsText.Text = LocalizationService.F("KeyDetailsFormat", key.VirtualKey, mapped & 0xFF, (mapped & 0xFF00) != 0 ? LocalizationService.T("ExtendedKey") : LocalizationService.T("StandardKey"));
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ActionTypeCombo.SelectedItem is not ActionTypeChoice selectedType) return;
        if (selectedType.Type is EditableActionType.MouseMove or EditableActionType.MouseClick or EditableActionType.MouseWheel)
        {
            if (!int.TryParse(XBox.Text, out var x) || !int.TryParse(YBox.Text, out var y) || Math.Abs((long)x) > 100000 || Math.Abs((long)y) > 100000)
            { System.Windows.MessageBox.Show(this, LocalizationService.T("MousePositionInvalid")); return; }
            if (selectedType.Type == EditableActionType.MouseMove) ReplacementEvents = [new MacroEvent { TimeMs = _startTime, Kind = MacroEventKind.MouseMove, X = x, Y = y }];
            else if (selectedType.Type == EditableActionType.MouseWheel)
            {
                if (!int.TryParse(WheelDeltaBox.Text, out var delta) || delta == 0 || Math.Abs((long)delta) > 12000) { System.Windows.MessageBox.Show(this, LocalizationService.T("WheelDeltaInvalid")); return; }
                ReplacementEvents = [new MacroEvent { TimeMs = _startTime, Kind = MacroEventKind.MouseWheel, X = x, Y = y, Delta = delta }];
            }
            else
            {
                if (MouseButtonCombo.SelectedItem is not MouseButtonChoice mouseButton || !TryDuration(out var duration)) return;
                ReplacementEvents = CreateMouseClickEvents(_startTime, x, y, mouseButton.Button, duration);
            }
        }
        else
        {
            if (KeyCombo.SelectedItem is not KeyChoice key || !TryDuration(out var duration)) return;
            ReplacementEvents = CreateKeyPressEvents(_startTime, key.VirtualKey, duration);
        }
        DialogResult = true;
    }

    private bool TryDuration(out int duration)
    {
        if (!int.TryParse(DurationBox.Text, out duration) || duration < 1 || duration > 10000)
        { System.Windows.MessageBox.Show(this, LocalizationService.T("InputDurationInvalid")); return false; }
        return true;
    }

    internal static List<MacroEvent> CreateMouseClickEvents(long startTime, int x, int y, MouseButtonKind button, int duration) =>
        [new() { TimeMs = startTime, Kind = MacroEventKind.MouseDown, X = x, Y = y, Button = button }, new() { TimeMs = startTime + duration, Kind = MacroEventKind.MouseUp, X = x, Y = y, Button = button }];

    internal static List<MacroEvent> CreateKeyPressEvents(long startTime, uint virtualKey, int duration)
    {
        var mapped = NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MAPVK_VK_TO_VSC_EX);
        var scanCode = mapped & 0xFF;
        var extended = (mapped & 0xFF00) != 0;
        return [new() { TimeMs = startTime, Kind = MacroEventKind.KeyDown, VirtualKey = virtualKey, ScanCode = scanCode, Extended = extended }, new() { TimeMs = startTime + duration, Kind = MacroEventKind.KeyUp, VirtualKey = virtualKey, ScanCode = scanCode, Extended = extended }];
    }

    private static uint SafeVirtualKey(Key key)
    {
        try { return (uint)Math.Max(0, KeyInterop.VirtualKeyFromKey(key)); }
        catch { return 0; }
    }

    private sealed class ActionTypeChoice
    {
        public ActionTypeChoice(EditableActionType type) => Type = type;
        public EditableActionType Type { get; }
        public string Display => LocalizationService.T(Type switch { EditableActionType.MouseMove => "EditMouseMove", EditableActionType.MouseClick => "EditMouseClick", EditableActionType.MouseWheel => "EditMouseWheel", _ => "EditKeyPress" });
    }
    private sealed class MouseButtonChoice
    {
        public MouseButtonChoice(MouseButtonKind button) => Button = button;
        public MouseButtonKind Button { get; }
        public string Display => LocalizationService.T($"MouseButton{Button}");
    }
    private sealed class KeyChoice
    {
        public KeyChoice(Key key, uint virtualKey) { Key = key; VirtualKey = virtualKey; }
        public Key Key { get; }
        public uint VirtualKey { get; }
        public string Display => Key.ToString();
    }
}
