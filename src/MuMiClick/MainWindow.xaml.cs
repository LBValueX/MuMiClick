using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;

namespace MuMiClick;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MacroEvent> _events = [];
    private readonly ObservableCollection<EventListItem> _eventRows = [];
    private readonly InputHook _hook = new();
    private readonly MacroPlayer _player = new();
    private readonly DispatcherTimer _displayTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Forms.NotifyIcon _tray;
    private HotkeyManager? _hotkeys;
    private UserSettings _settings = new();
    private TargetWindowInfo? _target;
    private Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _variableGroups = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _countdownCancel;
    private bool _recording, _countingDown;
    private bool _saveDialogActiveDuringRecording;
    private EventListItem? _activeEventRow;
    private Stopwatch _recordWatch = new();
    private const string SettingsFileName = "settings.json";

    public MainWindow()
    {
        _settings = LoadSettings();
        LocalizationService.Apply(_settings.Language);
        InitializeComponent();
        ThemeService.Apply(this, _settings.DarkMode);
        EventList.ItemsSource = _eventRows;
        StopPhysicalBox.IsChecked = _settings.StopOnPhysicalInput;
        EasyModeBox.IsChecked = _settings.SimpleMode;
        SaveDialogStabilizationBox.IsChecked = _settings.StabilizeSaveDialog;
        SaveDialogTimeoutBox.Text = Math.Clamp(_settings.SaveDialogTimeoutSeconds, 1, 60).ToString();
        InstantMouseBox.IsChecked = _settings.InstantMouseMovement;
        InstantMouseDelayBox.Text = Math.Clamp(_settings.InstantMouseDelayMs, 0, 500).ToString();
        _hook.Recorded += CaptureEvent;
        _hook.PhysicalInput += () => { if (_player.IsPlaying && StopPhysicalBox.IsChecked == true) Dispatcher.BeginInvoke(StopPlayback); };
        _player.Progress += (loop, total, progress) => Dispatcher.BeginInvoke(() => { StatusText.Text = LocalizationService.F("PlaybackStatusFormat", loop, total == long.MaxValue ? "∞" : total); DetailText.Text = LocalizationService.F("ProgressFormat", progress); ElapsedText.Text = progress.ToString("P0"); UpdateControls(); });
        _player.ActionStarted += action => Dispatcher.BeginInvoke(() => HighlightActiveEvent(action));
        _player.Completed += text => Dispatcher.BeginInvoke(() => { SetIdle(text); UpdateControls(); });
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _displayTimer.Tick += (_, _) => { if (_recording) ElapsedText.Text = _recordWatch.Elapsed.ToString(@"mm\:ss\.f"); };
        _displayTimer.Start();
        _tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true, Text = LocalizationService.T("TrayIdle") };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        Loaded += (_, _) => { InitializeNative(); RestoreLastMacro(); ApplyDisplayMode(); };
        UpdateEventCount();
        UpdateHotkeyFooter();
        UpdateMouseGroupButton();
    }
    private void InitializeNative()
    {
        try
        {
            _hook.Install();
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WindowMessage);
            _hotkeys = new HotkeyManager(source.Handle); _hotkeys.Pressed += GlobalHotkey; _hotkeys.Register(_settings);
        }
        catch (Exception ex) { WpfMessageBox.Show(ex.Message, "MuMiClick", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys?.Handle((IntPtr)msg, wParam) == true) handled = true;
        return IntPtr.Zero;
    }
    private void GlobalHotkey(int id) => Dispatcher.BeginInvoke(async () =>
    {
        if (id == 1) { if (_recording || _countingDown) StopRecording(); else await BeginRecordingAsync(); }
        else if (id == 2) await BeginPlaybackAsync();
        else if (id == 3) TogglePause();
        else if (id == 4) { StopRecording(); StopPlayback(); }
    });
    private async void Record_Click(object sender, RoutedEventArgs e) => await BeginRecordingAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) { StopRecording(); StopPlayback(); }
    private async void Play_Click(object sender, RoutedEventArgs e) => await BeginPlaybackAsync();
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void EasyMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.SimpleMode = EasyModeBox.IsChecked == true;
        SaveSettings(_settings);
        ApplyDisplayMode();
    }
    private void ApplyDisplayMode()
    {
        var simple = EasyModeBox.IsChecked == true;
        var advancedVisibility = simple ? Visibility.Collapsed : Visibility.Visible;
        AdvancedActionsPanel.Visibility = advancedVisibility;
        AdvancedDivider.Visibility = advancedVisibility;
        AdvancedSettingsPanel.Visibility = advancedVisibility;
        CoordinatePanel.Visibility = advancedVisibility;
        AdvancedWorkspace.Visibility = advancedVisibility;
        AdvancedFooter.Visibility = advancedVisibility;
        AdvancedRow.Height = new GridLength(simple ? 0 : 1, simple ? GridUnitType.Pixel : GridUnitType.Star);
        FooterRow.Height = new GridLength(simple ? 0 : 1, simple ? GridUnitType.Pixel : GridUnitType.Auto);
        if (simple)
        {
            MinWidth = 760; MinHeight = 320;
            if (Width < 760) Width = 850;
            Height = 340;
        }
        else
        {
            MinWidth = 1050; MinHeight = 700;
            if (Width < 1050) Width = 1120;
            if (Height < 700) Height = 760;
        }
    }
    private async Task BeginRecordingAsync()
    {
        if (_player.IsPlaying) return;
        if (_recording || _countingDown) return;
        if (TargetRadio.IsChecked == true && _target is null) { WpfMessageBox.Show(LocalizationService.T("TargetRequired")); return; }
        _countingDown = true; _countdownCancel?.Dispose(); var countdown = _countdownCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (!int.TryParse(CountdownBox.Text, out var seconds) || seconds < 0) seconds = 0;
        try { for (var i = seconds; i > 0; i--) { StatusText.Text = LocalizationService.F("CountdownStatusFormat", i); DetailText.Text = LocalizationService.T("CountdownHelp"); await Task.Delay(1000, countdown.Token); } }
        catch (OperationCanceledException) { return; }
        finally { if (ReferenceEquals(_countdownCancel, countdown)) { _countingDown = false; _countdownCancel = null; } countdown.Dispose(); }
        _events.Clear(); _eventRows.Clear(); UpdateEventCount(); UpdateMouseGroupButton(); _saveDialogActiveDuringRecording = false; _recordWatch.Restart(); _hook.StartRecording(); _recording = true;
        StatusText.Text = LocalizationService.T("RecordingStatus"); StatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusRecordingBrush"); DetailText.Text = LocalizationService.T("RecordingHelp"); _tray.Text = LocalizationService.T("TrayRecording"); UpdateControls();
    }
    private void StopRecording()
    {
        if (!_recording && !_countingDown) return;
        _countdownCancel?.Cancel(); _countingDown = false; _hook.StopRecording(); _recordWatch.Stop(); _recording = false; SetIdle(LocalizationService.T("RecordingFinished")); UpdateControls();
    }
    private void CaptureEvent(MacroEvent e)
    {
        if (TargetRadio.IsChecked == true && _target is not null)
        {
            var handle = WindowLocator.Find(_target);
            if (handle != IntPtr.Zero && e.Kind is MacroEventKind.MouseMove or MacroEventKind.MouseDown or MacroEventKind.MouseUp or MacroEventKind.MouseWheel) (e.X, e.Y) = WindowLocator.ToRelative(handle, e.X, e.Y);
        }
        var saveDialogActive = SaveDialogStabilizationBox.IsChecked == true && WindowLocator.IsSaveDialogForeground();
        MacroEvent? waitEvent = null;
        if (saveDialogActive && !_saveDialogActiveDuringRecording)
        {
            var timeoutSeconds = int.TryParse(SaveDialogTimeoutBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 60) : 15;
            waitEvent = new MacroEvent { TimeMs = e.TimeMs, Kind = MacroEventKind.WaitForSaveDialog, TimeoutMs = timeoutSeconds * 1000 };
        }
        _saveDialogActiveDuringRecording = saveDialogActive;
        Dispatcher.BeginInvoke(() => { if (waitEvent is not null) AddEvent(waitEvent); AddEvent(e); });
    }
    private async Task BeginPlaybackAsync()
    {
        if (_recording || _countingDown) { WpfMessageBox.Show(LocalizationService.T("EndRecordingFirst")); return; }
        if (_player.IsPlaying) return;
        var infinite = InfiniteBox.IsChecked == true;
        var repeat = 1;
        if (!infinite && (!int.TryParse(RepeatBox.Text, out repeat) || repeat < 1 || repeat > 1000000)) { WpfMessageBox.Show(LocalizationService.T("RepeatRange")); return; }
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 0) { WpfMessageBox.Show(LocalizationService.T("IntervalInvalid")); return; }
        var speed = double.Parse(((System.Windows.Controls.ComboBoxItem)SpeedBox.SelectedItem).Content!.ToString()![..^1], System.Globalization.CultureInfo.InvariantCulture);
        var doc = new MacroDocument { Events = _events.ToList(), Variables = new(_variables, StringComparer.OrdinalIgnoreCase), VariableGroups = CloneVariableGroups(_variableGroups), CoordinateMode = TargetRadio.IsChecked == true ? CoordinateMode.TargetWindow : CoordinateMode.AbsoluteScreen, TargetWindow = _target };
        ClearActiveEvent();
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusPlayingBrush"); StatusText.Text = LocalizationService.T("PlaybackReady"); _tray.Text = LocalizationService.T("TrayPlaying"); UpdateControls();
        var instantMouseDelay = int.TryParse(InstantMouseDelayBox.Text, out var parsedMouseDelay) ? Math.Clamp(parsedMouseDelay, 0, 500) : 30;
        await _player.PlayAsync(doc, repeat, infinite, speed, interval, InstantMouseBox.IsChecked == true, instantMouseDelay, _lifetime.Token);
    }
    private void TogglePause()
    {
        _player.TogglePause();
        if (_player.IsPlaying)
        {
            DetailText.Text = LocalizationService.T(_player.IsPaused ? "PausedHelp" : "ResumedHelp");
            StatusText.Text = LocalizationService.T(_player.IsPaused ? "StatusPaused" : "StatusPlaying");
        }
    }
    private void StopPlayback() { if (_player.IsPlaying) _player.Stop(); else _player.ReleaseAll(); }

    private void HighlightActiveEvent(MacroEvent action)
    {
        var row = _eventRows.FirstOrDefault(x => ReferenceEquals(x.Event, action))
            ?? _eventRows.FirstOrDefault(x => x.IsMouseMoveGroup && x.SourceEvents.Any(source => ReferenceEquals(source, action)));
        if (row is null || ReferenceEquals(row, _activeEventRow)) return;
        if (_activeEventRow is not null) _activeEventRow.IsActive = false;
        _activeEventRow = row;
        row.IsActive = true;
        EventList.SelectedItems.Clear();
        EventList.SelectedItem = row;
        EventList.ScrollIntoView(row);
    }

    private void ClearActiveEvent()
    {
        if (_activeEventRow is null) return;
        _activeEventRow.IsActive = false;
        _activeEventRow = null;
        EventList.SelectedItems.Clear();
    }

    private void SetIdle(string message)
    {
        StatusText.Text = LocalizationService.T("StatusIdle"); StatusText.Foreground = (System.Windows.Media.Brush)FindResource("StatusIdleBrush"); DetailText.Text = message; _tray.Text = LocalizationService.T("TrayIdle"); if (!_recording) ElapsedText.Text = "00:00.0";
    }
    private void UpdateControls()
    {
        // Prevent accidental recording or replay requests while the playback engine owns input.
        RecordButton.IsEnabled = !_player.IsPlaying;
        PlayButton.IsEnabled = !_recording && !_countingDown && !_player.IsPlaying;
    }
    private void AddEvent(MacroEvent item)
    {
        var continuesMouseGroup = item.Kind == MacroEventKind.MouseMove && _events.Count > 0 && _events[^1].Kind == MacroEventKind.MouseMove;
        _events.Add(item);
        if (item.Kind == MacroEventKind.MouseMove)
        {
            var group = continuesMouseGroup ? _eventRows.LastOrDefault(x => x.IsMouseMoveGroup) : null;
            if (group is null)
            {
                group = EventListItem.Group(item);
                _eventRows.Add(group);
            }
            else
            {
                group.AddToGroup(item);
                if (group.IsExpanded) _eventRows.Add(EventListItem.Child(item, group));
            }
        }
        else _eventRows.Add(EventListItem.Single(item));
        UpdateEventCount();
        UpdateMouseGroupButton();
    }

    private void RebuildEventRows(bool expandAll = false)
    {
        _eventRows.Clear();
        EventListItem? currentGroup = null;
        foreach (var item in _events)
        {
            if (item.Kind == MacroEventKind.MouseMove)
            {
                if (currentGroup is null)
                {
                    currentGroup = EventListItem.Group(item);
                    _eventRows.Add(currentGroup);
                }
                else currentGroup.AddToGroup(item);
            }
            else
            {
                currentGroup = null;
                _eventRows.Add(EventListItem.Single(item));
            }
        }
        if (expandAll)
            foreach (var group in _eventRows.Where(x => x.IsMouseMoveGroup).ToList()) ExpandGroup(group);
        UpdateEventCount();
        UpdateMouseGroupButton();
    }

    private void ExpandGroup(EventListItem group)
    {
        if (!group.IsMouseMoveGroup || group.IsExpanded) return;
        var index = _eventRows.IndexOf(group) + 1;
        foreach (var item in group.SourceEvents) _eventRows.Insert(index++, EventListItem.Child(item, group));
        group.IsExpanded = true;
    }

    private void CollapseGroup(EventListItem group)
    {
        if (!group.IsMouseMoveGroup || !group.IsExpanded) return;
        var index = _eventRows.IndexOf(group) + 1;
        while (index < _eventRows.Count && ReferenceEquals(_eventRows[index].ParentGroup, group)) _eventRows.RemoveAt(index);
        group.IsExpanded = false;
    }

    private void ToggleMouseGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: EventListItem group }) return;
        if (group.IsExpanded) CollapseGroup(group); else ExpandGroup(group);
        UpdateMouseGroupButton();
        e.Handled = true;
    }

    private void ToggleMouseGroups_Click(object sender, RoutedEventArgs e)
    {
        var groups = _eventRows.Where(x => x.IsMouseMoveGroup).ToList();
        var expand = groups.Any(x => !x.IsExpanded);
        foreach (var group in groups)
            if (expand) ExpandGroup(group); else CollapseGroup(group);
        UpdateMouseGroupButton();
    }

    private void OpenToolMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } placementTarget) return;
        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void UpdateMouseGroupButton()
    {
        if (ToggleMovesButton is null) return;
        var groups = _eventRows.Where(x => x.IsMouseMoveGroup).ToList();
        ToggleMovesButton.IsEnabled = groups.Count > 0;
        ToggleMovesButton.Content = LocalizationService.T(groups.Count > 0 && groups.All(x => x.IsExpanded) ? "CollapseMouseMoves" : "ExpandMouseMoves");
    }

    private void UpdateEventCount() => EventCountText.Text = LocalizationService.F("EventCountFormat", _events.Count);
    private void UpdateHotkeyFooter() => HotkeyFooterText.Text = LocalizationService.F("HotkeyFooterFormat", _settings.RecordHotkey, _settings.PlayHotkey, _settings.PauseHotkey, _settings.StopHotkey);
    private void UpdateTargetText() => TargetText.Text = _target is null ? LocalizationService.T("NoTargetWindow") : LocalizationService.F("TargetPrefix", _target);

    private void DeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        var selected = EventList.SelectedItems.Cast<EventListItem>().SelectMany(x => x.SourceEvents).ToHashSet();
        if (selected.Count == 0) return;
        foreach (var item in _events.Where(selected.Contains).ToList()) _events.Remove(item);
        RebuildEventRows();
    }
    private void EditEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_recording || _countingDown || _player.IsPlaying) return;
        var rows = EventList.SelectedItems.Cast<EventListItem>().ToList();
        if (rows.Count != 1) { WpfMessageBox.Show(this, LocalizationService.T("SelectOneActionToEdit")); return; }
        var row = rows[0];
        if (row.IsMouseMoveGroup && !row.IsChild) { WpfMessageBox.Show(this, LocalizationService.T("ExpandMouseMovesToEdit")); return; }
        var selected = row.Event ?? row.SourceEvents.FirstOrDefault();
        if (selected?.Kind == MacroEventKind.RandomBranch) { RandomBranch_Click(sender, e); return; }
        if (selected is null || !ActionEditService.IsEditable(selected)) { WpfMessageBox.Show(this, LocalizationService.T("UnsupportedActionEdit")); return; }
        var source = ActionEditService.FindLogicalAction(_events.ToList(), selected);
        if (source.Count == 0) return;
        var dialog = new ActionEditorWindow(source[0], source.Count > 1 ? source[1] : null, _settings.DarkMode) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ReplacementEvents.Count == 0) return;
        var replacements = dialog.ReplacementEvents;
        var rebuilt = ActionEditService.ReplaceLogicalAction(_events.ToList(), source, replacements);
        _events.Clear(); foreach (var item in rebuilt) _events.Add(item);
        RebuildEventRows();
        EventList.SelectedItem = _eventRows.FirstOrDefault(x => x.SourceEvents.Any(item => ReferenceEquals(item, replacements[0])));
        if (EventList.SelectedItem is not null) EventList.ScrollIntoView(EventList.SelectedItem);
    }
    private void InsertSaveWait_Click(object sender, RoutedEventArgs e)
    {
        var timeoutSeconds = int.TryParse(SaveDialogTimeoutBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 60) : 15;
        var selectedEvent = (EventList.SelectedItem as EventListItem)?.SourceEvents.FirstOrDefault();
        var index = selectedEvent is null ? _events.Count : _events.IndexOf(selectedEvent);
        var time = index < _events.Count ? _events[index].TimeMs : (_events.Count == 0 ? 0 : _events[^1].TimeMs);
        var inserted = new MacroEvent { TimeMs = time, Kind = MacroEventKind.WaitForSaveDialog, TimeoutMs = timeoutSeconds * 1000 };
        _events.Insert(index, inserted);
        RebuildEventRows();
        EventList.SelectedItem = _eventRows.FirstOrDefault(x => ReferenceEquals(x.Event, inserted));
    }
    private void Variables_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VariablesWindow(_variables, _variableGroups, _settings.DarkMode) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _variables = dialog.Variables;
            _variableGroups = dialog.VariableGroups;
        }
    }
    private void InsertClipboardVariable_Click(object sender, RoutedEventArgs e)
    {
        if (_variables.Count == 0)
        {
            Variables_Click(sender, e);
            if (_variables.Count == 0) { WpfMessageBox.Show(this, LocalizationService.T("NoVariables")); return; }
        }
        var dialog = new ClipboardEventWindow(_variables.Keys, _variableGroups.Keys, _settings.DarkMode) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        InsertEventAtSelection(new MacroEvent
        {
            Kind = MacroEventKind.SetClipboardVariable,
            VariableName = dialog.VariableName,
            RandomFromVariableGroup = dialog.RandomFromVariableGroup,
            VariableGroupName = dialog.VariableGroupName
        });
    }
    private void InsertTextTrigger_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextTriggerWindow(_target, _settings.DarkMode) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.TargetWindow is null) return;
        InsertEventAtSelection(new MacroEvent
        {
            Kind = MacroEventKind.WaitForWindowText,
            WaitText = dialog.ExpectedText,
            TextExactMatch = dialog.ExactMatch,
            TextTargetWindow = dialog.TargetWindow,
            TimeoutMs = dialog.TimeoutMs
        });
    }
    private void RandomBranch_Click(object sender, RoutedEventArgs e)
    {
        var selected = EventList.SelectedItems.Cast<EventListItem>().SelectMany(x => x.SourceEvents).Distinct().ToList();
        var editing = selected.Count == 1 && selected[0].Kind == MacroEventKind.RandomBranch ? selected[0] : null;
        var source = _events.Where(x => !ReferenceEquals(x, editing) && x.Kind != MacroEventKind.RandomBranch).ToList();
        var initial = selected.Where(source.Contains).ToList();
        var dialog = new RandomBranchWindow(source, initial, editing?.Branches,
            TargetRadio.IsChecked == true ? CoordinateMode.TargetWindow : CoordinateMode.AbsoluteScreen, _target, _settings.DarkMode) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var consumed = dialog.ConsumedEvents;
        var insertionIndex = editing is null
            ? consumed.Select(x => _events.IndexOf(x)).Where(x => x >= 0).DefaultIfEmpty(_events.Count).Min()
            : _events.IndexOf(editing);
        var time = insertionIndex >= 0 && insertionIndex < _events.Count ? _events[insertionIndex].TimeMs : (_events.Count == 0 ? 0 : _events[^1].TimeMs);
        foreach (var item in _events.Where(consumed.Contains).ToList()) _events.Remove(item);
        if (editing is null)
        {
            var branchEvent = new MacroEvent { TimeMs = time, Kind = MacroEventKind.RandomBranch, Branches = dialog.Branches };
            _events.Insert(Math.Clamp(insertionIndex, 0, _events.Count), branchEvent);
            editing = branchEvent;
        }
        else editing.Branches = dialog.Branches;
        RebuildEventRows();
        EventList.SelectedItem = _eventRows.FirstOrDefault(x => ReferenceEquals(x.Event, editing));
    }
    private void InsertEventAtSelection(MacroEvent inserted)
    {
        var selectedEvent = (EventList.SelectedItem as EventListItem)?.SourceEvents.FirstOrDefault();
        var index = selectedEvent is null ? _events.Count : _events.IndexOf(selectedEvent);
        inserted.TimeMs = index < _events.Count ? _events[index].TimeMs : (_events.Count == 0 ? 0 : _events[^1].TimeMs);
        _events.Insert(index, inserted);
        RebuildEventRows();
        EventList.SelectedItem = _eventRows.FirstOrDefault(x => ReferenceEquals(x.Event, inserted));
    }
    private void EventList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteEvent_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { EditEvent_Click(sender, e); e.Handled = true; }
    }
    private void EventList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { EditEvent_Click(sender, e); e.Handled = true; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog { Filter = LocalizationService.T("MacroFilter"), FileName = "macro.mumacro" };
        if (dialog.ShowDialog() != true) return;
        var doc = new MacroDocument { CoordinateMode = TargetRadio.IsChecked == true ? CoordinateMode.TargetWindow : CoordinateMode.AbsoluteScreen, TargetWindow = _target, Variables = new(_variables, StringComparer.OrdinalIgnoreCase), VariableGroups = CloneVariableGroups(_variableGroups), Events = _events.ToList() };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(doc, JsonOptions())); _settings.LastMacroPath = dialog.FileName; SaveSettings(_settings); SetIdle(LocalizationService.T("MacroSaved"));
    }
    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog { Filter = LocalizationService.T("MacroOpenFilter") }; if (dialog.ShowDialog() != true) return;
        try { var doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(dialog.FileName), JsonOptions()) ?? throw new InvalidDataException(); if (!MacroDocument.IsSupportedFormatVersion(doc.FormatVersion)) throw new InvalidDataException(LocalizationService.T("UnsupportedFormat")); _events.Clear(); foreach (var x in doc.Events.OrderBy(x => x.TimeMs)) _events.Add(x); _variables = new(doc.Variables ?? [], StringComparer.OrdinalIgnoreCase); _variableGroups = CloneVariableGroups(doc.VariableGroups ?? []); RebuildEventRows(); _target = doc.TargetWindow; AbsoluteRadio.IsChecked = doc.CoordinateMode == CoordinateMode.AbsoluteScreen; TargetRadio.IsChecked = doc.CoordinateMode == CoordinateMode.TargetWindow; UpdateTargetText(); _settings.LastMacroPath = dialog.FileName; SaveSettings(_settings); SetIdle(LocalizationService.T("MacroLoaded")); }
        catch (Exception ex) { WpfMessageBox.Show(LocalizationService.F("FileReadErrorFormat", ex.Message)); }
    }
    private void RestoreLastMacro()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastMacroPath) || !File.Exists(_settings.LastMacroPath)) return;
        try
        {
            var doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(_settings.LastMacroPath), JsonOptions()) ?? throw new InvalidDataException();
            if (!MacroDocument.IsSupportedFormatVersion(doc.FormatVersion)) return;
            _events.Clear();
            foreach (var x in doc.Events.OrderBy(x => x.TimeMs)) _events.Add(x);
            _variables = new(doc.Variables ?? [], StringComparer.OrdinalIgnoreCase);
            _variableGroups = CloneVariableGroups(doc.VariableGroups ?? []);
            RebuildEventRows();
            _target = doc.TargetWindow;
            AbsoluteRadio.IsChecked = doc.CoordinateMode == CoordinateMode.AbsoluteScreen;
            TargetRadio.IsChecked = doc.CoordinateMode == CoordinateMode.TargetWindow;
            UpdateTargetText();
            SetIdle(LocalizationService.T("RecentMacroLoaded"));
        }
        catch { _settings.LastMacroPath = null; SaveSettings(_settings); }
    }
    private void SelectTarget_Click(object sender, RoutedEventArgs e)
    {
        var list = new System.Windows.Controls.ListBox { Margin = new Thickness(10), MinWidth = 500, MinHeight = 350 };
        var empty = new System.Windows.Controls.TextBlock { Margin = new Thickness(10, 0, 10, 8), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), Text = LocalizationService.T("NoTargetWindows"), TextWrapping = TextWrapping.Wrap };
        void RefreshWindows()
        {
            var options = WindowLocator.GetWindows();
            list.ItemsSource = options;
            empty.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshWindows();
        var refresh = new System.Windows.Controls.Button { Content = LocalizationService.T("Refresh"), Margin = new Thickness(5), MinWidth = 80 };
        var ok = new System.Windows.Controls.Button { Content = LocalizationService.T("Select"), IsDefault = true, Margin = new Thickness(5), MinWidth = 80 }; var cancel = new System.Windows.Controls.Button { Content = LocalizationService.T("Cancel"), IsCancel = true, Margin = new Thickness(5), MinWidth = 80 };
        var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(list); panel.Children.Add(empty); var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right }; actions.Children.Add(refresh); actions.Children.Add(ok); actions.Children.Add(cancel); panel.Children.Add(actions);
        var dialog = new Window { Owner = this, Title = LocalizationService.T("SelectTarget"), Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        refresh.Click += (_, _) => RefreshWindows();
        ok.Click += (_, _) => dialog.DialogResult = list.SelectedItem is not null; if (dialog.ShowDialog() == true && list.SelectedItem is TargetWindowInfo selected) { _target = selected; UpdateTargetText(); TargetRadio.IsChecked = true; }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var previous = (_settings.RecordHotkey, _settings.PlayHotkey, _settings.PauseHotkey, _settings.StopHotkey, _settings.Language, _settings.DarkMode);
        _settings.RecordHotkey = dialog.RecordHotkey;
        _settings.PlayHotkey = dialog.PlayHotkey;
        _settings.PauseHotkey = dialog.PauseHotkey;
        _settings.StopHotkey = dialog.StopHotkey;
        _settings.Language = dialog.SelectedLanguage;
        _settings.DarkMode = dialog.DarkMode;
        try
        {
            _hotkeys?.Register(_settings);
        }
        catch (Exception ex)
        {
            (_settings.RecordHotkey, _settings.PlayHotkey, _settings.PauseHotkey, _settings.StopHotkey, _settings.Language, _settings.DarkMode) = previous;
            try { _hotkeys?.Register(_settings); } catch { }
            WpfMessageBox.Show(this, ex.Message, LocalizationService.T("HotkeyRegistrationError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.StopOnPhysicalInput = StopPhysicalBox.IsChecked == true;
        LocalizationService.Apply(_settings.Language);
        ThemeService.Apply(this, _settings.DarkMode);
        RebuildEventRows();
        UpdateTargetText();
        UpdateHotkeyFooter();
        SaveSettings(_settings);
        SetIdle(LocalizationService.T("HotkeysApplied"));
    }
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }); WpfApplication.Current.Shutdown(); } catch { SetIdle(LocalizationService.T("ElevateFailed")); }
    }
    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static Dictionary<string, List<string>> CloneVariableGroups(IReadOnlyDictionary<string, List<string>> groups) =>
        groups.ToDictionary(x => x.Key, x => (x.Value ?? []).ToList(), StringComparer.OrdinalIgnoreCase);
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuMiClick", SettingsFileName);
    private static UserSettings LoadSettings()
    {
        try
        {
            var json = File.ReadAllText(SettingsPath);
            using var parsed = JsonDocument.Parse(json);
            var isLegacySettings = !parsed.RootElement.TryGetProperty(nameof(UserSettings.SettingsVersion), out _);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions()) ?? new();
            if (isLegacySettings)
            {
                settings.SettingsVersion = 2;
                settings.StopOnPhysicalInput = true;
            }
            // Upgrade prior shipped defaults once; custom settings remain untouched.
            if (settings.RecordHotkey == "Ctrl+Alt+F8" && settings.PlayHotkey == "Ctrl+Alt+F9" && settings.PauseHotkey == "Ctrl+Alt+F10" && settings.StopHotkey == "Ctrl+Alt+F12")
            {
                settings.RecordHotkey = "F8"; settings.PlayHotkey = "F9"; settings.PauseHotkey = "F11"; settings.StopHotkey = "F7";
            }
            if (settings.RecordHotkey == "F8" && settings.PlayHotkey == "F9" && settings.PauseHotkey == "F10" && settings.StopHotkey == "F12")
            {
                settings.PauseHotkey = "F11"; settings.StopHotkey = "F7";
            }
            return settings;
        }
        catch { return new(); }
    }
    private static void SaveSettings(UserSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions())); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _settings.StabilizeSaveDialog = SaveDialogStabilizationBox.IsChecked == true;
        _settings.SaveDialogTimeoutSeconds = int.TryParse(SaveDialogTimeoutBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 60) : 15;
        _settings.InstantMouseMovement = InstantMouseBox.IsChecked == true;
        _settings.InstantMouseDelayMs = int.TryParse(InstantMouseDelayBox.Text, out var parsedMouseDelay) ? Math.Clamp(parsedMouseDelay, 0, 500) : 30;
        SaveSettings(_settings);
        _lifetime.Cancel(); StopRecording(); _player.Stop(); _hotkeys?.Dispose(); _hook.Dispose(); _tray.Visible = false; _tray.Dispose();
    }
}
