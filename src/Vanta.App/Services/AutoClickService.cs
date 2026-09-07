using System.Diagnostics;
using System.Runtime.InteropServices;
using Vanta.Interop;
using Vanta.Models;

namespace Vanta.Services;

internal sealed class AutoClickService : IDisposable
{
    private readonly Random _random = new();
    private CancellationTokenSource? _cancellation;
    private long _clickCount;

    public bool IsRunning => _cancellation is not null;
    public long ClickCount => Interlocked.Read(ref _clickCount);

    public event EventHandler<long>? Clicked;
    public event EventHandler<string>? Stopped;

    public void Start(AppSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        _clickCount = 0;
        _cancellation = new CancellationTokenSource();
        _ = RunAsync(settings.CreateClickingSnapshot(), _cancellation.Token);
    }

    public void Stop(string reason = "Ready")
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
        Stopped?.Invoke(this, reason);
    }

    private async Task RunAsync(AppSettings settings, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var sequenceIndex = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (settings.SequenceEnabled && settings.SequencePoints.Count > 0)
                {
                    var point = settings.SequencePoints[sequenceIndex % settings.SequencePoints.Count];
                    NativeMethods.SetCursorPos(point.X, point.Y);
                    sequenceIndex++;
                }

                await SendTargetInputAsync(settings, GetBaseInterval(settings), token);
                var count = Interlocked.Increment(ref _clickCount);
                Clicked?.Invoke(this, count);

                if (settings.DoubleClickEnabled && string.Equals(settings.ClickerType, "Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(Math.Clamp(settings.DoubleClickGapMs, 1, 1000), token);
                    await SendTargetInputAsync(settings, GetBaseInterval(settings), token);
                    count = Interlocked.Increment(ref _clickCount);
                    Clicked?.Invoke(this, count);
                }

                if (settings.LimitEnabled)
                {
                    var reachedLimit = settings.LimitType == "Seconds"
                        ? stopwatch.Elapsed.TotalSeconds >= settings.LimitValue
                        : count >= settings.LimitValue;

                    if (reachedLimit)
                    {
                        Stop("Limit reached");
                        return;
                    }
                }

                var interval = GetBaseInterval(settings);
                var variation = settings.VariationEnabled
                    ? interval * (settings.VariationPercent / 100d) * ((_random.NextDouble() * 2) - 1)
                    : 0;
                var holdDuration = GetHoldDuration(settings.ClickDurationPercent, interval);
                var delay = Math.Max(1, interval + variation - holdDuration);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected whenever clicking is stopped.
        }
        catch (Exception exception)
        {
            Stop($"Stopped: {exception.Message}");
        }
    }

    private static double GetBaseInterval(AppSettings settings)
    {
        return settings.IsDelayMode
            ? Math.Clamp(settings.CadenceValue, 1, 60_000)
            : 1000d / Math.Clamp(settings.CadenceValue, 0.000001, 1000);
    }

    private static Task SendTargetInputAsync(AppSettings settings, double intervalMs, CancellationToken token)
    {
        return string.Equals(settings.ClickerType, "Keyboard", StringComparison.OrdinalIgnoreCase)
            ? SendKeyAsync(settings.KeyboardKey, settings.ClickDurationPercent, intervalMs, token)
            : SendClickAsync(settings.MouseButton, settings.ClickDurationPercent, intervalMs, token);
    }

    private static async Task SendClickAsync(string button, int durationPercent, double intervalMs, CancellationToken token)
    {
        var (down, up) = button switch
        {
            "Right" => (NativeMethods.MouseEventRightDown, NativeMethods.MouseEventRightUp),
            "Middle" => (NativeMethods.MouseEventMiddleDown, NativeMethods.MouseEventMiddleUp),
            _ => (NativeMethods.MouseEventLeftDown, NativeMethods.MouseEventLeftUp)
        };

        SendMouseInput(down);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(GetHoldDuration(durationPercent, intervalMs)), token);
        }
        finally
        {
            // Never leave a mouse button held if the user stops during the down interval.
            SendMouseInput(up);
        }
    }

    private static double GetHoldDuration(int durationPercent, double intervalMs) =>
        Math.Clamp(intervalMs * Math.Clamp(durationPercent, 1, 100) / 100d, 1, 250);

    private static void SendMouseInput(uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.INPUTUNION
                {
                    MouseInput = new NativeMethods.MOUSEINPUT { Flags = flags }
                }
            }
        };

        if (NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
        {
            throw new InvalidOperationException("Windows rejected the simulated mouse input.");
        }
    }

    private static async Task SendKeyAsync(string key, int durationPercent, double intervalMs, CancellationToken token)
    {
        var virtualKey = (ushort)GlobalHotKeyService.GetVirtualKey(key);
        SendKeyboardInput(virtualKey, 0);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(GetHoldDuration(durationPercent, intervalMs)), token);
        }
        finally
        {
            // Never leave a key held if the user stops during the down interval.
            SendKeyboardInput(virtualKey, NativeMethods.KeyEventKeyUp);
        }
    }

    private static void SendKeyboardInput(ushort virtualKey, uint flags)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeMethods.INPUTUNION
                {
                    KeyboardInput = new NativeMethods.KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        Flags = flags
                    }
                }
            }
        };

        if (NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
        {
            throw new InvalidOperationException("Windows rejected the simulated keyboard input.");
        }
    }

    public void Dispose() => Stop();
}
