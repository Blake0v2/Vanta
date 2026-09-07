using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;
using Vanta.Interop;

namespace Vanta.Services;

internal sealed class GlobalHotKeyService : IDisposable
{
    private const int HotKeyId = 0x5641;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _registered;

    public event EventHandler? Pressed;

    public void Attach(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowProc);
    }

    public void Register(string modifier, string key)
    {
        Unregister();

        var modifiers = ParseModifiers(modifier);

        var virtualKey = KeyInterop.VirtualKeyFromKey(ParseKey(key));
        if (!NativeMethods.RegisterHotKey(_windowHandle, HotKeyId, modifiers | NativeMethods.ModNoRepeat, (uint)virtualKey))
        {
            throw new Win32Exception("That hotkey is already in use by another application.");
        }

        _registered = true;
    }

    public static int GetVirtualKey(string key) => KeyInterop.VirtualKeyFromKey(ParseKey(key));

    private static Key ParseKey(string key)
    {
        return Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.Q;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ParseModifiers(string modifiers)
    {
        var flags = 0u;
        foreach (var modifier in modifiers.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            flags |= modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => NativeMethods.ModControl,
                "alt" => NativeMethods.ModAlt,
                "shift" => NativeMethods.ModShift,
                "win" or "windows" => NativeMethods.ModWin,
                _ => 0u
            };
        }

        return flags;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotKeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WindowProc);
    }
}
