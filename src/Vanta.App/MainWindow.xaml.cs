using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private const double AdvancedWidth = 900;
    private const double AdvancedHeight = 490;
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
    private bool _isCapturingKeyboardTarget;
    private bool _isSynchronizingControls;
    private bool _lastDelayMode;
    private int _resizeVersion;
    private string _hotkeyBeforeCapture = "Alt + Q";
    private string _keyboardTargetBeforeCapture = "Space";
    private ModifierKeys _capturedModifiers;
    private TextBox? _activeHotkeyBox;
    private TextBox? _activeKeyboardTargetBox;

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
        ApplyCaptureState();

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

    private void ApplyCaptureState()
    {
        if (string.IsNullOrWhiteSpace(_capturePath))
        {
            return;
        }

        var stateArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--capture-state=", StringComparison.OrdinalIgnoreCase))?
            .Substring("--capture-state=".Length);
        if (string.IsNullOrWhiteSpace(stateArgument))
        {
            return;
        }

        var states = stateArgument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var state in states)
        {
            switch (state.ToLowerInvariant())
            {
                case "interval": AdvancedDelayMode.IsChecked = true; break;
                case "hold": AdvancedHoldMode.IsChecked = true; break;
                case "keyboard": AdvancedKeyboardType.IsChecked = true; break;
                case "time":
                    AdvancedLimitOn.IsChecked = true;
                    AdvancedLimitTime.IsChecked = true;
                    break;
                case "dutyhold": AdvancedDutyHoldMode.IsChecked = true; break;
                case "variation": AdvancedVariationOn.IsChecked = true; break;
            }
        }

        UpdateClickerTypeVisuals();
        UpdateAdvancedStateVisuals();
        UpdateAdvancedSummaries();
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        var period = string.IsNullOrWhiteSpace(_settings.CadencePeriod) ? "Second" : _settings.CadencePeriod;
        var periodValue = Math.Clamp(_settings.CadencePeriodValue, 0.01, 60_000);
        var delayMilliseconds = _settings.IsDelayMode
            ? Math.Clamp(_settings.CadenceValue, 1, 86_400_000)
            : 1000d / Math.Clamp(_settings.CadenceValue, 0.000001, 1000);
        var clicksPerSecond = _settings.IsDelayMode
            ? 1000d / delayMilliseconds
            : Math.Clamp(_settings.CadenceValue, 0.000001, 1000);
        var advancedVisibleRate = !_settings.IsDelayMode && _settings.CadenceDisplayValue is > 0
            ? Math.Clamp(_settings.CadenceDisplayValue.Value, 0.1, 60_000)
            : ToVisibleRate(clicksPerSecond, period) * periodValue;
        var homeVisibleRate = ToVisibleRate(clicksPerSecond, period);

        CadenceValueBox.Text = homeVisibleRate.ToString("0.##", CultureInfo.InvariantCulture);
        SelectComboItem(CadenceUnitCombo, period);
        AdvancedCadenceValueBox.Text = advancedVisibleRate.ToString("0.##", CultureInfo.InvariantCulture);
        AdvancedPeriodValueBox.Text = periodValue.ToString("0.##", CultureInfo.InvariantCulture);
        SetIntervalFields(delayMilliseconds);
        SelectComboItem(AdvancedCadenceUnitCombo, period);
        AdvancedRateMode.IsChecked = !_settings.IsDelayMode;
        AdvancedDelayMode.IsChecked = _settings.IsDelayMode;
        _lastDelayMode = _settings.IsDelayMode;

        var hotkeyDisplay = FormatHotkeyDisplay(_settings.HotKeyModifier, _settings.HotKey);
        HotkeyInputBox.Text = hotkeyDisplay;
        AdvancedHotkeyInputBox.Text = hotkeyDisplay;
        SelectComboItem(ActivationCombo, _settings.ActivationMode);
        SelectComboItem(MouseButtonCombo, _settings.MouseButton);
        AdvancedToggleMode.IsChecked = !string.Equals(_settings.ActivationMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedHoldMode.IsChecked = string.Equals(_settings.ActivationMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseType.IsChecked = !string.Equals(_settings.ClickerType, "Keyboard", StringComparison.OrdinalIgnoreCase);
        AdvancedKeyboardType.IsChecked = string.Equals(_settings.ClickerType, "Keyboard", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseLeft.IsChecked = string.Equals(_settings.MouseButton, "Left", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseMiddle.IsChecked = string.Equals(_settings.MouseButton, "Middle", StringComparison.OrdinalIgnoreCase);
        AdvancedMouseRight.IsChecked = string.Equals(_settings.MouseButton, "Right", StringComparison.OrdinalIgnoreCase);
        var keyboardTarget = ParseKeyboardTarget(_settings.KeyboardKey);
        _settings.KeyboardKey = keyboardTarget.ToString();
        SetKeyboardTargetDisplays(KeyDisplayName(keyboardTarget, false));

        AdvancedClickDurationBox.Text = _settings.ClickDurationPercent.ToString(CultureInfo.InvariantCulture);
        AdvancedDutyClickMode.IsChecked = !string.Equals(_settings.DutyCycleMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedDutyHoldMode.IsChecked = string.Equals(_settings.DutyCycleMode, "Hold", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitOff.IsChecked = !_settings.LimitEnabled;
        AdvancedLimitOn.IsChecked = _settings.LimitEnabled;
        var storedLimitIsClicks = string.Equals(_settings.LimitType, "Clicks", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitClicksValueBox.Text = (_settings.LimitClicksValue ?? (storedLimitIsClicks ? _settings.LimitValue : 1000)).ToString(CultureInfo.InvariantCulture);
        AdvancedLimitTimeValueBox.Text = (_settings.LimitTimeValue ?? (!storedLimitIsClicks ? _settings.LimitValue : 60)).ToString(CultureInfo.InvariantCulture);
        AdvancedLimitClicks.IsChecked = string.Equals(_settings.LimitType, "Clicks", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitTime.IsChecked = !string.Equals(_settings.LimitType, "Clicks", StringComparison.OrdinalIgnoreCase);
        SelectLimitTimeUnit(_settings.LimitTimeUnit);
        AdvancedVariationBox.Text = _settings.VariationPercent.ToString(CultureInfo.InvariantCulture);
        AdvancedVariationOff.IsChecked = !_settings.VariationEnabled;
        AdvancedVariationOn.IsChecked = _settings.VariationEnabled;
        AdvancedDoubleOff.IsChecked = !_settings.DoubleClickEnabled;
        AdvancedDoubleOn.IsChecked = _settings.DoubleClickEnabled;
        _settings.SequenceEnabled = false;
        UpdateClickerTypeVisuals();
        UpdateAdvancedStateVisuals();
        UpdateAdvancedSummaries();
        Topmost = _settings.AlwaysOnTop;
        UpdatePinVisual();

        _isLoading = false;
    }

    private void ReadSettingsFromControls()
    {
        var visibleRate = ParseDouble(AdvancedCadenceValueBox.Text, 10, 0.1, 60_000);
        var periodValue = ParseDouble(AdvancedPeriodValueBox.Text, 1, 0.01, 60_000);
        _settings.CadencePeriod = SelectedValue(AdvancedCadenceUnitCombo, "Second");
        _settings.CadencePeriodValue = periodValue;
        _settings.IsDelayMode = AdvancedDelayMode.IsChecked == true;
        _settings.CadenceDisplayValue = visibleRate;
        _settings.CadenceValue = _settings.IsDelayMode
            ? GetIntervalInputMilliseconds()
            : ToClicksPerSecond(visibleRate, _settings.CadencePeriod) / periodValue;
        _settings.ActivationMode = AdvancedHoldMode.IsChecked == true ? "Hold" : "Toggle";
        _settings.ClickerType = AdvancedKeyboardType.IsChecked == true ? "Keyboard" : "Mouse";
        _settings.MouseButton = AdvancedMouseRight.IsChecked == true
            ? "Right"
            : AdvancedMouseMiddle.IsChecked == true ? "Middle" : "Left";
        _settings.ClickDurationPercent = ParseInt(AdvancedClickDurationBox.Text, 15, 1, 100);
        _settings.DutyCycleMode = AdvancedDutyHoldMode.IsChecked == true ? "Hold" : "Click";
        _settings.LimitEnabled = AdvancedLimitOn.IsChecked == true;
        _settings.LimitClicksValue = ParseInt(AdvancedLimitClicksValueBox.Text, 1000, 1, 1_000_000);
        _settings.LimitTimeValue = ParseInt(AdvancedLimitTimeValueBox.Text, 60, 1, 1_000_000);
        _settings.LimitType = AdvancedLimitTime.IsChecked == true ? "Time" : "Clicks";
        _settings.LimitTimeUnit = SelectedLimitTimeUnit();
        _settings.LimitValue = AdvancedLimitTime.IsChecked == true
            ? _settings.LimitTimeValue.Value
            : _settings.LimitClicksValue.Value;
        _settings.VariationEnabled = AdvancedVariationOn.IsChecked == true;
        _settings.VariationPercent = ParseInt(AdvancedVariationBox.Text, 10, 0, 100);
        _settings.DoubleClickEnabled = AdvancedDoubleOn.IsChecked == true;
        _settings.SequenceEnabled = false;
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

        if (!IsLoaded || !string.IsNullOrWhiteSpace(_capturePath))
        {
            Width = targetWidth;
            Height = targetHeight;
            return;
        }

        var currentWidth = ActualWidth;
        var currentHeight = ActualHeight;
        var currentLeft = Left;
        var currentTop = Top;
        var minimumLeft = SystemParameters.VirtualScreenLeft;
        var maximumLeft = Math.Max(minimumLeft, minimumLeft + SystemParameters.VirtualScreenWidth - targetWidth);
        var minimumTop = SystemParameters.VirtualScreenTop;
        var maximumTop = Math.Max(minimumTop, minimumTop + SystemParameters.VirtualScreenHeight - targetHeight);
        var targetLeft = Math.Clamp(currentLeft - ((targetWidth - currentWidth) / 2d), minimumLeft, maximumLeft);
        var targetTop = Math.Clamp(currentTop - ((targetHeight - currentHeight) / 2d), minimumTop, maximumTop);
        var resizeVersion = ++_resizeVersion;

        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        Width = targetWidth;
        Height = targetHeight;
        Left = targetLeft;
        Top = targetTop;

        var duration = new Duration(TimeSpan.FromMilliseconds(190));
        var widthAnimation = CreateResizeAnimation(currentWidth, targetWidth, duration);
        var heightAnimation = CreateResizeAnimation(currentHeight, targetHeight, duration);
        var leftAnimation = CreateResizeAnimation(currentLeft, targetLeft, duration);
        var topAnimation = CreateResizeAnimation(currentTop, targetTop, duration);
        heightAnimation.Completed += (_, _) =>
        {
            if (resizeVersion != _resizeVersion)
            {
                return;
            }

            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Width = targetWidth;
            Height = targetHeight;
            Left = targetLeft;
            Top = targetTop;
        };

        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, heightAnimation);
        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private static DoubleAnimation CreateResizeAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
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
                AdvancedRateMode.IsChecked = true;
                _lastDelayMode = false;
                AdvancedCadenceValueBox.Text = CadenceValueBox.Text;
                AdvancedPeriodValueBox.Text = "1";
            }
            else if (ReferenceEquals(sender, CadenceUnitCombo))
            {
                AdvancedRateMode.IsChecked = true;
                _lastDelayMode = false;
                SelectComboItem(AdvancedCadenceUnitCombo, SelectedValue(CadenceUnitCombo, "Second"));
                AdvancedPeriodValueBox.Text = "1";
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

        UpdateAdvancedSummaries();
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
            if (ReferenceEquals(sender, AdvancedRateMode) || ReferenceEquals(sender, AdvancedDelayMode))
            {
                var delayMode = AdvancedDelayMode.IsChecked == true;
                if (delayMode != _lastDelayMode)
                {
                    if (delayMode)
                    {
                        SetIntervalFields(1000d / GetRateInputClicksPerSecond());
                    }
                    else
                    {
                        var clicksPerSecond = 1000d / GetIntervalInputMilliseconds();
                        var periodValue = ParseDouble(AdvancedPeriodValueBox.Text, 1, 0.01, 60_000);
                        var visibleRate = ToVisibleRate(clicksPerSecond, SelectedValue(AdvancedCadenceUnitCombo, "Second")) * periodValue;
                        AdvancedCadenceValueBox.Text = visibleRate.ToString("0.##", CultureInfo.InvariantCulture);
                    }

                    _lastDelayMode = delayMode;
                }

                SyncHomeCadenceFromAdvanced();
            }
            else if (ReferenceEquals(sender, AdvancedCadenceValueBox)
                || ReferenceEquals(sender, AdvancedPeriodValueBox))
            {
                SyncHomeCadenceFromAdvanced();
            }
            else if (ReferenceEquals(sender, AdvancedIntervalHoursBox)
                || ReferenceEquals(sender, AdvancedIntervalMinutesBox)
                || ReferenceEquals(sender, AdvancedIntervalSecondsBox)
                || ReferenceEquals(sender, AdvancedIntervalMillisecondsBox))
            {
                SyncHomeCadenceFromAdvanced();
            }
            else if (ReferenceEquals(sender, AdvancedCadenceUnitCombo))
            {
                SelectComboItem(CadenceUnitCombo, SelectedValue(AdvancedCadenceUnitCombo, "Second"));
                SyncHomeCadenceFromAdvanced();
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

        UpdateClickerTypeVisuals();
        UpdateAdvancedStateVisuals();
        UpdateAdvancedSummaries();
        ScheduleSave();
    }

    private void SyncHomeCadenceFromAdvanced()
    {
        var clicksPerSecond = AdvancedDelayMode.IsChecked == true
            ? 1000d / GetIntervalInputMilliseconds()
            : GetRateInputClicksPerSecond();
        var homeVisibleRate = ToVisibleRate(clicksPerSecond, SelectedValue(AdvancedCadenceUnitCombo, "Second"));
        CadenceValueBox.Text = homeVisibleRate.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void UpdateClickerTypeVisuals()
    {
        var keyboard = AdvancedKeyboardType.IsChecked == true;
        AdvancedMouseOptions.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
        AdvancedKeyboardOptions.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        AdvancedClickerDescription.Text = keyboard
            ? "Select the keyboard key the auto clicker presses."
            : "Select the mouse button the auto clicker clicks.";
        ClickerTypeLabel.Text = keyboard ? "Keyboard" : "Mouse";
        MouseButtonCombo.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
        KeyboardTargetBox.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAdvancedStateVisuals()
    {
        var delayMode = AdvancedDelayMode.IsChecked == true;
        AdvancedRateOptions.Visibility = delayMode ? Visibility.Collapsed : Visibility.Visible;
        AdvancedIntervalOptions.Visibility = delayMode ? Visibility.Visible : Visibility.Collapsed;
        AdvancedCadenceDescription.Text = delayMode
            ? "Set the exact time between each click."
            : "Changes how fast the auto clicker clicks.";

        AdvancedHotkeyDescription.Text = AdvancedHoldMode.IsChecked == true
            ? "Hold the hotkey to click. Release to stop."
            : "Press the hotkey to toggle the clicker on and off.";

        var timeLimit = AdvancedLimitTime.IsChecked == true;
        AdvancedClickLimitEditor.Visibility = timeLimit ? Visibility.Collapsed : Visibility.Visible;
        AdvancedTimeLimitEditor.Visibility = timeLimit ? Visibility.Visible : Visibility.Collapsed;

        var continuousHold = AdvancedDutyHoldMode.IsChecked == true;
        AdvancedCadenceCard.IsEnabled = !continuousHold;
        AdvancedDutyClickOptions.Visibility = continuousHold ? Visibility.Collapsed : Visibility.Visible;
        AdvancedDutyDescription.Text = continuousHold
            ? "Holds the button down continuously. Click speed is disabled."
            : "Controls how long the button is held during each click.";
    }

    private void UpdateAdvancedSummaries()
    {
        if (AdvancedIntervalText is null || AdvancedRateSummaryText is null)
        {
            return;
        }

        var clicksPerSecond = AdvancedDelayMode.IsChecked == true
            ? 1000d / GetIntervalInputMilliseconds()
            : GetRateInputClicksPerSecond();
        var interval = 1000d / Math.Clamp(clicksPerSecond, 0.000001, 1000);
        AdvancedIntervalText.Text = $"{interval:0.##}ms interval";
        AdvancedRateSummaryText.Text = $"{clicksPerSecond:0.##} clicks per second";
    }

    private double GetRateInputClicksPerSecond()
    {
        var visibleRate = ParseDouble(AdvancedCadenceValueBox.Text, 10, 0.1, 60_000);
        var periodValue = ParseDouble(AdvancedPeriodValueBox.Text, 1, 0.01, 60_000);
        return Math.Clamp(
            ToClicksPerSecond(visibleRate, SelectedValue(AdvancedCadenceUnitCombo, "Second")) / periodValue,
            0.000001,
            1000);
    }

    private double GetIntervalInputMilliseconds()
    {
        var hours = ParseInt(AdvancedIntervalHoursBox.Text, 0, 0, 23);
        var minutes = ParseInt(AdvancedIntervalMinutesBox.Text, 0, 0, 59);
        var seconds = ParseInt(AdvancedIntervalSecondsBox.Text, 0, 0, 59);
        var milliseconds = ParseInt(AdvancedIntervalMillisecondsBox.Text, 0, 0, 999);
        return Math.Clamp(
            (hours * 3_600_000d) + (minutes * 60_000d) + (seconds * 1000d) + milliseconds,
            1,
            86_400_000);
    }

    private void SetIntervalFields(double totalMilliseconds)
    {
        var remaining = (long)Math.Round(Math.Clamp(totalMilliseconds, 1, 86_400_000));
        var hours = remaining / 3_600_000;
        remaining %= 3_600_000;
        var minutes = remaining / 60_000;
        remaining %= 60_000;
        var seconds = remaining / 1000;
        var milliseconds = remaining % 1000;
        AdvancedIntervalHoursBox.Text = hours.ToString(CultureInfo.InvariantCulture);
        AdvancedIntervalMinutesBox.Text = minutes.ToString(CultureInfo.InvariantCulture);
        AdvancedIntervalSecondsBox.Text = seconds.ToString(CultureInfo.InvariantCulture);
        AdvancedIntervalMillisecondsBox.Text = milliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private void EditHotkey_Click(object sender, RoutedEventArgs e) => AdvancedHotkeyInputBox.Focus();

    private void EditKeyboardKey_Click(object sender, RoutedEventArgs e) => AdvancedKeyboardKeyBox.Focus();

    private void KeyboardTarget_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _activeKeyboardTargetBox = (TextBox)sender;
        _keyboardTargetBeforeCapture = KeyDisplayName(ParseKeyboardTarget(_settings.KeyboardKey), false);
        _isCapturingKeyboardTarget = true;
        _activeKeyboardTargetBox.Text = "Press a key...";
    }

    private void KeyboardTarget_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isCapturingKeyboardTarget)
        {
            return;
        }

        _isCapturingKeyboardTarget = false;
        SetKeyboardTargetDisplays(_keyboardTargetBeforeCapture);
        _activeKeyboardTargetBox = null;
    }

    private void KeyboardTarget_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == Key.Escape)
        {
            _isCapturingKeyboardTarget = false;
            SetKeyboardTargetDisplays(_keyboardTargetBeforeCapture);
        }
        else
        {
            _settings.KeyboardKey = key.ToString();
            SetKeyboardTargetDisplays(KeyDisplayName(key, false));
            _isCapturingKeyboardTarget = false;
            ScheduleSave();
        }

        _activeKeyboardTargetBox = null;
        Keyboard.ClearFocus();
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

    private void SetHotkeyDisplays(string value)
    {
        HotkeyInputBox.Text = value;
        AdvancedHotkeyInputBox.Text = value;
    }

    private void SetKeyboardTargetDisplays(string value)
    {
        KeyboardTargetBox.Text = value;
        AdvancedKeyboardKeyBox.Text = value;
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

    private string SelectedLimitTimeUnit()
    {
        if (AdvancedLimitHours.IsChecked == true)
        {
            return "Hour";
        }

        return AdvancedLimitMinutes.IsChecked == true ? "Minute" : "Second";
    }

    private void SelectLimitTimeUnit(string unit)
    {
        AdvancedLimitHours.IsChecked = string.Equals(unit, "Hour", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitMinutes.IsChecked = string.Equals(unit, "Minute", StringComparison.OrdinalIgnoreCase);
        AdvancedLimitSeconds.IsChecked = AdvancedLimitHours.IsChecked != true && AdvancedLimitMinutes.IsChecked != true;
    }

    private static double ParseDouble(string value, double fallback, double minimum, double maximum) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static int ParseInt(string value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static Key ParseKeyboardTarget(string key) =>
        Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.Space;

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
