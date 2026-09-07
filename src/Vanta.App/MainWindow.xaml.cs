using System.ComponentModel;
using System.Globalization;
using System.IO;
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
    private const double HomeWidth = 804;
    private const double HomeHeight = 203;
    private const double AdvancedWidth = 860;
    private const double AdvancedHeight = 528;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly GlobalHotKeyService _hotKeyService = new();
    private readonly AutoClickService _clickService = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _holdTimer;
    private readonly string? _capturePath;
    private AppSettings _settings = new();
    private bool _isLoading = true;
    private bool _hotKeyAttached;
    private bool _isCapturingHotkey;
    private bool _isSynchronizingControls;
    private string _hotkeyBeforeCapture = "Alt + Q";
    private ModifierKeys _capturedModifiers;
    private TextBox? _activeHotkeyBox;

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

        LoadSettings();

        var captureView = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--capture-view=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--capture-view=".Length);
        if (string.Equals(captureView, "advanced", StringComparison.OrdinalIgnoreCase))
        {
            AdvancedNav.IsChecked = true;
            ShowView(AdvancedView);
        }
        else if (string.Equals(captureView, "settings", StringComparison.OrdinalIgnoreCase))
        {
            SettingsNav.IsChecked = true;
            ShowView(SettingsView);
        }

        _hotKeyService.Pressed += HotKeyService_Pressed;
        SourceInitialized += MainWindow_SourceInitialized;
        ContentRendered += MainWindow_ContentRendered;
        Closing += MainWindow_Closing;
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        var period = string.IsNullOrWhiteSpace(_settings.CadencePeriod) ? "Second" : _settings.CadencePeriod;
        var visibleRate = _settings.CadenceDisplayValue is > 0
            ? Math.Clamp(_settings.CadenceDisplayValue.Value, 0.1, 60_000)
            : _settings.IsDelayMode
                ? Math.Clamp(_settings.CadenceValue, 0.1, 60_000)
                : ToVisibleRate(_settings.CadenceValue, period);

        CadenceValueBox.Text = visibleRate.ToString("0.##", CultureInfo.InvariantCulture);
        SelectComboItem(CadenceUnitCombo, period);
        AdvancedCadenceValueBox.Text = CadenceValueBox.Text;
        SelectComboItem(AdvancedCadenceUnitCombo, period);
        AdvancedRateMode.IsChecked = !_settings.IsDelayMode;
        AdvancedDelayMode.IsChecked = _settings.IsDelayMode;

        var hotkeyDisplay = FormatHotkeyDisplay(_settings.HotKeyModifier, _settings.HotKey);
        HotkeyInputBox.Text = hotkeyDisplay;
        AdvancedHotkeyInputBox.Text = hotkeyDisplay;
        SelectComboItem(ActivationCombo, _settings.ActivationMode);
        SelectComboItem(MouseButtonCombo, _settings.MouseButton);
        AdvancedToggleMode.IsChecked = !string.Equals(_settings.ActivationMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedHoldMode.IsChecked = string.Equals(_settings.ActivationMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseLeft.IsChecked = string.Equals(_settings.MouseButton, "Left", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseMiddle.IsChecked = string.Equals(_settings.MouseButton, "Middle", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseRight.IsChecked = string.Equals(_settings.MouseButton, "Right", StringComparison.OrdinalIgnoreCase);

        AdvancedClickDurationBox.Text = _settings.ClickDurationPercent.ToString(CultureInfo.InvariantCulture);
        AdvancedLimitOff.IsChecked = !_settings.LimitEnabled;
        AdvancedLimitOn.IsChecked = _settings.LimitEnabled;
        AdvancedLimitValueBox.Text = _settings.LimitValue.ToString(CultureInfo.InvariantCulture);
        AdvancedLimitClicks.IsChecked = string.Equals(_settings.LimitType, "Clicks", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitTime.IsChecked = !string.Equals(_settings.LimitType, "Clicks", StringComparison.OrdinalIgnoreCase);
        AdvancedVariationBox.Text = _settings.VariationPercent.ToString(CultureInfo.InvariantCulture);
        AdvancedVariationOff.IsChecked = !_settings.VariationEnabled;
        AdvancedVariationOn.IsChecked = _settings.VariationEnabled;
        AdvancedDoubleClickBox.Text = _settings.DoubleClickGapMs.ToString(CultureInfo.InvariantCulture);
        AdvancedDoubleOff.IsChecked = !_settings.DoubleClickEnabled;
        AdvancedDoubleOn.IsChecked = _settings.DoubleClickEnabled;
        AdvancedSequenceOff.IsChecked = !_settings.SequenceEnabled;
        AdvancedSequenceOn.IsChecked = _settings.SequenceEnabled;
        RefreshSequencePoints();
        Topmost = _settings.AlwaysOnTop;
        UpdatePinVisual();

        _isLoading = false;
    }

    private void ReadSettingsFromControls()
    {
        var visibleRate = ParseDouble(AdvancedCadenceValueBox.Text, 10, 0.1, 60_000);
        _settings.CadencePeriod = SelectedValue(AdvancedCadenceUnitCombo, "Second");
        _settings.CadenceDisplayValue = visibleRate;
        _settings.IsDelayMode = AdvancedDelayMode.IsChecked == true;
        _settings.CadenceValue = _settings.IsDelayMode
            ? visibleRate
            : ToClicksPerSecond(visibleRate, _settings.CadencePeriod);
        _settings.ActivationMode = AdvancedHoldMode.IsChecked == true ? "Hold" : "Toggle";
        _settings.MouseButton = AdvancedMouseRight.IsChecked == true
            ? "Right"
            : AdvancedMouseMiddle.IsChecked == true ? "Middle" : "Left";
        _settings.ClickDurationPercent = ParseInt(AdvancedClickDurationBox.Text, 15, 1, 100);
        _settings.LimitEnabled = AdvancedLimitOn.IsChecked == true;
        _settings.LimitValue = ParseInt(AdvancedLimitValueBox.Text, 1000, 1, 1_000_000);
        _settings.LimitType = AdvancedLimitTime.IsChecked == true ? "Seconds" : "Clicks";
        _settings.VariationEnabled = AdvancedVariationOn.IsChecked == true;
        _settings.VariationPercent = ParseInt(AdvancedVariationBox.Text, 10, 0, 100);
        _settings.DoubleClickEnabled = AdvancedDoubleOn.IsChecked == true;
        _settings.DoubleClickGapMs = ParseInt(AdvancedDoubleClickBox.Text, 50, 1, 1000);
        _settings.SequenceEnabled = AdvancedSequenceOn.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
    }

    private void SaveSettings()
    {
        if (_isLoading)
        {
            return;
        }

        ReadSettingsFromControls();
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Settings persistence must never interrupt the clicker.
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (string.IsNullOrWhiteSpace(_capturePath))
        {
            _hotKeyService.Attach(handle);
            _hotKeyAttached = true;
            RegisterHotKey();
        }

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
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Vanta hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HotKeyService_Pressed(object? sender, EventArgs e)
    {
        ReadSettingsFromControls();

        if (_settings.ActivationMode == "Hold")
        {
            if (!_clickService.IsRunning)
            {
                _clickService.Start(_settings);
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
            _clickService.Start(_settings);
        }
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

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowView(HomeView);

    private void AdvancedNav_Click(object sender, RoutedEventArgs e) => ShowView(AdvancedView);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowView(SettingsView);

    private void ShowView(UIElement view)
    {
        HomeView.Visibility = view == HomeView ? Visibility.Visible : Visibility.Collapsed;
        AdvancedView.Visibility = view == AdvancedView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == SettingsView ? Visibility.Visible : Visibility.Collapsed;
        ResizeForView(view);
    }

    private void ResizeForView(UIElement view)
    {
        var targetWidth = view == AdvancedView ? AdvancedWidth : HomeWidth;
        var targetHeight = view == AdvancedView ? AdvancedHeight : HomeHeight;

        if (IsLoaded)
        {
            Left -= (targetWidth - ActualWidth) / 2d;
            Top -= (targetHeight - ActualHeight) / 2d;
        }

        Width = targetWidth;
        Height = targetHeight;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinVisual();
        ScheduleSave();
    }

    private void UpdatePinVisual()
    {
        PinIcon.Foreground = Topmost ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(241, 241, 242));
        PinButton.Background = Topmost ? new SolidColorBrush(Color.FromRgb(8, 46, 79)) : Brushes.Transparent;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSynchronizingControls)
        {
            return;
        }

        _isSynchronizingControls = true;
        try
        {
            if (ReferenceEquals(sender, CadenceValueBox))
            {
                AdvancedCadenceValueBox.Text = CadenceValueBox.Text;
            }
            else if (ReferenceEquals(sender, CadenceUnitCombo))
            {
                SelectComboItem(AdvancedCadenceUnitCombo, SelectedValue(CadenceUnitCombo, "Second"));
            }
            else if (ReferenceEquals(sender, ActivationCombo))
            {
                var hold = string.Equals(SelectedText(ActivationCombo, "Toggle"), "Hold", StringComparison.OrdinalIgnoreCase);
                AdvancedHoldMode.IsChecked = hold;
                AdvancedToggleMode.IsChecked = !hold;
            }
            else if (ReferenceEquals(sender, MouseButtonCombo))
            {
                var button = SelectedText(MouseButtonCombo, "Left");
                AdvancedMouseLeft.IsChecked = string.Equals(button, "Left", StringComparison.OrdinalIgnoreCase);
                AdvancedMouseMiddle.IsChecked = string.Equals(button, "Middle", StringComparison.OrdinalIgnoreCase);
                AdvancedMouseRight.IsChecked = string.Equals(button, "Right", StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _isSynchronizingControls = false;
        }

        ScheduleSave();
    }

    private void AdvancedSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSynchronizingControls)
        {
            return;
        }

        _isSynchronizingControls = true;
        try
        {
            if (ReferenceEquals(sender, AdvancedCadenceValueBox))
            {
                CadenceValueBox.Text = AdvancedCadenceValueBox.Text;
            }
            else if (ReferenceEquals(sender, AdvancedCadenceUnitCombo))
            {
                SelectComboItem(CadenceUnitCombo, SelectedValue(AdvancedCadenceUnitCombo, "Second"));
            }
            else if (ReferenceEquals(sender, AdvancedToggleMode) || ReferenceEquals(sender, AdvancedHoldMode))
            {
                SelectComboItem(ActivationCombo, AdvancedHoldMode.IsChecked == true ? "Hold" : "Toggle");
            }
            else if (ReferenceEquals(sender, AdvancedMouseLeft)
                || ReferenceEquals(sender, AdvancedMouseMiddle)
                || ReferenceEquals(sender, AdvancedMouseRight))
            {
                var button = AdvancedMouseRight.IsChecked == true
                    ? "Right"
                    : AdvancedMouseMiddle.IsChecked == true ? "Middle" : "Left";
                SelectComboItem(MouseButtonCombo, button);
            }
        }
        finally
        {
            _isSynchronizingControls = false;
        }

        ScheduleSave();
    }

    private void HotkeyInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _activeHotkeyBox = (TextBox)sender;
        _hotkeyBeforeCapture = FormatHotkeyDisplay(_settings.HotKeyModifier, _settings.HotKey);
        _isCapturingHotkey = true;
        _capturedModifiers = ModifierKeys.None;
        _activeHotkeyBox.Text = "Press keys...";
        _hotKeyService.Unregister();
    }

    private void HotkeyInputBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isCapturingHotkey)
        {
            return;
        }

        _isCapturingHotkey = false;
        SetHotkeyDisplays(_hotkeyBeforeCapture);
        _activeHotkeyBox = null;
        RegisterHotKey();
    }

    private void HotkeyInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == Key.Escape)
        {
            _isCapturingHotkey = false;
            SetHotkeyDisplays(_hotkeyBeforeCapture);
            _activeHotkeyBox = null;
            Keyboard.ClearFocus();
            RegisterHotKey();
            return;
        }

        if (IsModifierKey(key))
        {
            _capturedModifiers |= ModifierForKey(key);
            if (_activeHotkeyBox is not null)
            {
                _activeHotkeyBox.Text = $"{FormatModifiers(_capturedModifiers)} + ...";
            }
            return;
        }

        var modifiers = ReadActiveModifiers(e) | _capturedModifiers;
        _settings.HotKeyModifier = FormatModifiers(modifiers);
        _settings.HotKey = key.ToString();
        SetHotkeyDisplays(FormatHotkeyDisplay(_settings.HotKeyModifier, _settings.HotKey));
        _isCapturingHotkey = false;
        _activeHotkeyBox = null;
        Keyboard.ClearFocus();
        SaveSettings();
        RegisterHotKey();
    }

    private void ScheduleSave()
    {
        if (_isLoading || _saveTimer is null || !string.IsNullOrWhiteSpace(_capturePath))
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

    private void IntegerOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void AddCursorPoint_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        _settings.SequencePoints.Add(new ScreenPoint { X = cursor.X, Y = cursor.Y });
        RefreshSequencePoints();
        ScheduleSave();
    }

    private void RefreshSequencePoints()
    {
        SequencePointsList.ItemsSource = null;
        SequencePointsList.ItemsSource = _settings.SequencePoints;
        SequenceEmptyText.Visibility = _settings.SequencePoints.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetHotkeyDisplays(string value)
    {
        HotkeyInputBox.Text = value;
        AdvancedHotkeyInputBox.Text = value;
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

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _holdTimer.Stop();
        _saveTimer.Stop();
        if (string.IsNullOrWhiteSpace(_capturePath))
        {
            SaveSettings();
        }
        _clickService.Dispose();
        _hotKeyService.Dispose();
    }

    private static string SelectedText(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static string SelectedValue(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? item.Content?.ToString() ?? fallback
            : fallback;
    }

    private static void SelectComboItem(ComboBox comboBox, string content)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), content, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static double ParseDouble(string value, double fallback, double minimum, double maximum) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static int ParseInt(string value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static double ToVisibleRate(double clicksPerSecond, string period) => period switch
    {
        "Millisecond" => clicksPerSecond / 1000d,
        "Minute" => clicksPerSecond * 60d,
        "Hour" => clicksPerSecond * 3600d,
        _ => clicksPerSecond
    };

    private static double ToClicksPerSecond(double visibleRate, string period) => period switch
    {
        "Millisecond" => visibleRate * 1000d,
        "Minute" => visibleRate / 60d,
        "Hour" => visibleRate / 3600d,
        _ => visibleRate
    };

    private static bool IsModifierKey(Key key) => key is
        Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static ModifierKeys ModifierForKey(Key key) => key switch
    {
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    private static ModifierKeys ReadActiveModifiers(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.System || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? "None" : string.Join(" + ", parts);
    }

    private static string FormatHotkeyDisplay(string modifiers, string keyName)
    {
        var hasShift = modifiers.Split('+', StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase));
        var key = Enum.TryParse<Key>(keyName, true, out var parsed) ? parsed : Key.Q;
        var displayKey = KeyDisplayName(key, hasShift);
        return string.Equals(modifiers, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(modifiers)
            ? displayKey
            : $"{modifiers} + {displayKey}";
    }

    private static string KeyDisplayName(Key key, bool shift)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return key.ToString()[1..];
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num {key.ToString()[6..]}";
        }

        return key switch
        {
            Key.OemPlus => shift ? "+" : "=",
            Key.OemMinus => shift ? "_" : "-",
            Key.OemComma => shift ? "<" : ",",
            Key.OemPeriod => shift ? ">" : ".",
            Key.OemQuestion => shift ? "?" : "/",
            Key.OemSemicolon => shift ? ":" : ";",
            Key.OemQuotes => shift ? "\"" : "'",
            Key.OemOpenBrackets => shift ? "{" : "[",
            Key.OemCloseBrackets => shift ? "}" : "]",
            Key.OemPipe => shift ? "|" : "\\",
            Key.OemTilde => shift ? "~" : "`",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            _ => key.ToString()
        };
    }
}
