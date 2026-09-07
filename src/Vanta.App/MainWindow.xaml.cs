using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vanta.Interop;
using Vanta.Models;
using Vanta.Services;

namespace Vanta;

public partial class MainWindow : Window
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "Vanta";

    private readonly AppSettingsStore _settingsStore = new();
    private readonly GlobalHotKeyService _hotKeyService = new();
    private readonly AutoClickService _clickService = new();
    private readonly UpdateService _updateService = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _holdTimer;
    private AppSettings _settings = new();
    private bool _isLoading = true;
    private bool _hotKeyAttached;
    private int _testClickCount;
    private readonly string? _capturePath;

    public MainWindow()
    {
        InitializeComponent();

        _capturePath = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--capture-ui=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--capture-ui=".Length).Trim('"');

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
        };

        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(18) };
        _holdTimer.Tick += HoldTimer_Tick;

        PopulateHotKeys();
        LoadSettings();

        if (Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            DashboardView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
            SettingsNav.IsChecked = true;
        }

        _hotKeyService.Pressed += HotKeyService_Pressed;
        _clickService.Clicked += ClickService_Clicked;
        _clickService.Stopped += ClickService_Stopped;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        LocationChanged += WindowPosition_Changed;
        ContentRendered += MainWindow_ContentRendered;

        VersionText.Text = $"Version {UpdateService.CurrentVersion.ToString(3)}";

        if (Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_capturePath))
        {
            return;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(this);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.GetDirectoryName(_capturePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_capturePath);
            encoder.Save(stream);
        }
        finally
        {
            Close();
        }
    }

    private void PopulateHotKeys()
    {
        foreach (var letter in Enumerable.Range('A', 26).Select(value => ((char)value).ToString()))
        {
            HotKeyCombo.Items.Add(new ComboBoxItem { Content = letter });
        }

        foreach (var functionKey in Enumerable.Range(1, 12).Select(value => $"F{value}"))
        {
            HotKeyCombo.Items.Add(new ComboBoxItem { Content = functionKey });
        }
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        CadenceValueBox.Text = _settings.CadenceValue.ToString("0.##", CultureInfo.InvariantCulture);
        DelayModeRadio.IsChecked = _settings.IsDelayMode;
        RateModeRadio.IsChecked = !_settings.IsDelayMode;
        CadenceUnitCombo.SelectedIndex = _settings.IsDelayMode ? 1 : 0;
        SelectComboItem(ModifierCombo, _settings.HotKeyModifier);
        SelectComboItem(HotKeyCombo, _settings.HotKey);
        HoldModeRadio.IsChecked = _settings.ActivationMode == "Hold";
        ToggleModeRadio.IsChecked = _settings.ActivationMode != "Hold";
        LeftButtonRadio.IsChecked = _settings.MouseButton == "Left";
        MiddleButtonRadio.IsChecked = _settings.MouseButton == "Middle";
        RightButtonRadio.IsChecked = _settings.MouseButton == "Right";
        DurationSlider.Value = _settings.ClickDurationPercent;
        LimitEnabledCheck.IsChecked = _settings.LimitEnabled;
        LimitValueBox.Text = _settings.LimitValue.ToString(CultureInfo.InvariantCulture);
        LimitTypeCombo.SelectedIndex = _settings.LimitType == "Seconds" ? 1 : 0;
        VariationEnabledCheck.IsChecked = _settings.VariationEnabled;
        VariationSlider.Value = _settings.VariationPercent;
        DoubleClickEnabledCheck.IsChecked = _settings.DoubleClickEnabled;
        DoubleClickGapBox.Text = _settings.DoubleClickGapMs.ToString(CultureInfo.InvariantCulture);
        SequenceEnabledCheck.IsChecked = _settings.SequenceEnabled;
        StartWithWindowsCheck.IsChecked = IsStartupEnabled();
        RememberWindowCheck.IsChecked = _settings.RememberWindowPosition;
        MinimizeOnStartCheck.IsChecked = _settings.StartMinimized;
        Topmost = _settings.AlwaysOnTop;
        UpdatePinVisual();

        if (_settings.RememberWindowPosition && _settings.WindowLeft is double left && _settings.WindowTop is double top && IsVisibleOnAnyScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (_settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
        }

        RefreshSequenceList();
        _isLoading = false;
        UpdateStatus("Ready", false);
    }

    private void ReadSettingsFromControls()
    {
        _settings.CadenceValue = ParseDouble(CadenceValueBox.Text, 10, 0.1, 60_000);
        _settings.IsDelayMode = DelayModeRadio.IsChecked == true || CadenceUnitCombo.SelectedIndex == 1;
        _settings.HotKeyModifier = SelectedText(ModifierCombo, "Alt");
        _settings.HotKey = SelectedText(HotKeyCombo, "Q");
        _settings.ActivationMode = HoldModeRadio.IsChecked == true ? "Hold" : "Toggle";
        _settings.MouseButton = RightButtonRadio.IsChecked == true ? "Right" : MiddleButtonRadio.IsChecked == true ? "Middle" : "Left";
        _settings.ClickDurationPercent = (int)DurationSlider.Value;
        _settings.LimitEnabled = LimitEnabledCheck.IsChecked == true;
        _settings.LimitValue = ParseInt(LimitValueBox.Text, 1000, 1, 10_000_000);
        _settings.LimitType = LimitTypeCombo.SelectedIndex == 1 ? "Seconds" : "Clicks";
        _settings.VariationEnabled = VariationEnabledCheck.IsChecked == true;
        _settings.VariationPercent = (int)VariationSlider.Value;
        _settings.DoubleClickEnabled = DoubleClickEnabledCheck.IsChecked == true;
        _settings.DoubleClickGapMs = ParseInt(DoubleClickGapBox.Text, 50, 1, 1000);
        _settings.SequenceEnabled = SequenceEnabledCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.RememberWindowPosition = RememberWindowCheck.IsChecked == true;
        _settings.StartMinimized = MinimizeOnStartCheck.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
    }

    private void SaveSettings()
    {
        if (_isLoading)
        {
            return;
        }

        ReadSettingsFromControls();
        if (_settings.RememberWindowPosition && WindowState == WindowState.Normal)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            UpdateStatus($"Could not save settings: {exception.Message}", false);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotKeyService.Attach(handle);
        _hotKeyAttached = true;
        RegisterHotKey();

        try
        {
            const int cornerPreference = 33;
            var rounded = 2;
            NativeMethods.DwmSetWindowAttribute(handle, cornerPreference, ref rounded, sizeof(int));
        }
        catch
        {
            // Rounded corners are cosmetic and unavailable on older Windows versions.
        }
    }

    private void RegisterHotKey()
    {
        if (!_hotKeyAttached || _isLoading)
        {
            return;
        }

        ReadSettingsFromControls();
        try
        {
            _hotKeyService.Register(_settings.HotKeyModifier, _settings.HotKey);
            UpdateStatus("Ready", false);
        }
        catch (Win32Exception exception)
        {
            UpdateStatus(exception.Message, false);
        }
    }

    private void HotKeyService_Pressed(object? sender, EventArgs e)
    {
        ReadSettingsFromControls();
        if (_settings.ActivationMode == "Hold")
        {
            if (!_clickService.IsRunning)
            {
                StartClicking();
            }

            _holdTimer.Start();
            return;
        }

        if (_clickService.IsRunning)
        {
            _clickService.Stop();
        }
        else
        {
            StartClicking();
        }
    }

    private void StartClicking()
    {
        ReadSettingsFromControls();
        _clickService.Start(_settings);
        UpdateStatus("Clicking", true);
    }

    private void HoldTimer_Tick(object? sender, EventArgs e)
    {
        var virtualKey = GlobalHotKeyService.GetVirtualKey(_settings.HotKey);
        if ((NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) == 0)
        {
            _holdTimer.Stop();
            _clickService.Stop();
        }
    }

    private void ClickService_Clicked(object? sender, long count)
    {
        Dispatcher.BeginInvoke(() => ClickCountText.Text = $"{count:N0} click{(count == 1 ? string.Empty : "s")}");
    }

    private void ClickService_Stopped(object? sender, string reason)
    {
        Dispatcher.BeginInvoke(() => UpdateStatus(reason, false));
    }

    private void UpdateStatus(string text, bool active)
    {
        var hotKey = $"{SelectedText(ModifierCombo, "Alt")} + {SelectedText(HotKeyCombo, "Q")}";
        StatusText.Text = active ? $"{text} · {hotKey} to stop" : $"{text} · {hotKey}";
        StatusDot.Fill = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedBrush");
    }

    private void DashboardNav_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void SequenceNav_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        SequenceList.Focus();
    }

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
    }

    private void AddCursorButton_Click(object sender, RoutedEventArgs e)
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            _settings.SequencePoints.Add(new ScreenPoint { X = point.X, Y = point.Y });
            RefreshSequenceList();
            ScheduleSave();
        }
    }

    private void RemoveCursorButton_Click(object sender, RoutedEventArgs e)
    {
        var index = SequenceList.SelectedIndex;
        if (index >= 0 && index < _settings.SequencePoints.Count)
        {
            _settings.SequencePoints.RemoveAt(index);
            RefreshSequenceList();
            ScheduleSave();
        }
    }

    private void RefreshSequenceList()
    {
        SequenceList.ItemsSource = null;
        SequenceList.ItemsSource = _settings.SequencePoints;
    }

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationText is not null)
        {
            DurationText.Text = $"{(int)e.NewValue}%";
        }

        ScheduleSave();
    }

    private void VariationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VariationText is not null)
        {
            VariationText.Text = $"{(int)e.NewValue}%";
        }

        ScheduleSave();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e) => ScheduleSave();

    private void Hotkey_Changed(object sender, SelectionChangedEventArgs e)
    {
        ScheduleSave();
        RegisterHotKey();
    }

    private void ScheduleSave()
    {
        if (_isLoading || _saveTimer is null)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(character => char.IsDigit(character) || character == '.');
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true)
                ?? Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
            if (StartWithWindowsCheck.IsChecked == true)
            {
                key.SetValue(StartupValueName, $"\"{Environment.ProcessPath}\" --minimized");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }

            ScheduleSave();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows startup could not be changed.\n\n{exception.Message}", "Vanta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
        return key?.GetValue(StartupValueName) is not null;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking GitHub Releases…";

        try
        {
            var result = await _updateService.CheckAsync();
            UpdateStatusText.Text = result.Message;
            if (result.UpdateAvailable && result.ReleaseUrl is not null)
            {
                var open = MessageBox.Show(this, $"{result.Message}\n\nOpen the download page?", "Vanta update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                {
                    UpdateService.OpenRelease(result.ReleaseUrl);
                }
            }
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"Update check failed: {exception.Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Reset every Vanta setting to its default?", "Reset settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _clickService.Stop();
        _settingsStore.Reset();
        _isLoading = true;
        _settings = new AppSettings();
        LoadSettings();
        RegisterHotKey();
        UpdateStatusText.Text = "Settings were reset.";
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            UpdateStatusText.Text = "Windows Installed apps opened. Search for Vanta to uninstall.";
        }
        catch
        {
            Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });
        }
    }

    private void TestPad_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _testClickCount++;
        TestCountText.Text = $"{_testClickCount:N0} click{(_testClickCount == 1 ? string.Empty : "s")}";
        TestLastText.Text = $"Last input: {e.ChangedButton} · {DateTime.Now:T}";
    }

    private void ClearTestButton_Click(object sender, RoutedEventArgs e)
    {
        _testClickCount = 0;
        TestCountText.Text = "0 clicks";
        TestLastText.Text = "Waiting for input";
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinVisual();
        ScheduleSave();
    }

    private void UpdatePinVisual()
    {
        PinButton.Background = Topmost ? new SolidColorBrush(Color.FromRgb(32, 47, 35)) : Brushes.Transparent;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowPosition_Changed(object? sender, EventArgs e) => ScheduleSave();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _holdTimer.Stop();
        _saveTimer.Stop();
        SaveSettings();
        _clickService.Dispose();
        _hotKeyService.Dispose();
    }

    private static string SelectedText(ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
    }

    private static void SelectComboItem(ComboBox comboBox, string content)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static int ParseInt(string value, int fallback, int minimum, int maximum)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static double ParseDouble(string value, double fallback, double minimum, double maximum)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private static bool IsVisibleOnAnyScreen(double left, double top)
    {
        var x = left + 40;
        var y = top + 40;
        return x >= SystemParameters.VirtualScreenLeft
            && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && y >= SystemParameters.VirtualScreenTop
            && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }
}
