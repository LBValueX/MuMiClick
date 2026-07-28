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
    private readonly InputHook _hook = new();
    private readonly MacroPlayer _player = new();
    private readonly DispatcherTimer _displayTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Forms.NotifyIcon _tray;
    private HotkeyManager? _hotkeys;
    private UserSettings _settings = new();
    private TargetWindowInfo? _target;
    private CancellationTokenSource? _countdownCancel;
    private bool _recording, _countingDown;
    private bool _saveDialogActiveDuringRecording;
    private Stopwatch _recordWatch = new();
    private const string SettingsFileName = "settings.json";

    public MainWindow()
    {
        InitializeComponent();
        EventList.ItemsSource = _events;
        _settings = LoadSettings();
        RecordHotkeyBox.Text = _settings.RecordHotkey; PlayHotkeyBox.Text = _settings.PlayHotkey; PauseHotkeyBox.Text = _settings.PauseHotkey; StopHotkeyBox.Text = _settings.StopHotkey; StopPhysicalBox.IsChecked = _settings.StopOnPhysicalInput;
        EasyModeBox.IsChecked = _settings.SimpleMode;
        SaveDialogStabilizationBox.IsChecked = _settings.StabilizeSaveDialog;
        SaveDialogTimeoutBox.Text = Math.Clamp(_settings.SaveDialogTimeoutSeconds, 1, 60).ToString();
        _hook.Recorded += CaptureEvent;
        _hook.PhysicalInput += () => { if (_player.IsPlaying && StopPhysicalBox.IsChecked == true) Dispatcher.BeginInvoke(StopPlayback); };
        _player.Progress += (loop, total, progress) => Dispatcher.BeginInvoke(() => { StatusText.Text = $"상태: 재생 중 ({loop}/{(total == long.MaxValue ? "∞" : total)})"; DetailText.Text = $"진행률 {progress:P0}"; ElapsedText.Text = progress.ToString("P0"); UpdateControls(); });
        _player.Completed += text => Dispatcher.BeginInvoke(() => { SetIdle(text); UpdateControls(); });
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _displayTimer.Tick += (_, _) => { if (_recording) ElapsedText.Text = _recordWatch.Elapsed.ToString(@"mm\:ss\.f"); };
        _displayTimer.Start();
        _tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "MuMiClick - 대기" };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        Loaded += (_, _) => { InitializeNative(); RestoreLastMacro(); ApplyDisplayMode(); };
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
        AdvancedSettingsPanel.Visibility = advancedVisibility;
        CoordinatePanel.Visibility = advancedVisibility;
        AdvancedWorkspace.Visibility = advancedVisibility;
        AdvancedFooter.Visibility = advancedVisibility;
        AdvancedRow.Height = new GridLength(simple ? 0 : 1, simple ? GridUnitType.Pixel : GridUnitType.Star);
        FooterRow.Height = new GridLength(simple ? 0 : 1, simple ? GridUnitType.Pixel : GridUnitType.Auto);
        if (simple) Height = 340;
        else if (Height < 620) Height = 720;
    }
    private async Task BeginRecordingAsync()
    {
        if (_player.IsPlaying) return;
        if (_recording || _countingDown) return;
        if (TargetRadio.IsChecked == true && _target is null) { WpfMessageBox.Show("창 상대 좌표 모드에서는 대상 창을 선택해야 합니다."); return; }
        _countingDown = true; _countdownCancel?.Dispose(); var countdown = _countdownCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (!int.TryParse(CountdownBox.Text, out var seconds) || seconds < 0) seconds = 0;
        try { for (var i = seconds; i > 0; i--) { StatusText.Text = $"상태: 녹화 시작까지 {i}초"; DetailText.Text = "카운트다운 중에는 입력을 녹화하지 않습니다."; await Task.Delay(1000, countdown.Token); } }
        catch (OperationCanceledException) { return; }
        finally { if (ReferenceEquals(_countdownCancel, countdown)) { _countingDown = false; _countdownCancel = null; } countdown.Dispose(); }
        _events.Clear(); _saveDialogActiveDuringRecording = false; _recordWatch.Restart(); _hook.StartRecording(); _recording = true;
        StatusText.Text = "상태: ● 녹화 중"; StatusText.Foreground = System.Windows.Media.Brushes.Crimson; DetailText.Text = "트레이 아이콘에도 녹화 중 상태가 표시됩니다. F8 또는 정지로 종료"; _tray.Text = "MuMiClick - 녹화 중"; UpdateControls();
    }
    private void StopRecording()
    {
        if (!_recording && !_countingDown) return;
        _countdownCancel?.Cancel(); _countingDown = false; _hook.StopRecording(); _recordWatch.Stop(); _recording = false; SetIdle("녹화가 종료되었습니다."); UpdateControls();
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
        Dispatcher.BeginInvoke(() => { if (waitEvent is not null) _events.Add(waitEvent); _events.Add(e); EventCountText.Text = $"이벤트 {_events.Count}개"; });
    }
    private async Task BeginPlaybackAsync()
    {
        if (_recording || _countingDown) { WpfMessageBox.Show("녹화를 먼저 끝내세요."); return; }
        if (_player.IsPlaying) return;
        var infinite = InfiniteBox.IsChecked == true;
        var repeat = 1;
        if (!infinite && (!int.TryParse(RepeatBox.Text, out repeat) || repeat < 1 || repeat > 1000000)) { WpfMessageBox.Show("반복 횟수는 1~1,000,000 사이여야 합니다."); return; }
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 0) { WpfMessageBox.Show("반복 간격은 0 이상의 밀리초입니다."); return; }
        var speed = double.Parse(((System.Windows.Controls.ComboBoxItem)SpeedBox.SelectedItem).Content!.ToString()![..^1], System.Globalization.CultureInfo.InvariantCulture);
        var doc = new MacroDocument { Events = _events.ToList(), CoordinateMode = TargetRadio.IsChecked == true ? CoordinateMode.TargetWindow : CoordinateMode.AbsoluteScreen, TargetWindow = _target };
        StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen; StatusText.Text = "상태: 재생 준비"; _tray.Text = "MuMiClick - 재생 중"; UpdateControls();
        await _player.PlayAsync(doc, repeat, infinite, speed, interval, _lifetime.Token);
    }
    private void TogglePause()
    {
        _player.TogglePause();
        if (_player.IsPlaying)
        {
            DetailText.Text = _player.IsPaused ? "일시정지됨 — F11을 누르면 재개합니다." : "재생을 재개했습니다.";
            StatusText.Text = _player.IsPaused ? "상태: 일시정지" : "상태: 재생 중";
        }
    }
    private void StopPlayback() { if (_player.IsPlaying) _player.Stop(); else _player.ReleaseAll(); }
    private void SetIdle(string message)
    {
        StatusText.Text = "상태: 대기"; StatusText.Foreground = System.Windows.Media.Brushes.Black; DetailText.Text = message; _tray.Text = "MuMiClick - 대기"; if (!_recording) ElapsedText.Text = "00:00.0";
    }
    private void UpdateControls()
    {
        // Prevent accidental recording or replay requests while the playback engine owns input.
        RecordButton.IsEnabled = !_player.IsPlaying;
        PlayButton.IsEnabled = !_recording && !_countingDown && !_player.IsPlaying;
    }
    private void DeleteEvent_Click(object sender, RoutedEventArgs e) { if (EventList.SelectedItem is MacroEvent selected) _events.Remove(selected); EventCountText.Text = $"이벤트 {_events.Count}개"; }
    private void InsertSaveWait_Click(object sender, RoutedEventArgs e)
    {
        var timeoutSeconds = int.TryParse(SaveDialogTimeoutBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 60) : 15;
        var index = EventList.SelectedIndex >= 0 ? EventList.SelectedIndex : _events.Count;
        var time = index < _events.Count ? _events[index].TimeMs : (_events.Count == 0 ? 0 : _events[^1].TimeMs);
        _events.Insert(index, new MacroEvent { TimeMs = time, Kind = MacroEventKind.WaitForSaveDialog, TimeoutMs = timeoutSeconds * 1000 });
        EventCountText.Text = $"이벤트 {_events.Count}개";
        EventList.SelectedIndex = index;
    }
    private void EventList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Delete) DeleteEvent_Click(sender, e); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog { Filter = "MuMiClick 매크로 (*.mumacro)|*.mumacro|JSON (*.json)|*.json", FileName = "macro.mumacro" };
        if (dialog.ShowDialog() != true) return;
        var doc = new MacroDocument { CoordinateMode = TargetRadio.IsChecked == true ? CoordinateMode.TargetWindow : CoordinateMode.AbsoluteScreen, TargetWindow = _target, Events = _events.ToList() };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(doc, JsonOptions())); _settings.LastMacroPath = dialog.FileName; SaveSettings(_settings); SetIdle("매크로를 저장했습니다.");
    }
    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog { Filter = "MuMiClick 매크로 (*.mumacro;*.json)|*.mumacro;*.json" }; if (dialog.ShowDialog() != true) return;
        try { var doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(dialog.FileName), JsonOptions()) ?? throw new InvalidDataException(); if (doc.FormatVersion != 1) throw new InvalidDataException("지원하지 않는 매크로 형식입니다."); _events.Clear(); foreach (var x in doc.Events.OrderBy(x => x.TimeMs)) _events.Add(x); _target = doc.TargetWindow; AbsoluteRadio.IsChecked = doc.CoordinateMode == CoordinateMode.AbsoluteScreen; TargetRadio.IsChecked = doc.CoordinateMode == CoordinateMode.TargetWindow; TargetText.Text = _target is null ? "대상 창: 선택 안 됨" : "대상 창: " + _target; EventCountText.Text = $"이벤트 {_events.Count}개"; _settings.LastMacroPath = dialog.FileName; SaveSettings(_settings); SetIdle("매크로를 불러왔습니다."); }
        catch (Exception ex) { WpfMessageBox.Show("파일을 읽을 수 없습니다: " + ex.Message); }
    }
    private void RestoreLastMacro()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastMacroPath) || !File.Exists(_settings.LastMacroPath)) return;
        try
        {
            var doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(_settings.LastMacroPath), JsonOptions()) ?? throw new InvalidDataException();
            if (doc.FormatVersion != 1) return;
            _events.Clear();
            foreach (var x in doc.Events.OrderBy(x => x.TimeMs)) _events.Add(x);
            _target = doc.TargetWindow;
            AbsoluteRadio.IsChecked = doc.CoordinateMode == CoordinateMode.AbsoluteScreen;
            TargetRadio.IsChecked = doc.CoordinateMode == CoordinateMode.TargetWindow;
            TargetText.Text = _target is null ? "대상 창: 선택 안 됨" : "대상 창: " + _target;
            EventCountText.Text = $"이벤트 {_events.Count}개";
            SetIdle("최근 매크로를 자동으로 불러왔습니다.");
        }
        catch { _settings.LastMacroPath = null; SaveSettings(_settings); }
    }
    private void SelectTarget_Click(object sender, RoutedEventArgs e)
    {
        var options = WindowLocator.GetWindows(); var list = new System.Windows.Controls.ListBox { ItemsSource = options, DisplayMemberPath = "Item2", Margin = new Thickness(10), MinWidth = 500, MinHeight = 350 };
        var ok = new System.Windows.Controls.Button { Content = "선택", IsDefault = true, Margin = new Thickness(5), MinWidth = 80 }; var cancel = new System.Windows.Controls.Button { Content = "취소", IsCancel = true, Margin = new Thickness(5), MinWidth = 80 };
        var panel = new System.Windows.Controls.StackPanel(); panel.Children.Add(list); var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right }; actions.Children.Add(ok); actions.Children.Add(cancel); panel.Children.Add(actions);
        var dialog = new Window { Owner = this, Title = "대상 창 선택", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => dialog.DialogResult = list.SelectedItem is not null; if (dialog.ShowDialog() == true && list.SelectedItem is ValueTuple<IntPtr, TargetWindowInfo> selected) { _target = selected.Item2; TargetText.Text = "대상 창: " + _target; TargetRadio.IsChecked = true; }
    }
    private void ApplyHotkeys_Click(object sender, RoutedEventArgs e)
    {
        try { _settings.RecordHotkey = RecordHotkeyBox.Text; _settings.PlayHotkey = PlayHotkeyBox.Text; _settings.PauseHotkey = PauseHotkeyBox.Text; _settings.StopHotkey = StopHotkeyBox.Text; _settings.StopOnPhysicalInput = StopPhysicalBox.IsChecked == true; _hotkeys?.Register(_settings); SaveSettings(_settings); SetIdle("단축키를 적용했습니다."); }
        catch (Exception ex) { WpfMessageBox.Show(ex.Message, "단축키 오류", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }); WpfApplication.Current.Shutdown(); } catch { SetIdle("관리자 재실행이 취소되었거나 실패했습니다."); }
    }
    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuMiClick", SettingsFileName);
    private static UserSettings LoadSettings()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new();
            // Upgrade prior shipped defaults once; custom settings remain untouched.
            if (settings.RecordHotkey == "Ctrl+Alt+F8" && settings.PlayHotkey == "Ctrl+Alt+F9" && settings.PauseHotkey == "Ctrl+Alt+F10" && settings.StopHotkey == "Ctrl+Alt+F12")
                settings = new UserSettings { StopOnPhysicalInput = settings.StopOnPhysicalInput, LastMacroPath = settings.LastMacroPath, SimpleMode = settings.SimpleMode };
            if (settings.RecordHotkey == "F8" && settings.PlayHotkey == "F9" && settings.PauseHotkey == "F10" && settings.StopHotkey == "F12")
                settings = new UserSettings { StopOnPhysicalInput = settings.StopOnPhysicalInput, LastMacroPath = settings.LastMacroPath, SimpleMode = settings.SimpleMode };
            return settings;
        }
        catch { return new(); }
    }
    private static void SaveSettings(UserSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions())); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _settings.StabilizeSaveDialog = SaveDialogStabilizationBox.IsChecked == true;
        _settings.SaveDialogTimeoutSeconds = int.TryParse(SaveDialogTimeoutBox.Text, out var parsed) ? Math.Clamp(parsed, 1, 60) : 15;
        SaveSettings(_settings);
        _lifetime.Cancel(); StopRecording(); _player.Stop(); _hotkeys?.Dispose(); _hook.Dispose(); _tray.Visible = false; _tray.Dispose();
    }
}
