namespace Vanta.Models;

public sealed class AppSettings
{
    public double CadenceValue { get; set; } = 10;
    public string CadencePeriod { get; set; } = "Second";
    public double? CadenceDisplayValue { get; set; }
    public double CadencePeriodValue { get; set; } = 1;
    public bool IsDelayMode { get; set; }
    public string HotKeyModifier { get; set; } = "Alt";
    public string HotKey { get; set; } = "Q";
    public string ActivationMode { get; set; } = "Toggle";
    public string ClickerType { get; set; } = "Mouse";
    public string MouseButton { get; set; } = "Left";
    public string KeyboardKey { get; set; } = "Space";
    public int ClickDurationPercent { get; set; } = 15;
    public string DutyCycleMode { get; set; } = "Click";
    public bool LimitEnabled { get; set; }
    public int LimitValue { get; set; } = 1000;
    public string LimitType { get; set; } = "Clicks";
    public int? LimitClicksValue { get; set; }
    public int? LimitTimeValue { get; set; }
    public string LimitTimeUnit { get; set; } = "Second";
    public bool VariationEnabled { get; set; }
    public int VariationPercent { get; set; } = 10;
    public bool DoubleClickEnabled { get; set; }
    public int DoubleClickGapMs { get; set; } = 50;
    public bool SequenceEnabled { get; set; }
    public List<ScreenPoint> SequencePoints { get; set; } = [];
    public bool StartWithWindows { get; set; }
    public bool RememberWindowPosition { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public AppSettings CreateClickingSnapshot()
    {
        return new AppSettings
        {
            CadenceValue = CadenceValue,
            CadencePeriod = CadencePeriod,
            CadenceDisplayValue = CadenceDisplayValue,
            CadencePeriodValue = CadencePeriodValue,
            IsDelayMode = IsDelayMode,
            HotKeyModifier = HotKeyModifier,
            HotKey = HotKey,
            ActivationMode = ActivationMode,
            ClickerType = ClickerType,
            MouseButton = MouseButton,
            KeyboardKey = KeyboardKey,
            ClickDurationPercent = ClickDurationPercent,
            DutyCycleMode = DutyCycleMode,
            LimitEnabled = LimitEnabled,
            LimitValue = LimitValue,
            LimitType = LimitType,
            LimitClicksValue = LimitClicksValue,
            LimitTimeValue = LimitTimeValue,
            LimitTimeUnit = LimitTimeUnit,
            VariationEnabled = VariationEnabled,
            VariationPercent = VariationPercent,
            DoubleClickEnabled = DoubleClickEnabled,
            DoubleClickGapMs = DoubleClickGapMs,
            SequenceEnabled = SequenceEnabled,
            SequencePoints = SequencePoints.Select(point => new ScreenPoint { X = point.X, Y = point.Y }).ToList()
        };
    }
}

public sealed class ScreenPoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public override string ToString() => $"X {X:N0}   Y {Y:N0}";
}
